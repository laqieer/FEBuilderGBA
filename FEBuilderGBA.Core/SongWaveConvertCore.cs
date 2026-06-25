// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform DirectSound wav-import conversion helpers (#1448).
//
// De-UI'd VERBATIM ports of the three pure WinForms wave-conversion algorithms
// that the WinForms SongInstrumentImportWaveForm pipeline used but that had no
// Core equivalent (the Avalonia wave-import path could only append raw bytes and
// rejected anything above 8-bit):
//   * SongUtilDPCM.wavToDPCMByte (+ dpcm_lookahead / Add0x80 / squared)
//                                          -> WavToDPCMByte   (DPCM compress)
//   * SongUtil.CalculateSNR                -> CalculateSNR    (quality metric)
//   * SongUtil.LoadWavS                    -> LoadWavS        (.s assembly load)
//   * PatchUtil.Search_m4a_hq_mixerLow     -> HasHqMixer      (advisory gate)
//
// These are PURE algorithms: WAV bytes in -> bytes/number out. No external
// tool, no UI. The sox resample/normalize step (genuinely external) lives in
// SongSoxConvertCore. The GBA-sample DECODE side (ByteToWavForDPCM / ByteToWav /
// WavToByte) already lives in SongDirectSoundWavCore — this file is the ENCODE /
// preview / .s-load side that was missing.
//
// WF-parity faithfulness: the four helpers are byte/value-identical to the
// WinForms originals; the only differences are de-UI'ing (no R.ShowStopError /
// no PatchUtil UI gate inside the encoder) and bounds/never-throw hardening.
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform DirectSound wav-import conversion helpers (#1448): the
    /// ENCODE / quality-preview side of the wave pipeline (the decode side lives
    /// in <see cref="SongDirectSoundWavCore"/>). Pure static ports of the WinForms
    /// <c>SongUtilDPCM</c> / <c>SongUtil.CalculateSNR</c> / <c>SongUtil.LoadWavS</c>
    /// algorithms plus the m4a-HQ-mixer advisory gate. Every entry point is
    /// bounds-guarded and never throws.
    /// </summary>
    public static class SongWaveConvertCore
    {
        // DPCM delta lookup table (verbatim from WF SongUtilDPCM). The DECODE
        // table in SongDirectSoundWavCore is the same constant.
        static readonly int[] DpcmLookupTable = new int[]
            { 0, 1, 4, 9, 16, 25, 36, 49, -64, -49, -36, -25, -16, -9, -4, -1 };

        const uint DPCM_BLK_SIZE = 0x40; // 64 samples per DPCM block

        static int Squared(int x) { return x * x; }

        // ------------------------------------------------------------------
        // High-level orchestration — ports WF SongInstrumentImportWaveForm.RunSox
        // + Preview, GUI-free. The Avalonia/WinForms dialog calls these so the
        // sox -> (raw | DPCM) -> GBA-sample flow lives in ONE tested place.
        // ------------------------------------------------------------------

        /// <summary>Outcome of <see cref="Convert"/> / <see cref="Preview"/>.</summary>
        public class ConvertResult
        {
            /// <summary>Ready-to-append GBA DirectSound sample bytes (header +
            /// 8-bit PCM, or the DPCM block stream). <c>null</c> on failure.</summary>
            public byte[] SampleBytes;
            /// <summary><c>true</c> when the result is DPCM-compressed.</summary>
            public bool IsDpcm;
            /// <summary>The sox-converted (or original) RIFF/WAVE bytes — the
            /// pre-encode 8-bit PCM wave, used as the SNR reference. <c>null</c>
            /// when conversion failed before producing a wave.</summary>
            public byte[] ConvertedWav;
            /// <summary>Non-null localized error on failure; <c>null</c> on success.</summary>
            public string Error;
        }

        /// <summary>
        /// Full WF-parity conversion: optional sox resample/normalize
        /// (<paramref name="channel"/>/<paramref name="hz"/>/<paramref name="strip"/>/<paramref name="volume100"/>)
        /// then either raw 8-bit PCM (<see cref="SongDirectSoundWavCore.WavToByte"/>)
        /// or DPCM (<see cref="WavToDPCMByte"/>) per <paramref name="useDpcm"/>.
        /// <para>WF gating parity (#1448 review): DPCM is only attempted when
        /// <paramref name="hqMixerAvailable"/> is <c>true</c> (the m4a HQ-mixer
        /// patch is installed); otherwise it SILENTLY falls back to raw PCM — never
        /// producing a DPCM sample the user cannot play. The dialog disables the
        /// DPCM option when the patch is absent, so this is a defensive backstop.</para>
        /// <para>Returns a <see cref="ConvertResult"/>; on any failure
        /// <see cref="ConvertResult.SampleBytes"/> is <c>null</c> and
        /// <see cref="ConvertResult.Error"/> is set. Never throws.</para>
        /// </summary>
        public static ConvertResult Convert(
            byte[] wavBytes, uint channel, uint hz, uint strip, uint volume100,
            bool useDpcm, uint lookahead, bool hqMixerAvailable)
        {
            var r = new ConvertResult();
            try
            {
                if (wavBytes == null || wavBytes.Length < 4)
                {
                    r.Error = R._("Not a Wave file. The data is too small.");
                    return r;
                }

                // 1) sox resample/normalize (no-op early-exit when all defaults).
                byte[] wav = SongSoxConvertCore.ConvertWaveBySox(
                    wavBytes, channel, hz, strip, volume100, out string soxErr);
                if (wav == null)
                {
                    r.Error = soxErr ?? R._("Wave conversion failed.");
                    return r;
                }
                r.ConvertedWav = wav;

                // 2) encode. DPCM only when requested AND the HQ mixer exists.
                if (useDpcm && hqMixerAvailable)
                {
                    byte[] dpcm = WavToDPCMByte(wav, lookahead, out string dpcmErr);
                    if (dpcm == null)
                    {
                        r.Error = dpcmErr ?? R._("DPCM compression failed.");
                        return r;
                    }
                    r.SampleBytes = dpcm;
                    r.IsDpcm = true;
                    return r;
                }

                byte[] raw = SongDirectSoundWavCore.WavToByte(wav, out string rawErr);
                if (raw == null)
                {
                    r.Error = rawErr ?? R._("Wave conversion failed.");
                    return r;
                }
                r.SampleBytes = raw;
                r.IsDpcm = false;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = R._("Wave conversion failed: {0}", ex.Message);
                return r;
            }
        }

        /// <summary>Preview metrics shown in the import dialog (size delta + SNR).</summary>
        public class PreviewResult
        {
            public long OriginalSize;
            public long ResultSize;
            public bool IsDpcm;
            /// <summary>SNR in dB (DPCM only); <see cref="double.NaN"/> for raw PCM.</summary>
            public double Snr = double.NaN;
            public string Error;
        }

        /// <summary>
        /// Compute the import-dialog preview (ports WF
        /// <c>SongInstrumentImportWaveForm.Preview_button_Click</c>): runs
        /// <see cref="Convert"/>, reports the on-ROM sample byte count vs the
        /// original file size, and — for a DPCM result — the SNR (dB) of the
        /// DPCM-decoded wave against the pre-encode wave (higher is better; the WF
        /// dialog warns &lt;= 20 dB usually is not worth compressing). Never throws.
        /// </summary>
        public static PreviewResult Preview(
            byte[] wavBytes, uint channel, uint hz, uint strip, uint volume100,
            bool useDpcm, uint lookahead, bool hqMixerAvailable)
        {
            var p = new PreviewResult();
            ConvertResult c = Convert(wavBytes, channel, hz, strip, volume100, useDpcm, lookahead, hqMixerAvailable);
            if (c.SampleBytes == null)
            {
                p.Error = c.Error;
                return p;
            }

            p.OriginalSize = wavBytes?.Length ?? 0;
            p.ResultSize = c.SampleBytes.Length;
            p.IsDpcm = c.IsDpcm;

            if (c.IsDpcm)
            {
                // Decode the DPCM sample back to a wave and compare against the
                // pre-encode (sox-converted) wave — exactly the WF SNR reference.
                byte[] decodedWav = SongDirectSoundWavCore.ByteToWavForDPCM(c.SampleBytes, 0);
                if (decodedWav != null && c.ConvertedWav != null)
                {
                    p.Snr = CalculateSNR(c.ConvertedWav, decodedWav);
                }
            }
            return p;
        }

        // ------------------------------------------------------------------
        // DPCM ENCODE — VERBATIM port of WF SongUtilDPCM.wavToDPCMByte
        // ------------------------------------------------------------------

        /// <summary>
        /// De-UI'd VERBATIM port of WinForms <c>SongUtilDPCM.wavToDPCMByte</c>:
        /// convert a RIFF/WAVE (8-bit mono PCM) byte array into a DPCM-compressed
        /// GBA DirectSound sample (header <c>0x01</c> + <c>freq*1024</c> + length +
        /// the 33-byte/64-sample delta block stream). Uses the recursive
        /// <paramref name="lookahead"/> optimizer (default 3 in the WF dialog) to
        /// pick the lowest-error delta sequence.
        /// <para>The result is byte-identical to the WF encoder and DECODES back
        /// through <see cref="SongDirectSoundWavCore.ByteToWavForDPCM"/>. Replaces
        /// the WF <c>R.ShowStopError</c> with the <paramref name="error"/> out-param
        /// (set + <c>null</c> on a non-WAV / too-small / &gt;8-bit input) and drops
        /// the WF <c>PatchUtil</c> UI gate entirely (the caller owns any advisory).
        /// </para>
        /// <para>Never throws — any exception is caught -&gt; <paramref name="error"/>
        /// + <c>null</c>.</para>
        /// </summary>
        public static byte[] WavToDPCMByte(byte[] wavBytes, uint lookahead, out string error)
        {
            error = null;
            try
            {
                byte[] data = wavBytes;
                if (data == null || data.Length < 4
                    || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
                {
                    error = R._("Not a Wave file. The RIFF header is missing.");
                    return null;
                }
                if (data.Length < (44 + 1))
                {
                    error = R._("Not a Wave file. The data is too small.");
                    return null;
                }

                uint fmt_samples_per_sec = U.u32(data, 24);
                uint fmt_bits_per_sample = U.u16(data, 34);
                uint data_chunk_size = U.u32(data, 40);
                if (data_chunk_size > data.Length - 44)
                {//チャンクのデータサイズが不正だったら修正する. (clamp a bogus chunk size)
                    data_chunk_size = (uint)(data.Length - 44);
                }
                if (data_chunk_size <= 1)
                {
                    error = R._("Not a Wave file. data_chunk_size ({0}) is too small.", data_chunk_size);
                    return null;
                }
                if (fmt_bits_per_sample > 8)
                {//サンプルビット数が8ビットを超える (more than 8-bit)
                    error = R._("The Wave file is too high quality. {0}bit\r\nPlease use about 8bit 12khz mono.", fmt_bits_per_sample);
                    return null;
                }

                // The WF dialog ships 3 as the default lookahead and never 0.
                // A 0 lookahead would zero every delta (silence); clamp to 1.
                if (lookahead == 0) lookahead = 1;

                List<byte> wave = new List<byte>();
                U.append_u32(wave, 0x01);                          // compression flag (DPCM)
                U.append_u32(wave, fmt_samples_per_sec * 1024);    // freq*1024
                U.append_u32(wave, 0);                             // unknown
                U.append_u32(wave, data_chunk_size);               // original PCM length

                long loopEnd = data_chunk_size; // no-loop import: loopEnd == data length

                for (long i = 0; i < loopEnd; i += DPCM_BLK_SIZE)
                {
                    uint waveDataI = (uint)(44 + i);
                    byte[] wavBin = U.getBinaryData(data, waveDataI, DPCM_BLK_SIZE);
                    int[] ds = Add0x80(wavBin);

                    int s = ds[0];
                    U.append_u8(wave, (byte)(s & 0xFF));

                    uint innerLoopCount = 1;
                    byte outData = 0;
                    while (innerLoopCount < DPCM_BLK_SIZE)
                    {
                        uint sampleBufReadLen = Math.Min(lookahead, DPCM_BLK_SIZE - innerLoopCount);
                        int minimumError;
                        uint minimumErrorIndex;
                        if (innerLoopCount != 1)
                        {//どういうわけか、最初の1バイトは特殊な格納方法をする (first nibble special)
                            DpcmLookahead(out minimumError, out minimumErrorIndex,
                                ds, innerLoopCount, sampleBufReadLen, s);
                            outData = (byte)((minimumErrorIndex & 0xF) << 4);
                            s += DpcmLookupTable[minimumErrorIndex];
                            innerLoopCount += 1;
                        }

                        sampleBufReadLen = Math.Min(lookahead, DPCM_BLK_SIZE - innerLoopCount);
                        DpcmLookahead(out minimumError, out minimumErrorIndex,
                            ds, innerLoopCount, sampleBufReadLen, s);
                        outData |= (byte)(minimumErrorIndex & 0xF);
                        s += DpcmLookupTable[minimumErrorIndex];
                        innerLoopCount += 1;

                        U.append_u8(wave, (byte)(outData & 0xFF));
                    }
                }

                return wave.ToArray();
            }
            catch (Exception ex)
            {
                error = R._("Failed to read the Wave file: {0}", ex.Message);
                return null;
            }
        }

        // Recursive lookahead delta optimizer — VERBATIM from WF dpcm_lookahead.
        static void DpcmLookahead(
            out int minimumError, out uint minimumErrorIndex,
            int[] sampleBuf, uint startIndex, uint lookahead, int prevLevel)
        {
            if (lookahead == 0)
            {
                minimumError = 0;
                minimumErrorIndex = 0;
                return;
            }

            minimumError = int.MaxValue;
            minimumErrorIndex = 0; //一度も補正されない時があるので 0 を設定するべき (default 0)
            for (uint i = 0; i < DpcmLookupTable.Length; i++)
            {
                int newLevel = prevLevel + DpcmLookupTable[i];

                int s = sampleBuf[startIndex];
                int errorEstimation = Squared(s - newLevel);
                if (errorEstimation >= minimumError)
                {
                    continue;
                }

                int recMinimumError;
                uint recMinimumErrorIndex;
                DpcmLookahead(out recMinimumError, out recMinimumErrorIndex,
                    sampleBuf, startIndex + 1, lookahead - 1, newLevel);

                int error = Squared(s - newLevel) + recMinimumError;
                if (error < minimumError)
                {
                    if (newLevel <= 127 && newLevel >= -128)
                    {
                        minimumError = error;
                        minimumErrorIndex = i;
                    }
                }
            }
        }

        // Convert unsigned WAV bytes to signed samples — VERBATIM from WF Add0x80.
        static int[] Add0x80(byte[] s)
        {
            int[] ds = new int[DPCM_BLK_SIZE];
            for (int i = 0; i < s.Length; i++)
            {
                int dd = ((int)s[i]) + 0x80;
                ds[i] = (sbyte)dd;
            }
            return ds;
        }

        // ------------------------------------------------------------------
        // SNR — VERBATIM port of WF SongUtil.CalculateSNR
        // ------------------------------------------------------------------

        /// <summary>
        /// VERBATIM port of WinForms <c>SongUtil.CalculateSNR</c>: the
        /// signal-to-noise ratio (dB) between an original WAV
        /// (<paramref name="sourceData"/>) and a lossily-compressed-then-decoded
        /// WAV (<paramref name="decompressData"/>). Higher is better; the WF dialog
        /// warns that &lt;= 20 dB usually means DPCM is not worth it. Compares from
        /// byte <c>0x44</c> (skips the RIFF/WAVE header) over the shorter length,
        /// and returns <c>100</c> on an exact match (avoids +Inf).
        /// <para>Bounds-guarded; returns <c>0</c> on null/short input. Never throws.</para>
        /// </summary>
        public static double CalculateSNR(byte[] sourceData, byte[] decompressData)
        {
            if (sourceData == null || decompressData == null) return 0;

            long sum_son = 0;
            long sum_mum = 0;
            int max = Math.Min(sourceData.Length, decompressData.Length);

            // No PCM body to compare (both WAVs shorter than the 0x44 header
            // start, or no overlap past it): return 0, NOT the 100 "exact match"
            // value (Copilot review #1537 — the empty loop would leave sum_mum==0
            // and misreport a truncated/invalid WAV as a perfect match).
            if (max <= 0x44) return 0;

            for (int i = 0x44; i < max; i++)
            {
                sum_son += ((long)decompressData[i]) * ((long)decompressData[i]);
                long sub = ((long)decompressData[i]) - ((long)sourceData[i]);
                sum_mum += sub * sub;
            }

            if (sum_mum == 0)
            {
                return 100;
            }

            double snr = 10 * Math.Log10((double)sum_son / (double)sum_mum);
            return snr;
        }

        // ------------------------------------------------------------------
        // .s assembly load — VERBATIM port of WF SongUtil.LoadWavS
        // ------------------------------------------------------------------

        /// <summary>
        /// VERBATIM port of WinForms <c>SongUtil.LoadWavS</c>: assemble the raw GBA
        /// DirectSound sample bytes from a <c>.s</c> assembly source whose
        /// <c>.byte</c> / <c>.word</c> directives carry the sample data (the format
        /// sox+DPCM tooling and some external converters emit). Strips <c>@</c>/<c>#</c>
        /// comments; accepts decimal, negative-decimal, and <c>0x</c> hex byte tokens.
        /// <para>Returns <c>true</c> on success with <paramref name="data"/> set;
        /// on a non-numeric token sets <paramref name="error"/> + returns <c>false</c>
        /// (matches the WF <c>R.Error</c> message shape). Never throws.</para>
        /// </summary>
        public static bool LoadWavS(string[] lines, out byte[] data, out string error)
        {
            error = null;
            data = Array.Empty<byte>();
            try
            {
                List<byte> list = new List<byte>();
                Dictionary<string, byte> byteMap = new Dictionary<string, byte>();
                for (int i = 0; i < 256; i++)
                {
                    byteMap[i.ToString()] = (byte)i;
                    byteMap[(0 - i).ToString()] = (byte)((0 - i) & 0xFF);
                    byteMap["0x" + i.ToString("X02")] = (byte)i;
                    byteMap["0x" + i.ToString("x02")] = (byte)i;
                }
                for (int i = 0; i < 16; i++)
                {
                    byteMap["0x" + i.ToString("X1")] = (byte)i;
                    byteMap["0x" + i.ToString("x1")] = (byte)i;
                }

                if (lines == null)
                {
                    error = R._("The Wave file is too small.");
                    return false;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null) continue;
                    line = ClipCommentWithCharpAndAtmark(line);

                    if (line.Length <= 1)
                        continue;

                    string[] token = line.Split(new string[] { " ", "\t", "," }, StringSplitOptions.RemoveEmptyEntries);
                    if (token.Length <= 0)
                        continue;

                    if (token[0] == ".byte")
                    {
                        for (int n = 1; n < token.Length; n++)
                        {
                            string v = token[n].Trim();
                            byte vv;
                            if (!byteMap.TryGetValue(v, out vv))
                            {
                                data = Array.Empty<byte>();
                                error = R._("{0} line {1}: contains a non-numeric value ({2}).", "(.s)", i + 1, v);
                                return false;
                            }
                            list.Add(vv);
                        }
                    }
                    else if (token[0] == ".word")
                    {
                        for (int n = 1; n < token.Length; n++)
                        {
                            string v = token[n].Trim();
                            uint vv = U.atoi0x(v);
                            U.append_u32(list, vv);
                        }
                    }
                }

                data = list.ToArray();
                return true;
            }
            catch (Exception ex)
            {
                data = Array.Empty<byte>();
                error = R._("Failed to read the Wave file: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// File-path overload of <see cref="LoadWavS(string[],out byte[],out string)"/>:
        /// reads <paramref name="filename"/> line-by-line then delegates. Returns
        /// <c>false</c> with <paramref name="error"/> set on a missing/unreadable
        /// file. Never throws.
        /// </summary>
        public static bool LoadWavS(string filename, out byte[] data, out string error)
        {
            error = null;
            data = Array.Empty<byte>();
            try
            {
                if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
                {
                    error = R._("The file does not exist: {0}", filename ?? "");
                    return false;
                }
                string[] lines = File.ReadAllLines(filename);
                return LoadWavS(lines, out data, out error);
            }
            catch (Exception ex)
            {
                data = Array.Empty<byte>();
                error = R._("Failed to read the Wave file: {0}", ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // m4a HQ mixer advisory gate — port of WF Search_m4a_hq_mixerLow
        // ------------------------------------------------------------------

        /// <summary>
        /// Port of WinForms <c>PatchUtil.Search_m4a_hq_mixerLow</c> via the existing
        /// <see cref="PatchDetection.SearchPatchBool(ROM, PatchDetection.PatchTableSt[])"/>:
        /// is the m4a HQ sound-mixer patch installed in <paramref name="rom"/>?
        /// DPCM-compressed samples only PLAY CORRECTLY with the HQ mixer, so the
        /// import dialog uses this for a NON-BLOCKING advisory (it does not refuse
        /// the import). Bounds-guarded; <c>false</c> on null ROM. Never throws.
        /// </summary>
        public static bool HasHqMixer(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            PatchDetection.PatchTableSt[] table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt{ name="m4a_hq_mixer", ver="FE8J", addr=0xD4ECC, data=new byte[]{0x96, 0x02}},
                new PatchDetection.PatchTableSt{ name="m4a_hq_mixer", ver="FE8U", addr=0xD01D4, data=new byte[]{0x96, 0x02}},
                new PatchDetection.PatchTableSt{ name="m4a_hq_mixer", ver="FE7J", addr=0xBF0B0, data=new byte[]{0x96, 0x02}},
                new PatchDetection.PatchTableSt{ name="m4a_hq_mixer", ver="FE7U", addr=0xBE56C, data=new byte[]{0x96, 0x02}},
                new PatchDetection.PatchTableSt{ name="m4a_hq_mixer", ver="FE6",  addr=0x9C838, data=new byte[]{0x96, 0x02}},
            };
            return PatchDetection.SearchPatchBool(rom, table);
        }

        // ------------------------------------------------------------------
        // Comment strip — inlined port of WF U.ClipCommentWithCharpAndAtmark
        // (Core's U.ClipComment only strips '#'; the .s sample sources carry
        // '@' GAS comments too, so we need the full WF order here).
        // ------------------------------------------------------------------
        static string ClipCommentWithCharpAndAtmark(string str)
        {
            if (str == null) return "";
            str = ClipAt(str, "{J}");
            str = ClipAt(str, "{U}");
            str = ClipAt(str, "//");
            str = ClipAt(str, "#");
            str = ClipAt(str, "@");
            return str;
        }

        static string ClipAt(string str, string token)
        {
            int term = str.IndexOf(token, StringComparison.Ordinal);
            if (term >= 0) return str.Substring(0, term);
            return str;
        }
    }
}
