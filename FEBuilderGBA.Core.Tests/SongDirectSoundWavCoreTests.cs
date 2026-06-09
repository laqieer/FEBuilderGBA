// SPDX-License-Identifier: GPL-3.0-or-later
// #1057 (N00 slice) Core tests for SongDirectSoundWavCore — the GBA DirectSound
// sample <-> WAV seam (verbatim WF SongUtil port + RAW append + P4 repoint with
// a byte-identical fault restore).
//
// Uses a synthetic ROM (no RomInfo needed — the seam only touches the sample
// header/body and the P4 pointer slot). Validates:
//   * legacy-shape round-trip: sample -> ByteToWav -> WavToByte -> byte-identical
//   * ByteToWavForDPCM decodes a synthetic DPCM sample to the expected PCM
//   * WavToByte rejects non-RIFF / truncated / >8-bit (null + error, no throw)
//   * ImportWave success: P4 repointed to the appended sample, decodes back,
//     old sample bytes unchanged, returns U.toPointer(appendOffset)
//   * ImportWave fault (bad/empty wav): NOT_FOUND, error, byte-identical ROM
//   * outer rollback: BeginUndoScope + Rollback restores length, P4 + bytes
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SongDirectSoundWavCoreTests
    {
        // A DirectSound sample placed near the ROM start; the P4 pointer slot
        // lives in a separate voice entry, also near the start. ROM end is free.
        const uint SAMPLE_OFFSET = 0x400;
        const uint VOICE_ENTRY = 0x800;     // P4 slot = VOICE_ENTRY + 4
        const uint ROM_LEN = 0x20000;       // 128 KiB

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            // Zero the header/entry region so reads are deterministic.
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        // Build an uncompressed 8-bit GBA DirectSound sample (header + raw PCM)
        // directly into a byte[] at the given offset. Returns the raw PCM bytes
        // (the values stored at +16.. before the signed -0x80 export shift).
        static byte[] WriteSample(byte[] data, uint offset, uint freq, byte[] pcm)
        {
            U.write_u8(data, offset + 0, 0x00);              // compression flag = uncompressed
            U.write_u32(data, offset + 4, freq * 1024);      // freq * 1024
            U.write_u32(data, offset + 8, 0);                // loop point (unused here)
            U.write_u32(data, offset + 12, (uint)pcm.Length);// sample length
            for (int i = 0; i < pcm.Length; i++)
                U.write_u8(data, (uint)(offset + 16 + i), pcm[i]);
            return pcm;
        }

        static byte[] MakePcm(int n)
        {
            byte[] pcm = new byte[n];
            for (int i = 0; i < n; i++) pcm[i] = (byte)((i * 7 + 3) & 0xFF);
            return pcm;
        }

        // -----------------------------------------------------------------
        // 1. Round-trip (legacy shape): sample -> ByteToWav -> WavToByte.
        //
        //    DOCUMENTED RESIDUAL (verbatim-WF behaviour — tightening #5): a true
        //    full-byte identity is NOT achievable through the verbatim WF codec.
        //    `byteToWav` always emits data_chunk_size = len + 1 plus a single
        //    0x80 lead-pad byte, so the WAV body is exactly one byte SHORTER than
        //    its declared chunk size. `wavToByte` then CLAMPS
        //    (data_chunk_size > data.Length - 45 => data_chunk_size = len) and
        //    re-derives len = data_chunk_size - 1, dropping the final PCM byte.
        //    Every pass therefore shrinks the sample by exactly one byte; there
        //    is no nonempty fixed point. We do NOT "fix" this (verbatim-port hard
        //    rule) — instead the test asserts the EXACT stable relationship:
        //      * loop flag (0) preserved byte-identically
        //      * freq * 1024 preserved byte-identically
        //      * the rebuilt body == the original PCM with its LAST byte clamped
        //        off, followed by the single WF trailing zero.
        //    Tight/nonzero-pad/looped samples normalize to this same legacy
        //    shape on re-import. DPCM does NOT round-trip byte-identically
        //    through WavToByte (separate codec — see test #2).
        // -----------------------------------------------------------------
        [Fact]
        public void RoundTrip_LegacyShape_StablePcmExceptDocumentedClamp()
        {
            const uint FREQ = 12000;
            byte[] pcm = MakePcm(64);

            // Build an uncompressed on-ROM sample: header(16) + raw PCM at +16.
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            WriteSample(data, SAMPLE_OFFSET, FREQ, pcm);

            // sample -> WAV -> rebuilt sample bytes
            byte[] wav = SongDirectSoundWavCore.ByteToWav(data, SAMPLE_OFFSET);
            Assert.NotNull(wav);
            byte[] rebuilt = SongDirectSoundWavCore.WavToByte(wav, out string err);
            Assert.Null(err);
            Assert.NotNull(rebuilt);

            // Header: loop flag 0 + freq*1024 preserved exactly.
            Assert.Equal(0u, U.u32(rebuilt, 0));               // loop flag
            Assert.Equal(FREQ * 1024, U.u32(rebuilt, 4));      // freq * 1024
            Assert.Equal(0u, U.u32(rebuilt, 8));               // unknown dword

            // The documented one-byte clamp: rebuilt header len == pcm.Length - 1.
            uint rebuiltLen = U.u32(rebuilt, 12);
            Assert.Equal((uint)(pcm.Length - 1), rebuiltLen);

            // Body: each rebuilt body byte == the corresponding original PCM byte
            // (ByteToWav shifts -0x80, WavToByte shifts +0x80 — they invert), for
            // all but the clamped final byte. A single trailing zero closes it.
            // rebuilt = header(16) + rebuiltLen body bytes + trailing 0.
            for (int i = 0; i < rebuiltLen; i++)
                Assert.Equal(pcm[i], rebuilt[16 + i]);
            Assert.Equal(0, rebuilt[16 + rebuiltLen]);         // WF trailing zero
            Assert.Equal((int)(16 + rebuiltLen + 1), rebuilt.Length);
        }

        // -----------------------------------------------------------------
        // 2. ByteToWavForDPCM decodes a synthetic DPCM sample to the expected
        //    PCM. (Do NOT assert DPCM round-trips through WavToByte.)
        // -----------------------------------------------------------------
        [Fact]
        public void ByteToWavForDPCM_DecodesExpectedPcm()
        {
            // DPCM lookup table (must match the Core port).
            int[] lut = { 0, 1, 4, 9, 16, 25, 36, 49, -64, -49, -36, -25, -16, -9, -4, -1 };
            const uint FREQ = 12000;

            // One DPCM block = 0x21 bytes: byte[0] is the seed (unsigned base),
            // bytes[1..0x20] each carry two 4-bit deltas (high nibble then low),
            // EXCEPT byte[1] whose high nibble is skipped (WF "first pass special").
            // Sample length 64 -> compressDataLen = 33*ceil(64/64) = 33 = one block.
            byte seed = 0x90; // dd starts at 0x90 - 0x80 = 16
            byte[] block = new byte[0x21];
            block[0] = seed;
            // Fill delta nibbles with a deterministic pattern.
            var deltaBytes = new List<byte>();
            for (int i = 1; i < 0x21; i++)
            {
                byte b = (byte)((i * 5 + 1) & 0xFF);
                block[i] = b;
                deltaBytes.Add(b);
            }

            // Compute the expected decoded PCM the same way the decoder does.
            var expected = new List<byte>();
            int dd = ((int)seed) - 0x80;
            expected.Add((byte)dd);
            for (int i = 1; i < 0x21; i++)
            {
                uint a = block[i];
                if (i != 1)
                {
                    int idxHi = (int)((a >> 4) & 0xF);
                    dd = dd + lut[idxHi];
                    expected.Add((byte)dd);
                }
                int idxLo = (int)(a & 0xF);
                dd = dd + lut[idxLo];
                expected.Add((byte)dd);
            }

            // Build the DPCM sample header (+0 flag=1, +4 freq*1024, +12 len=64)
            // and the single block at +0x10.
            byte[] data = new byte[ROM_LEN];
            U.write_u8(data, SAMPLE_OFFSET + 0, 0x01); // DPCM flag
            U.write_u32(data, SAMPLE_OFFSET + 4, FREQ * 1024);
            U.write_u32(data, SAMPLE_OFFSET + 12, 64);
            for (int i = 0; i < block.Length; i++)
                U.write_u8(data, (uint)(SAMPLE_OFFSET + 0x10 + i), block[i]);

            byte[] wav = SongDirectSoundWavCore.ByteToWavForDPCM(data, SAMPLE_OFFSET);
            Assert.NotNull(wav);

            // The WAV body (data after the 44-byte header) is the export-shifted
            // PCM: each stored byte is (decoded - 0x80). Recover the decoded PCM.
            byte[] body = U.getBinaryData(wav, 44, wav.Length - 44);
            Assert.Equal(expected.Count, body.Length);
            for (int i = 0; i < expected.Count; i++)
            {
                // ByteToWavForDPCM appends (byte)dd directly (NOT shifted), so
                // the body equals the raw decoded dd bytes.
                Assert.Equal(expected[i], body[i]);
            }
        }

        // -----------------------------------------------------------------
        // 3. WavToByte rejects non-RIFF, truncated, and >8-bit — null + error,
        //    no throw (no IndexOutOfRange on malformed/truncated input).
        // -----------------------------------------------------------------
        [Fact]
        public void WavToByte_NonRiff_ReturnsNullError_NoThrow()
        {
            byte[] notWav = new byte[64];
            for (int i = 0; i < notWav.Length; i++) notWav[i] = (byte)i;
            notWav[0] = (byte)'X'; // break the RIFF magic
            byte[] result = SongDirectSoundWavCore.WavToByte(notWav, out string err);
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void WavToByte_Truncated_ReturnsNullError_NoThrow()
        {
            // A valid RIFF magic but fewer than 45 bytes — the size guard must
            // fire BEFORE any header u32 read overruns.
            byte[] tiny = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0 };
            byte[] result = SongDirectSoundWavCore.WavToByte(tiny, out string err);
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void WavToByte_EmptyAndNull_NoThrow()
        {
            byte[] r1 = SongDirectSoundWavCore.WavToByte(Array.Empty<byte>(), out string e1);
            Assert.Null(r1);
            Assert.False(string.IsNullOrEmpty(e1));

            byte[] r2 = SongDirectSoundWavCore.WavToByte(null, out string e2);
            Assert.Null(r2);
            Assert.False(string.IsNullOrEmpty(e2));
        }

        [Fact]
        public void WavToByte_HighBitDepth_ReturnsNullError()
        {
            // Build a 16-bit WAV header so fmt_bits_per_sample (+34) == 16.
            byte[] wav = MakeMinimalWav(bitsPerSample: 16, dataBytes: 32);
            byte[] result = SongDirectSoundWavCore.WavToByte(wav, out string err);
            Assert.Null(result);
            Assert.False(string.IsNullOrEmpty(err));
        }

        // -----------------------------------------------------------------
        // 4. ImportWave success: P4 repointed to a NEW offset (the appended
        //    sample), the appended bytes decode back via ByteToWav, old sample
        //    bytes unchanged, returns U.toPointer(appendOffset).
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWave_Success_RepointsAndPreservesOldSample()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Seed an existing sample so we can prove it is NOT touched.
                byte[] oldPcm = MakePcm(48);
                WriteSample(rom.Data, SAMPLE_OFFSET, 8000, oldPcm);
                byte[] oldSampleRegion = rom.getBinaryData(SAMPLE_OFFSET, 16 + oldPcm.Length + 1);

                // Point the P4 slot at the old sample to start with.
                rom.write_p32(VOICE_ENTRY + 4, SAMPLE_OFFSET);

                uint lenBefore = (uint)rom.Data.Length;

                // Build a valid 8-bit mono WAV to import.
                byte[] wav = MakeMinimalWav(bitsPerSample: 8, dataBytes: 100);

                uint newPtr = SongDirectSoundWavCore.ImportWave(rom, VOICE_ENTRY + 4, wav, out string err);

                Assert.Null(err);
                Assert.NotEqual(U.NOT_FOUND, newPtr);
                // Return value is U.toPointer(appendOffset) == toPointer(lenBefore).
                Assert.Equal(U.toPointer(lenBefore), newPtr);
                // P4 slot now stores that GBA pointer.
                Assert.Equal(newPtr, rom.u32(VOICE_ENTRY + 4));

                // The appended sample decodes back via ByteToWav (the new sample
                // is uncompressed — flag byte 0 from WavToByte's header).
                uint newOff = U.toOffset(newPtr);
                byte[] decoded = SongDirectSoundWavCore.ByteToWav(rom.Data, newOff);
                Assert.NotNull(decoded);

                // Old sample bytes unchanged.
                byte[] oldSampleAfter = rom.getBinaryData(SAMPLE_OFFSET, 16 + oldPcm.Length + 1);
                Assert.Equal(oldSampleRegion, oldSampleAfter);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 5. ImportWave fault (bad/empty wav): NOT_FOUND, error set, ROM
        //    byte-identical (no mutation).
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWave_BadWav_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                byte[] bad = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 0, 1, 2, 3 };
                uint result = SongDirectSoundWavCore.ImportWave(rom, VOICE_ENTRY + 4, bad, out string err);

                Assert.Equal(U.NOT_FOUND, result);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical, ZERO mutation
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 6. Outer rollback: wrap an ImportWave in BeginUndoScope(ud) +
        //    Rollback(ud); assert rom.Data.Length AND the P4 slot AND bytes are
        //    restored byte-identical (proves the append/resize is undone).
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWave_OuterRollback_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Seed a P4 slot value so we can prove it is restored.
                rom.write_p32(VOICE_ENTRY + 4, SAMPLE_OFFSET);

                var undo = new Undo();
                CoreState.Undo = undo;

                byte[] before = (byte[])rom.Data.Clone();
                uint lenBefore = (uint)rom.Data.Length;
                uint p4Before = rom.u32(VOICE_ENTRY + 4);

                byte[] wav = MakeMinimalWav(bitsPerSample: 8, dataBytes: 200);

                var ud = new Undo.UndoData
                {
                    time = DateTime.Now,
                    name = "import wave",
                    list = new List<Undo.UndoPostion>(),
                    filesize = lenBefore,
                };

                uint newPtr;
                using (ROM.BeginUndoScope(ud))
                {
                    newPtr = SongDirectSoundWavCore.ImportWave(rom, VOICE_ENTRY + 4, wav, out string err);
                    Assert.Null(err);
                    Assert.NotEqual(U.NOT_FOUND, newPtr);
                }

                // The import grew the ROM + repointed P4.
                Assert.True(rom.Data.Length > lenBefore);
                Assert.NotEqual(p4Before, rom.u32(VOICE_ENTRY + 4));

                // Roll the whole scope back.
                undo.Rollback(ud);

                // Length restored, P4 restored, bytes byte-identical.
                Assert.Equal((int)lenBefore, rom.Data.Length);
                Assert.Equal(p4Before, rom.u32(VOICE_ENTRY + 4));
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        // ----- helpers --------------------------------------------------------

        // Build a minimal canonical 44-byte WAV header + N data bytes. The data
        // bytes are an ascending ramp. fmt_bits_per_sample is at +34 (u16).
        static byte[] MakeMinimalWav(int bitsPerSample, int dataBytes)
        {
            var fp = new List<byte>();
            void Ascii(string s) { foreach (var c in s) fp.Add((byte)c); }

            uint freq = 12000;
            uint byteRate = freq; // 8-bit mono
            Ascii("RIFF");
            U.append_u32(fp, (uint)(36 + dataBytes));
            Ascii("WAVE");
            Ascii("fmt ");
            U.append_u32(fp, 16);                  // fmt chunk size
            U.append_u16(fp, 1);                   // PCM
            U.append_u16(fp, 1);                   // channels
            U.append_u32(fp, freq);                // samples per sec (+24)
            U.append_u32(fp, byteRate);            // bytes per sec (+28)
            U.append_u16(fp, 1);                   // block align (+32)
            U.append_u16(fp, (uint)bitsPerSample); // bits per sample (+34)
            Ascii("data");
            U.append_u32(fp, (uint)dataBytes);     // data chunk size (+40)
            for (int i = 0; i < dataBytes; i++)
                fp.Add((byte)(i & 0xFF));
            return fp.ToArray();
        }
    }
}
