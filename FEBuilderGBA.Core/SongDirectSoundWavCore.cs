// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform GBA DirectSound sample <-> WAV seam (#1057, N00 slice).
//
// VERBATIM port of the four WinForms SongUtil helpers (do NOT "improve"):
//   * byteToWav                       -> ByteToWav
//   * byteToWavForDPCM                -> ByteToWavForDPCM
//   * IsDirectSoundWaveCompressedDPCM -> IsDirectSoundWaveCompressedDPCM
//   * wavToByte                       -> WavToByte   (de-UI'd: no R.ShowStopError /
//                                                     R.ShowNoYes / DialogResult /
//                                                     too-big confirm prompt)
// plus two ROM-aware wrappers:
//   * ExportWave  (READ-ONLY)   — pick DPCM vs raw decode by the compression flag
//   * ImportWave  (ROM-MUTATING) — RAW append at ROM end + P4 repoint, with the
//                                  #885/#923 length-aware byte-identical fault restore.
//
// GBA DirectSound sample header (RAW, NOT LZ77; DPCM is a separate raw codec):
//   +0  compression flag (0x01 == DPCM, else uncompressed 8-bit PCM)
//   +4  samples_per_sec * 1024  (frequency * 1024)
//  +12  sample length (bytes of PCM, uncompressed count)
//  +16  start of 8-bit PCM (or the DPCM block stream)
//
// 8-bit PCM convention is the EXACT WF one (tightening #2): on export each
// byte is stored signed two's-complement (dd = u8 - 0x80; (byte)dd), NOT the
// standard unsigned WAV convention. Import inverts it ((byte)((sbyte)x + 0x80)).
// The 0x80 lead byte + the data_chunk_size = len + 1 quirk are preserved.
//
// ROUND-TRIP RESIDUAL (tightening #5): a sample -> ByteToWav -> WavToByte pass
// is NOT byte-identical. ByteToWav emits data_chunk_size = len + 1 plus a single
// 0x80 lead-pad byte, so the WAV body is one byte shorter than its declared
// chunk size; WavToByte then CLAMPS that chunk size to the real body length and
// re-derives len = data_chunk_size - 1, dropping the final PCM byte. Each pass
// shrinks the sample by exactly one byte — there is no nonempty fixed point.
// What IS preserved exactly: the loop flag (0), freq * 1024, and the PCM body
// minus the clamped last byte. Tight / nonzero-pad / looped samples normalize
// to this legacy shape (loop flag 0, trailing zero present). DPCM does NOT
// round-trip byte-identically through WavToByte at all (separate raw codec).
// We do NOT "fix" the clamp here — this is a verbatim WF port.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform GBA DirectSound sample &lt;-&gt; WAV helpers (#1057 N00 slice).
    /// Pure static ports of the WinForms <c>SongUtil</c> wave helpers plus two
    /// ROM-aware wrappers. Every entry point is bounds-guarded and never throws.
    /// </summary>
    public static class SongDirectSoundWavCore
    {
        // ----- DPCM decode lookup (verbatim from WF byteToWavForDPCM) ---------
        static readonly int[] DpcmLookupTable = new int[]
            { 0, 1, 4, 9, 16, 25, 36, 49, -64, -49, -36, -25, -16, -9, -4, -1 };

        const uint DPCM_DECODE_SIZE = 0x1 + 0x20;

        /// <summary>
        /// VERBATIM port of WinForms <c>SongUtil.byteToWav</c>: decode an
        /// uncompressed 8-bit GBA DirectSound sample at <paramref name="waveOffset"/>
        /// into a RIFF/WAVE byte array.
        /// <para>Keeps the EXACT WF 8-bit PCM convention (tightening #2):
        /// <c>dd = u8(...) - 0x80; fp.Add((byte)dd)</c> — signed two's-complement,
        /// NOT standard unsigned WAV — plus the <c>0x80</c> lead byte and the
        /// <c>data_chunk_size = len + 1</c> quirk.</para>
        /// <para>Bounds-guarded: validates <c>waveOffset + 16 &lt;= Length</c> and
        /// <c>waveOffset + 16 + len &lt;= Length</c> before any indexed read;
        /// returns <c>null</c> on guard failure. Never throws.</para>
        /// </summary>
        public static byte[] ByteToWav(byte[] romData, uint waveOffset)
        {
            if (romData == null) return null;
            // Header occupies +4 (freq) and +12 (len) -> need +16 bytes present
            // before we read the PCM that starts at +16.
            if ((long)waveOffset + 16 > romData.Length) return null;

            //周波数?? frequency*1024
            uint samples_per_sec1024 = U.u32(romData, waveOffset + 4);
            samples_per_sec1024 = samples_per_sec1024 / 1024;

            //長さ length
            uint len = U.u32(romData, waveOffset + 12);

            // The PCM body runs [waveOffset+16, waveOffset+16+len).
            if ((long)waveOffset + 16 + len > romData.Length) return null;

            List<byte> fp = new List<byte>();
            AppendAscii(fp, "RIFF");                        //riff_chunk_ID
            U.append_u32(fp, (uint)(36 + len + 1));         //riff_chunk_size
            AppendAscii(fp, "WAVE");                        //riff_form_type
            AppendAscii(fp, "fmt ");                        //fmt_chunk_ID
            U.append_u32(fp, 16);                           //fmt_chunk_size
            U.append_u16(fp, 1);                            //fmt_wave_format_type
            U.append_u16(fp, 1);                            //fmt_channel
            U.append_u32(fp, (uint)samples_per_sec1024);    //fmt_samples_per_sec
            U.append_u32(fp, (uint)(samples_per_sec1024 * 8 / 8)); //fmt_bytes_per_sec
            U.append_u16(fp, (uint)(1));                    //fmt_block_size
            U.append_u16(fp, (uint)(8));                    //fmt_bits_per_sample

            AppendAscii(fp, "data");                        //data_chunk_ID
            U.append_u32(fp, (uint)(len + 1));              //data_chunk_size

            fp.Add((byte)0x80); //データ長+1あるらしい. 謎の余白. (lead pad byte)
            for (int i = 0; i < len; i++)
            {
                uint d = U.u8(romData, (uint)(i + waveOffset + 12 + 4));
                int dd = ((int)d) - 0x80;
                fp.Add((byte)dd);
            }
            return fp.ToArray();
        }

        /// <summary>
        /// VERBATIM port of WinForms <c>SongUtil.byteToWavForDPCM</c>: decode a
        /// DPCM-compressed GBA DirectSound sample at <paramref name="waveOffset"/>
        /// (delta-PCM block decode) into a RIFF/WAVE byte array.
        /// <para>Bounds-guarded: validates the header and every DPCM block read
        /// stays in range; returns <c>null</c> on guard failure. Never throws.</para>
        /// </summary>
        public static byte[] ByteToWavForDPCM(byte[] romData, uint waveOffset)
        {
            if (romData == null) return null;
            if ((long)waveOffset + 16 > romData.Length) return null;

            //周波数?? frequency*1024
            uint samples_per_sec1024 = U.u32(romData, waveOffset + 4);
            samples_per_sec1024 = samples_per_sec1024 / 1024;

            //長さ length
            uint len = U.u32(romData, waveOffset + 12);

            uint compressDataLen = GetDirectSoundWaveDataLength(romData, waveOffset);
            // The DPCM block stream runs [waveOffset+0x10, waveOffset+0x10+compressDataLen).
            if ((long)waveOffset + 0x10 + compressDataLen > romData.Length) return null;

            List<byte> fp = new List<byte>();
            AppendAscii(fp, "RIFF");                        //riff_chunk_ID
            U.append_u32(fp, (uint)(36 + len));             //riff_chunk_size
            AppendAscii(fp, "WAVE");                        //riff_form_type
            AppendAscii(fp, "fmt ");                        //fmt_chunk_ID
            U.append_u32(fp, 16);                           //fmt_chunk_size
            U.append_u16(fp, 1);                            //fmt_wave_format_type
            U.append_u16(fp, 1);                            //fmt_channel
            U.append_u32(fp, (uint)samples_per_sec1024);    //fmt_samples_per_sec
            U.append_u32(fp, (uint)(samples_per_sec1024 * 8 / 8)); //fmt_bytes_per_sec
            U.append_u16(fp, (uint)(1));                    //fmt_block_size
            U.append_u16(fp, (uint)(8));                    //fmt_bits_per_sample

            AppendAscii(fp, "data");                        //data_chunk_ID
            U.append_u32(fp, (uint)(len));                  //data_chunk_size

            for (uint n = 0; n < compressDataLen; n += DPCM_DECODE_SIZE)
            {
                uint readAddrN = waveOffset + 0x10 + n;
                byte[] block = U.getBinaryData(romData, readAddrN, DPCM_DECODE_SIZE);
                uint d = block[0];
                int dd = ((int)d) - 0x80;
                fp.Add((byte)dd);
                uint index;
                for (int i = 1; i < DPCM_DECODE_SIZE; i++)
                {
                    uint a = block[i];
                    if (i != 1)
                    {//どういうわけか、最初の1回目は特殊処理が必要 (first pass is special)
                        index = (uint)((a >> 4) & 0xF);
                        dd = dd + DpcmLookupTable[index];
                        fp.Add((byte)dd);
                    }

                    index = (uint)(a & 0xF);
                    dd = dd + DpcmLookupTable[index];
                    fp.Add((byte)dd);
                }
            }
            return fp.ToArray();
        }

        /// <summary>
        /// Port of WinForms <c>SongUtil.IsDirectSoundWaveCompressedDPCM(byte[],uint)</c>:
        /// the sample header byte at <paramref name="waveOffset"/> is <c>0x01</c>
        /// for DPCM-compressed, anything else for uncompressed PCM.
        /// <para>Bounds-guarded; returns <c>false</c> on guard failure. Never throws.</para>
        /// </summary>
        public static bool IsDirectSoundWaveCompressedDPCM(byte[] romData, uint waveOffset)
        {
            if (romData == null) return false;
            if ((long)waveOffset >= romData.Length) return false;
            uint head1 = U.u8(romData, waveOffset + 0);
            return (head1 == 0x01);
        }

        /// <summary>
        /// Port of WinForms <c>SongUtil.GetDirectSoundWaveDataLength(byte[],uint)</c>:
        /// the byte count of the on-ROM sample body (uncompressed == the +12 sample
        /// length; DPCM == 33 * ceil(len / 64)). Bounds-guarded; <c>0</c> on guard
        /// failure. Never throws. <c>public</c> so the Song Exchange transplant
        /// (<c>SongExchangeCore.InstrumentMap._prepare_DirectSound</c>, #1002 Slice 3)
        /// can size a DirectSound sample directly from raw SOURCE-ROM bytes —
        /// byte-identical to WF <c>SongUtil.GetDirectSoundWaveDataLength(byte[],uint)</c>.
        /// </summary>
        public static uint GetDirectSoundWaveDataLength(byte[] romData, uint waveOffset)
        {
            if (romData == null) return 0;
            if ((long)waveOffset + 16 > romData.Length) return 0;

            uint sample_length = U.u32(romData, waveOffset + 12);

            if (!IsDirectSoundWaveCompressedDPCM(romData, waveOffset))
            {//非圧縮wave (uncompressed)
                return sample_length;
            }

            //33 * (old data / 64);
            uint div64 = sample_length / 64;
            if (sample_length % 64 != 0)
            {//端数切り上げ (round up)
                div64++;
            }
            return 33 * (div64);
        }

        /// <summary>
        /// Public ROM-aware overload of <see cref="GetDirectSoundWaveDataLength(byte[],uint)"/>:
        /// the on-ROM sample body byte count for the sample at OFFSET
        /// <paramref name="waveOffset"/>. Used by the instrument-set export
        /// (<c>SongInstrumentSetCore</c>) to size the <c>.DirectSound.bin</c>
        /// side file. Bounds-guarded; <c>0</c> on guard failure. Never throws.
        /// </summary>
        public static uint GetDirectSoundWaveDataLength(ROM rom, uint waveOffset)
        {
            if (rom == null) return 0;
            return GetDirectSoundWaveDataLength(rom.Data, waveOffset);
        }

        /// <summary>
        /// Port of WinForms <c>SongUtil.IsDirectSoundData(byte[],uint)</c>: is the
        /// sample at OFFSET <paramref name="addr"/> a plausible (non-broken) GBA
        /// DirectSound sample? Rejects a too-short body (<c>len &lt;= 4</c>), an
        /// absurdly large one (<c>len &gt;= 4 MiB</c>), and any header/body that
        /// runs past the ROM end. Mirrors the WF guard the instrument-set export
        /// uses to SKIP a broken DirectSound row rather than emit a bogus side
        /// file. Bounds-guarded; <c>false</c> on guard failure. Never throws.
        /// </summary>
        public static bool IsDirectSoundData(byte[] romData, uint addr)
        {
            if (romData == null) return false;
            if ((long)addr + 12 + 4 > romData.Length) return false;

            uint len = GetDirectSoundWaveDataLength(romData, addr);
            if (len >= 1024 * 1024 * 4)
            {//4MB使う音源とかマジですか? (4 MiB sample? no.)
                return false;
            }
            if (len <= 4)
            {//短すぎる (too short)
                return false;
            }
            if ((long)addr + 12 + 4 + len > romData.Length)
            {
                return false;
            }
            //どうやら正しいデータのようだ. (looks like valid data)
            return true;
        }

        /// <summary>ROM-aware overload of <see cref="IsDirectSoundData(byte[],uint)"/>.</summary>
        public static bool IsDirectSoundData(ROM rom, uint addr)
        {
            if (rom == null) return false;
            return IsDirectSoundData(rom.Data, addr);
        }

        /// <summary>
        /// Decode the GBA DirectSound sample referenced by the GBA pointer
        /// <paramref name="wavePtr"/> into RIFF/WAVE bytes. Picks
        /// <see cref="ByteToWavForDPCM"/> vs <see cref="ByteToWav"/> by the
        /// compression flag. READ-ONLY.
        /// <para>Returns <c>null</c> when the ROM is null, the pointer is not a
        /// safe offset, or the inner decode guard fails. Never throws.</para>
        /// </summary>
        public static byte[] ExportWave(ROM rom, uint wavePtr)
        {
            if (rom == null) return null;
            uint off = U.toOffset(wavePtr);
            if (!U.isSafetyOffset(off, rom)) return null;

            try
            {
                if (IsDirectSoundWaveCompressedDPCM(rom.Data, off))
                    return ByteToWavForDPCM(rom.Data, off);
                return ByteToWav(rom.Data, off);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// De-UI'd VERBATIM port of WinForms <c>SongUtil.wavToByte</c>: convert a
        /// RIFF/WAVE byte array into the on-ROM GBA DirectSound sample bytes
        /// (header + signed-shifted 8-bit PCM). No <c>R.ShowStopError</c> /
        /// <c>R.ShowNoYes</c> / <c>DialogResult</c> — validation failures set
        /// <paramref name="error"/> and <c>return null</c>; the "too-big confirm"
        /// prompt is DROPPED entirely (the GUI owns any size warning).
        /// <para>Keeps the EXACT WF import convention (tightening #2):
        /// <c>(byte)((sbyte)data[44+1+n] + 0x80)</c>, <c>len = data_chunk_size - 1</c>,
        /// the loop-flag dword = 0, freq * 1024, and the trailing <c>0</c> append.</para>
        /// <para><paramref name="error"/> is <c>null</c> on success. Never throws
        /// (any exception is caught -&gt; <paramref name="error"/> + <c>null</c>),
        /// including on malformed / truncated input (no IndexOutOfRange).</para>
        /// </summary>
        public static byte[] WavToByte(byte[] wavBytes, out string error)
        {
            error = null;
            try
            {
                byte[] data = wavBytes;
                if (data == null || data.Length < 4
                    || data[0] != 'R'
                    || data[1] != 'I'
                    || data[2] != 'F'
                    || data[3] != 'F')
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
                if (data_chunk_size > data.Length - (44 + 1))
                {//チャンクのデータサイズが不正だったら修正する. (clamp a bogus chunk size)
                    data_chunk_size = (uint)(data.Length - (44 + 1));
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
                // NOTE: the WF ">= 100KB" confirmation prompt is intentionally
                // DROPPED here (tightening #1). The GUI owns any size warning.

                List<byte> wave = new List<byte>();
                U.append_u32(wave, 0); //ループするかどうかのフラグ?  ループしない 0x00000000
                //                             ループする   0x00000004
                U.append_u32(wave, fmt_samples_per_sec * 1024); //周波数*1024 (freq*1024)
                U.append_u32(wave, 0); //不明 (unknown)
                U.append_u32(wave, data_chunk_size - 1); //データ長 (data length)

                for (int n = 0; n < data_chunk_size - 1; n++)
                {
                    byte d = (byte)(((sbyte)data[44 + 1 + n]) + 0x80);
                    wave.Add(d);
                }
                wave.Add(0); //なぜか、 長さ-1して、空データを末尾に追加する.
                //少なくともsappyの挙動はそうなっている. bug? (trailing zero pad)
                return wave.ToArray();
            }
            catch (Exception ex)
            {
                error = R._("Failed to read the Wave file: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Import a RIFF/WAVE byte array as a NEW GBA DirectSound sample: the RAW
        /// sample bytes are appended at ROM end and the wave-pointer slot
        /// <paramref name="wavePointerSlotOffset"/> (the voice entry's +4 P4 field,
        /// passed as an OFFSET) is repointed to the appended data. ROM-MUTATING;
        /// records into the caller's ambient undo scope.
        /// <para>Returns <see cref="U.toPointer(uint)"/> of the append offset on
        /// success, or <see cref="U.NOT_FOUND"/> on any failure — with the ROM
        /// restored byte-identical (tightening #3: defensive snapshot + length-aware
        /// restore, the #885/#923 pattern). Never throws.</para>
        /// </summary>
        /// <param name="wavePointerSlotOffset">The OFFSET of the P4 wave-pointer
        /// slot (voice entry +4). <c>write_p32</c> converts it to a GBA pointer
        /// internally.</param>
        public static uint ImportWave(ROM rom, uint wavePointerSlotOffset, byte[] wavBytes, out string error)
        {
            error = null;
            if (rom == null)
            {
                error = R._("ROM is not loaded.");
                return U.NOT_FOUND;
            }

            byte[] sample = WavToByte(wavBytes, out error);
            if (sample == null)
            {
                // error already set by WavToByte
                return U.NOT_FOUND;
            }
            return AppendSampleAndRepoint(rom, wavePointerSlotOffset, sample, out error);
        }

        /// <summary>
        /// Import READY GBA DirectSound sample bytes (already a header + 8-bit PCM
        /// or a DPCM block stream — e.g. the output of the #1448 conversion dialog's
        /// sox/DPCM pipeline) as a NEW sample: the bytes are appended at ROM end and
        /// the +4 P4 wave-pointer slot is repointed. Unlike <see cref="ImportWave"/>
        /// this does NOT run <see cref="WavToByte"/> — the caller has already encoded
        /// the sample (so a DPCM sample survives intact). ROM-MUTATING; records into
        /// the caller's ambient undo scope.
        /// <para>Returns the GBA pointer of the append offset on success, or
        /// <see cref="U.NOT_FOUND"/> on failure with the ROM restored byte-identical.
        /// Never throws.</para>
        /// </summary>
        public static uint ImportSampleBytes(ROM rom, uint wavePointerSlotOffset, byte[] sampleBytes, out string error)
        {
            error = null;
            if (rom == null)
            {
                error = R._("ROM is not loaded.");
                return U.NOT_FOUND;
            }
            if (sampleBytes == null || sampleBytes.Length < 16)
            {
                error = R._("The wave sample data is empty or too small.");
                return U.NOT_FOUND;
            }
            return AppendSampleAndRepoint(rom, wavePointerSlotOffset, sampleBytes, out error);
        }

        /// <summary>
        /// Append <paramref name="sample"/> (ready GBA-sample bytes) at ROM end and
        /// repoint the +4 P4 slot. Shared by <see cref="ImportWave"/> and
        /// <see cref="ImportSampleBytes"/>: word-aligns the append, validates the
        /// slot, and on ANY fault restores the ROM byte-identical (the #885/#923
        /// snapshot pattern). Records into the caller's ambient undo scope.
        /// </summary>
        static uint AppendSampleAndRepoint(ROM rom, uint wavePointerSlotOffset, byte[] sample, out string error)
        {
            error = null;
            // The +4 pointer slot must be in-bounds for the write_p32.
            if ((long)wavePointerSlotOffset + 4 > rom.Data.Length)
            {
                error = R._("The wave pointer slot address is out of range.");
                return U.NOT_FOUND;
            }

            // Defensive snapshot for the byte-identical restore on fault. The
            // caller's ambient undo scope captures the writes for UNDO; this
            // snapshot guarantees a FAILED import mutates ZERO bytes.
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                // Word-align the append offset so the repointed P4 GBA pointer
                // lands on a 4-byte boundary even when the current ROM length is
                // not word-aligned (mirrors ImageImportCore.AppendToRomEnd /
                // U.Padding4). The single write_resize_data both pads the
                // 0..3-byte gap between the old length and appendOffset AND
                // reserves the sample — the padding bytes become part of the
                // resized buffer (and are undone by the snapshot/undo on fault).
                uint appendOffset = U.Padding4((uint)rom.Data.Length);
                if (!rom.write_resize_data(appendOffset + (uint)sample.Length))
                {
                    RestoreSnapshot(rom, snapshot);
                    error = R._("Failed to allocate ROM space for the wave sample.");
                    return U.NOT_FOUND;
                }
                rom.write_range(appendOffset, sample);
                // Repoint the P4 wave-pointer slot to the new sample (write_p32
                // takes an OFFSET and converts it to a GBA pointer internally).
                rom.write_p32(wavePointerSlotOffset, appendOffset);

                return U.toPointer(appendOffset);
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snapshot);
                error = R._("Wave import failed: {0}", ex.Message);
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore (the #885/#923 pattern): an append
        /// can GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snapshot)
        {
            if (rom.Data.Length != snapshot.Length)
                rom.write_resize_data((uint)snapshot.Length);
            Array.Copy(snapshot, rom.Data, snapshot.Length);
        }

        /// <summary>Append the ASCII bytes of a literal chunk id (Core has no
        /// <c>U.append_range(List&lt;byte&gt;, string)</c>).</summary>
        static void AppendAscii(List<byte> fp, string s)
        {
            foreach (var c in s) fp.Add((byte)c);
        }
    }
}
