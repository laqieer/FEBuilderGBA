// SPDX-License-Identifier: GPL-3.0-or-later
// #1057 (PR1) Core tests for SongInstrumentSetCore — the recursive READ-ONLY
// instrument-set (voicegroup) EXPORT seam (port of WF SongInstrumentForm.
// ExportAllLow / ExportOneLow).
//
// Uses synthetic in-memory ROMs (a ROM over a hand-laid byte[]). The export only
// reads voice-entry bytes + the pointed-at sample/keymap/sub-voicegroup blobs and
// emits a TSV index + side files through in-memory collector delegates, so no real
// ROM file is needed. Covers the recursive SHAPES (Copilot review pt 3):
//   * a direct 0x08 DirectSound voice -> one .DirectSound.bin of the right length
//     (uncompressed AND DPCM-compressed length rule), correct 4 header columns
//   * a 0x03 Wave Memory voice -> .Wave.bin of EXACTLY 16 bytes
//   * a 0x80 drum voice nesting a child voicegroup -> .Drum.instrument index + the
//     child's side files; a nested @SELF+0 self-ref AND a separate nonzero @SELF+0C
//   * a 0x40 multisample voice -> .Multi.instrument + the 128-byte .Multi.keys.bin
//   * deterministic filenames + TSV column counts
//   * @BROKENDATA (nested out-of-range self-ref) + unsafe / broken-DirectSound
//     pointer SKIP behavior
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SongInstrumentSetCoreTests
    {
        const uint ROM_LEN = 0x40000;       // 256 KiB
        const uint VOCA_BASE = 0x1000;      // top-level voicegroup base (offset)
        const int BLOCK = 12;

        // In-memory side-file + index collector for the export delegates.
        sealed class Sink
        {
            public readonly Dictionary<string, byte[]> Files = new();
            public readonly Dictionary<string, List<string>> Indexes = new();

            public Action<string, byte[]> WriteFile => (name, bytes) => Files[name] = bytes;
            public Action<string, IEnumerable<string>> WriteLines =>
                (name, lines) => Indexes[name] = new List<string>(lines);
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            // 0x00-fill so an unwritten voice slot reads type 0x00 with a null (=0)
            // P4 — which fails isSafetyPointer and stops the voice scan.
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        // Write a 12-byte voice entry. p4/p8 are GBA pointers (or 0).
        static void WriteVoice(ROM rom, uint addr, byte type,
            byte b1, byte b2, byte b3, uint p4, uint p8,
            byte b8, byte b9, byte b10, byte b11)
        {
            rom.write_u8(addr + 0, type);
            rom.write_u8(addr + 1, b1);
            rom.write_u8(addr + 2, b2);
            rom.write_u8(addr + 3, b3);
            rom.write_u32(addr + 4, p4);
            rom.write_u32(addr + 8, p8);
            // For non-0x40 types the +8..+11 are raw bytes (overwrites the p8 dword).
            if (type != 0x40)
            {
                rom.write_u8(addr + 8, b8);
                rom.write_u8(addr + 9, b9);
                rom.write_u8(addr + 10, b10);
                rom.write_u8(addr + 11, b11);
            }
        }

        // Write an uncompressed DirectSound sample header + PCM at offset; returns
        // the sample body byte count (= pcmLen).
        static void WriteDirectSoundSample(ROM rom, uint off, uint freq, int pcmLen)
        {
            rom.write_u8(off + 0, 0x00);            // uncompressed
            rom.write_u32(off + 4, freq * 1024);
            rom.write_u32(off + 8, 0);
            rom.write_u32(off + 12, (uint)pcmLen);
            for (int i = 0; i < pcmLen; i++)
                rom.write_u8((uint)(off + 16 + i), (byte)((i * 3 + 1) & 0xFF));
        }

        // Write a DPCM-compressed DirectSound sample. uncompressedLen is the +12
        // length; the on-ROM body is 33*ceil(len/64) bytes.
        static uint WriteDpcmSample(ROM rom, uint off, uint freq, uint uncompressedLen)
        {
            rom.write_u8(off + 0, 0x01);            // DPCM
            rom.write_u32(off + 4, freq * 1024);
            rom.write_u32(off + 12, uncompressedLen);
            uint div64 = uncompressedLen / 64;
            if (uncompressedLen % 64 != 0) div64++;
            uint bodyLen = 33 * div64;
            for (uint i = 0; i < bodyLen; i++)
                rom.write_u8(off + 0x10 + i, (byte)((i + 7) & 0xFF));
            return bodyLen;
        }

        // Split a TSV row into its tab-separated columns.
        static string[] Cols(string row) =>
            row.Split('\t', StringSplitOptions.None);

        // -----------------------------------------------------------------
        // 1. Direct 0x08 DirectSound voice (uncompressed) -> .DirectSound.bin of
        //    length 12+4+pcmLen, with the correct 4 header columns + filename token.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_DirectSound08_Uncompressed_WritesBinOfRightLength()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint sampleOff = 0x8000;
                const int PCM = 100;
                WriteDirectSoundSample(rom, sampleOff, 12000, PCM);
                WriteVoice(rom, VOCA_BASE, 0x08, 0x11, 0x22, 0x33,
                    U.toPointer(sampleOff), 0, 0x88, 0x99, 0xAA, 0xBB);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "voicegroup",
                    sink.WriteFile, sink.WriteLines);

                // Exactly one .DirectSound.bin, named with the 0xINDEX prefix.
                Assert.True(sink.Files.ContainsKey("voicegroup0x00.DirectSound.bin"));
                byte[] bin = sink.Files["voicegroup0x00.DirectSound.bin"];
                Assert.Equal(12 + 4 + PCM, bin.Length);

                // TSV row: 4 header cols (08 11 22 33) + filename + 4 trailing bytes.
                Assert.True(sink.Indexes.ContainsKey("voicegroup.instrument"));
                var rows = sink.Indexes["voicegroup.instrument"];
                Assert.Single(rows);
                var c = Cols(rows[0]);
                Assert.Equal("08", c[0]);
                Assert.Equal("11", c[1]);
                Assert.Equal("22", c[2]);
                Assert.Equal("33", c[3]);
                Assert.Equal("voicegroup0x00.DirectSound.bin", c[4]);
                // Trailing +8..+11 columns for non-0x40.
                Assert.Equal("88", c[5]);
                Assert.Equal("99", c[6]);
                Assert.Equal("AA", c[7]);
                Assert.Equal("BB", c[8]);
                Assert.Equal(9, c.Length);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 1b. DPCM-compressed DirectSound 0x00 -> .DirectSound.bin sized by the
        //     compressed-length rule (33*ceil(len/64)), NOT the raw +12 length.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_DirectSound00_Dpcm_UsesCompressedLength()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint sampleOff = 0x8000;
                const uint UNCOMPRESSED = 200; // -> div64 = ceil(200/64) = 4 -> body 132
                uint bodyLen = WriteDpcmSample(rom, sampleOff, 12000, UNCOMPRESSED);
                Assert.Equal(33u * 4u, bodyLen); // sanity: 132

                WriteVoice(rom, VOCA_BASE, 0x00, 0, 0, 0,
                    U.toPointer(sampleOff), 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                byte[] bin = sink.Files["vg0x00.DirectSound.bin"];
                Assert.Equal((int)(12 + 4 + bodyLen), bin.Length);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 2. Wave Memory 0x03 -> .Wave.bin of EXACTLY 16 bytes.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_WaveMemory03_WritesExactly16Bytes()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint waveOff = 0x9000;
                for (int i = 0; i < 32; i++) rom.write_u8((uint)(waveOff + i), (byte)(0xF0 + i));
                WriteVoice(rom, VOCA_BASE, 0x03, 1, 2, 3,
                    U.toPointer(waveOff), 0, 4, 5, 6, 7);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                Assert.True(sink.Files.ContainsKey("vg0x00.Wave.bin"));
                Assert.Equal(16, sink.Files["vg0x00.Wave.bin"].Length);

                var rows = sink.Indexes["vg.instrument"];
                var c = Cols(rows[0]);
                Assert.Equal("03", c[0]);
                Assert.Equal("vg0x00.Wave.bin", c[4]);
                Assert.Equal(9, c.Length); // 4 header + filename + 4 trailing
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 3. Drum 0x80 nesting a child voicegroup -> .Drum.instrument index +
        //    the child's side files (a child 0x03 Wave Memory voice).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_Drum80_Recurses_WritesChildIndexAndSideFiles()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Child voicegroup at 0x2000 with one 0x03 Wave Memory voice.
                uint childBase = 0x2000;
                uint childWaveOff = 0xA000;
                for (int i = 0; i < 16; i++) rom.write_u8((uint)(childWaveOff + i), (byte)i);
                WriteVoice(rom, childBase, 0x03, 0, 0, 0,
                    U.toPointer(childWaveOff), 0, 0, 0, 0, 0);
                // child voice 1 terminator: type 0x00 with null P4 -> scan stops.

                // Top-level drum voice points at the child voicegroup.
                WriteVoice(rom, VOCA_BASE, 0x80, 0xAA, 0xBB, 0xCC,
                    U.toPointer(childBase), 0, 0xD0, 0xD1, 0xD2, 0xD3);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // Top-level row references the nested index by filename.
                var topRows = sink.Indexes["vg.instrument"];
                Assert.Single(topRows);
                var c = Cols(topRows[0]);
                Assert.Equal("80", c[0]);
                Assert.Equal("vg0x00.Drum.instrument", c[4]);
                // Trailing +8..+11 still present for 0x80.
                Assert.Equal("D0", c[5]);
                Assert.Equal(9, c.Length);

                // The nested .Drum.instrument index exists + holds the child's row.
                Assert.True(sink.Indexes.ContainsKey("vg0x00.Drum.instrument"));
                var childRows = sink.Indexes["vg0x00.Drum.instrument"];
                Assert.Single(childRows);
                Assert.Equal("03", Cols(childRows[0])[0]);

                // The child's Wave Memory side file was written with the NESTED
                // basename prefix (vg0x00.Drum), NOT the top-level vg, so two
                // top-level groups whose child voice index is 0 never collide
                // (Copilot review pt 1).
                Assert.True(sink.Files.ContainsKey("vg0x00.Drum0x00.Wave.bin"));
                Assert.Equal(16, sink.Files["vg0x00.Drum0x00.Wave.bin"].Length);
                Assert.False(sink.Files.ContainsKey("vg0x00.Wave.bin"));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 3b. Nested @SELF+0 — a drum voice INSIDE a child voicegroup that points
        //     back at that child's own base emits @SELF+0 (no infinite recursion).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_NestedDrum_SelfRefZero_EmitsSelfPlus0()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Child voicegroup at 0x2000:
                //   voice 0: 0x80 drum -> points at 0x2000 (itself) => @SELF+0
                uint childBase = 0x2000;
                WriteVoice(rom, childBase, 0x80, 0, 0, 0,
                    U.toPointer(childBase), 0, 0, 0, 0, 0);

                // Top-level 0x80 drum -> child voicegroup (top level recurses in).
                WriteVoice(rom, VOCA_BASE, 0x80, 0, 0, 0,
                    U.toPointer(childBase), 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                var childRows = sink.Indexes["vg0x00.Drum.instrument"];
                Assert.Single(childRows);
                var c = Cols(childRows[0]);
                Assert.Equal("80", c[0]);
                Assert.Equal("@SELF+0", c[4]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 3c. Nested NONZERO @SELF+0C — a drum voice inside a child voicegroup that
        //     points at child base + 0x0C (the second 12-byte record) emits
        //     @SELF+0C (mandatory nonzero-offset coverage).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_NestedDrum_SelfRefNonzero_EmitsSelfPlus0C()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Child voicegroup at 0x2000 with TWO records so base+0x0C is in
                // range ((DataCount + 1) * 12 window).
                uint childBase = 0x2000;
                // record 0: drum -> child base + 0x0C  => @SELF+0C
                WriteVoice(rom, childBase, 0x80, 0, 0, 0,
                    U.toPointer(childBase + 0x0C), 0, 0, 0, 0, 0);
                // record 1: a valid data-less square wave (keeps DataCount == 2).
                WriteVoice(rom, childBase + 0x0C, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                // Top-level 0x80 drum -> child voicegroup.
                WriteVoice(rom, VOCA_BASE, 0x80, 0, 0, 0,
                    U.toPointer(childBase), 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                var childRows = sink.Indexes["vg0x00.Drum.instrument"];
                Assert.Equal(2, childRows.Count);
                var c0 = Cols(childRows[0]);
                Assert.Equal("80", c0[0]);
                Assert.Equal("@SELF+0C", c0[4]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 3d. Nested out-of-range self-ref -> @BROKENDATA (a drum voice inside a
        //     child voicegroup pointing at an in-ROM-but-out-of-voicegroup-range
        //     address while nested).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_NestedDrum_OutOfRange_EmitsBrokenData()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint childBase = 0x2000;
                // record 0: drum -> 0x3000 (a safe offset OUTSIDE the child range).
                WriteVoice(rom, childBase, 0x80, 0, 0, 0,
                    U.toPointer(0x3000), 0, 0, 0, 0, 0);

                WriteVoice(rom, VOCA_BASE, 0x80, 0, 0, 0,
                    U.toPointer(childBase), 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                var childRows = sink.Indexes["vg0x00.Drum.instrument"];
                Assert.Single(childRows);
                Assert.Equal("@BROKENDATA", Cols(childRows[0])[4]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 4. Multisample 0x40 -> .Multi.instrument index + .Multi.keys.bin of
        //    EXACTLY 128 bytes. Row OMITS the trailing +8..+11 columns (0x40).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_MultiSample40_WritesMultiIndexAnd128ByteKeymap()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Sub-voicegroup at 0x2000 with one 0x03 Wave Memory voice.
                uint subBase = 0x2000;
                uint subWaveOff = 0xB000;
                for (int i = 0; i < 16; i++) rom.write_u8((uint)(subWaveOff + i), (byte)(i + 1));
                WriteVoice(rom, subBase, 0x03, 0, 0, 0,
                    U.toPointer(subWaveOff), 0, 0, 0, 0, 0);

                // Keymap at 0x3000 (128 bytes).
                uint keymapOff = 0x3000;
                for (int i = 0; i < 128; i++) rom.write_u8((uint)(keymapOff + i), (byte)(i & 0xFF));

                // Top-level 0x40 voice: P4=sub-voicegroup, P8=keymap.
                WriteVoice(rom, VOCA_BASE, 0x40, 0x10, 0x20, 0x30,
                    U.toPointer(subBase), U.toPointer(keymapOff), 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // .Multi.keys.bin is EXACTLY 128 bytes.
                Assert.True(sink.Files.ContainsKey("vg0x00.Multi.keys.bin"));
                Assert.Equal(128, sink.Files["vg0x00.Multi.keys.bin"].Length);

                // .Multi.instrument index + the sub-voicegroup's Wave Memory file
                // (named with the NESTED basename vg0x00.Multi — Copilot pt 1). The
                // 128-byte keymap belongs to the TOP-level 0x40 voice so it keeps
                // the top-level vg prefix (vg0x00.Multi.keys.bin, asserted above).
                Assert.True(sink.Indexes.ContainsKey("vg0x00.Multi.instrument"));
                Assert.True(sink.Files.ContainsKey("vg0x00.Multi0x00.Wave.bin"));

                // Top-level row: 4 header + Multi.instrument token + keys token,
                // and NO trailing +8..+11 columns (0x40 omits them).
                var topRows = sink.Indexes["vg.instrument"];
                var c = Cols(topRows[0]);
                Assert.Equal("40", c[0]);
                Assert.Equal("10", c[1]);
                Assert.Equal("20", c[2]);
                Assert.Equal("30", c[3]);
                Assert.Equal("vg0x00.Multi.instrument", c[4]);
                Assert.Equal("vg0x00.Multi.keys.bin", c[5]);
                // 0x40 OMITS the +8..+11 trailing data columns (WF), but both the
                // Multi.instrument and Multi.keys.bin tokens are written with a
                // TRAILING tab, so the row ends in a trailing empty column on split:
                //   40 | 10 | 20 | 30 | Multi.instrument | Multi.keys.bin | (empty)
                Assert.Equal(7, c.Length);
                Assert.Equal("", c[6]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 5. Broken DirectSound pointer (valid pointer, but the sample header is
        //    broken: len <= 4) -> the row is SKIPPED, NO bogus .DirectSound.bin.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_BrokenDirectSound_SkipsRow_NoBin()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // A "sample" whose +12 length is 2 (<= 4) -> IsDirectSoundData false.
                uint sampleOff = 0x8000;
                rom.write_u8(sampleOff + 0, 0x00);
                rom.write_u32(sampleOff + 4, 12000 * 1024);
                rom.write_u32(sampleOff + 12, 2); // too short
                // voice 0: broken DirectSound; voice 1: a valid square wave so the
                // scan doesn't stop at voice 0 (DataCount stays >= 1).
                WriteVoice(rom, VOCA_BASE, 0x08, 0, 0, 0,
                    U.toPointer(sampleOff), 0, 0, 0, 0, 0);
                WriteVoice(rom, VOCA_BASE + 0x0C, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // No .DirectSound.bin written for the broken voice.
                Assert.False(sink.Files.ContainsKey("vg0x00.DirectSound.bin"));

                // The broken row is dropped from the index; the valid square wave
                // (index 1) survives.
                var rows = sink.Indexes["vg.instrument"];
                Assert.Single(rows);
                Assert.Equal("01", Cols(rows[0])[0]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 6. Unsafe pointer (P4 = 0 / not a safety pointer): the voice is invalid
        //    so the scan STOPS — a leading unsafe DirectSound voice yields an empty
        //    index, no side files.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_UnsafePointer_StopsScan_EmptyIndex()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // voice 0: DirectSound with P4 = 0 (unsafe) -> IsValidVoice false ->
                // CountVoices returns 0.
                WriteVoice(rom, VOCA_BASE, 0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                Assert.True(sink.Indexes.ContainsKey("vg.instrument"));
                Assert.Empty(sink.Indexes["vg.instrument"]);
                Assert.Empty(sink.Files);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 7. Multiple voices in one voicegroup -> deterministic 0xINDEX filenames
        //    and one row per voice.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_MultipleVoices_DeterministicFilenames()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // voice 0: DirectSound, voice 1: Wave Memory, voice 2: square wave.
                uint s0 = 0x8000, w1 = 0x9000;
                WriteDirectSoundSample(rom, s0, 8000, 40);
                for (int i = 0; i < 16; i++) rom.write_u8((uint)(w1 + i), (byte)i);

                WriteVoice(rom, VOCA_BASE + 0x00, 0x00, 0, 0, 0, U.toPointer(s0), 0, 0, 0, 0, 0);
                WriteVoice(rom, VOCA_BASE + 0x0C, 0x03, 0, 0, 0, U.toPointer(w1), 0, 0, 0, 0, 0);
                WriteVoice(rom, VOCA_BASE + 0x18, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                var rows = sink.Indexes["vg.instrument"];
                Assert.Equal(3, rows.Count);
                Assert.Equal("00", Cols(rows[0])[0]);
                Assert.Equal("03", Cols(rows[1])[0]);
                Assert.Equal("01", Cols(rows[2])[0]);

                // Deterministic, index-prefixed filenames.
                Assert.True(sink.Files.ContainsKey("vg0x00.DirectSound.bin"));
                Assert.True(sink.Files.ContainsKey("vg0x01.Wave.bin"));
                // Square wave (voice 2) has no side file (data-less type).
                Assert.False(sink.Files.ContainsKey("vg0x02.DirectSound.bin"));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 8. Null / guard inputs never throw (ROM null, bad base).
        // -----------------------------------------------------------------
        [Fact]
        public void Export_NullAndGuard_Inputs_NoThrow()
        {
            var sink = new Sink();
            // Null ROM.
            SongInstrumentSetCore.ExportAll(null, VOCA_BASE, "vg", sink.WriteFile, sink.WriteLines);
            Assert.Empty(sink.Indexes);

            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Out-of-range base (below the 0x200 safety floor): emits nothing.
                SongInstrumentSetCore.ExportAll(rom, 0x10, "vg", sink.WriteFile, sink.WriteLines);
                Assert.Empty(sink.Indexes);

                // Null delegates: no throw.
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg", null, null);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 9. (Copilot review pt 1) Two top-level nested groups whose CHILD voice
        //    index is 0 must NOT collide: child side files use the nested-index
        //    basename (vg0x00.Drum / vg0x01.Drum), not the shared top-level vg.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_TwoNestedDrums_ChildIndex0_NoCollision()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Two distinct child voicegroups, each with a single 0x03 Wave
                // Memory voice at child index 0.
                uint childA = 0x2000, childB = 0x2100;
                uint waveA = 0xA000, waveB = 0xB000;
                for (int i = 0; i < 16; i++) { rom.write_u8((uint)(waveA + i), (byte)i); rom.write_u8((uint)(waveB + i), (byte)(0x40 + i)); }
                WriteVoice(rom, childA, 0x03, 0, 0, 0, U.toPointer(waveA), 0, 0, 0, 0, 0);
                WriteVoice(rom, childB, 0x03, 0, 0, 0, U.toPointer(waveB), 0, 0, 0, 0, 0);

                // Top-level voices 0 and 1: both drums, pointing at childA / childB.
                WriteVoice(rom, VOCA_BASE + 0x00, 0x80, 0, 0, 0, U.toPointer(childA), 0, 0, 0, 0, 0);
                WriteVoice(rom, VOCA_BASE + 0x0C, 0x80, 0, 0, 0, U.toPointer(childB), 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // Distinct nested indexes + distinct child side files — no collision.
                Assert.True(sink.Indexes.ContainsKey("vg0x00.Drum.instrument"));
                Assert.True(sink.Indexes.ContainsKey("vg0x01.Drum.instrument"));
                Assert.True(sink.Files.ContainsKey("vg0x00.Drum0x00.Wave.bin"));
                Assert.True(sink.Files.ContainsKey("vg0x01.Drum0x00.Wave.bin"));
                // The two child Wave files hold DIFFERENT bytes (proves no overwrite).
                Assert.NotEqual(sink.Files["vg0x00.Drum0x00.Wave.bin"],
                                sink.Files["vg0x01.Drum0x00.Wave.bin"]);
                // The bare top-level name was NEVER used for a nested child.
                Assert.False(sink.Files.ContainsKey("vg0x00.Wave.bin"));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 10. (Copilot review pt 2) A safe pointer with FEWER than the fixed bytes
        //     remaining (Wave 16 / keymap 128) must SKIP the row — never write a
        //     truncated side file.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_WaveMemory_ShortAtEof_SkipsRow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Wave Memory P4 at ROM_LEN - 8 (only 8 bytes remain, < 16).
                uint shortOff = ROM_LEN - 8;
                WriteVoice(rom, VOCA_BASE + 0x00, 0x03, 0, 0, 0, U.toPointer(shortOff), 0, 0, 0, 0, 0);
                // A valid square wave at index 1 so the scan continues past index 0.
                WriteVoice(rom, VOCA_BASE + 0x0C, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // No (short) .Wave.bin; the row is dropped; the square wave survives.
                Assert.False(sink.Files.ContainsKey("vg0x00.Wave.bin"));
                var rows = sink.Indexes["vg.instrument"];
                Assert.Single(rows);
                Assert.Equal("01", Cols(rows[0])[0]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Export_MultiKeymap_ShortAtEof_SkipsRow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Sub-voicegroup with a valid Wave voice.
                uint subBase = 0x2000, subWave = 0xC000;
                for (int i = 0; i < 16; i++) rom.write_u8((uint)(subWave + i), (byte)i);
                WriteVoice(rom, subBase, 0x03, 0, 0, 0, U.toPointer(subWave), 0, 0, 0, 0, 0);

                // Keymap P8 at ROM_LEN - 64 (only 64 bytes remain, < 128).
                uint shortKeymap = ROM_LEN - 64;
                WriteVoice(rom, VOCA_BASE + 0x00, 0x40, 0, 0, 0,
                    U.toPointer(subBase), U.toPointer(shortKeymap), 0, 0, 0, 0);
                WriteVoice(rom, VOCA_BASE + 0x0C, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                var sink = new Sink();
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // Row skipped: no keymap, no nested Multi index written.
                Assert.False(sink.Files.ContainsKey("vg0x00.Multi.keys.bin"));
                Assert.False(sink.Indexes.ContainsKey("vg0x00.Multi.instrument"));
                var rows = sink.Indexes["vg.instrument"];
                Assert.Single(rows);
                Assert.Equal("01", Cols(rows[0])[0]);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 11. (Copilot review pt 3) ExportAll validates against the PASSED rom, not
        //     the ambient CoreState.ROM. With CoreState.ROM unset (or a DIFFERENT,
        //     smaller ROM), the export still works against the explicit rom.
        // -----------------------------------------------------------------
        [Fact]
        public void Export_DoesNotDependOnCoreStateRom()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                uint sampleOff = 0x8000;
                WriteDirectSoundSample(rom, sampleOff, 12000, 64);
                WriteVoice(rom, VOCA_BASE, 0x08, 0x11, 0x22, 0x33,
                    U.toPointer(sampleOff), 0, 0, 0, 0, 0);

                // CoreState.ROM points at a DIFFERENT, tiny ROM (would mis-validate
                // or throw if the export read the ambient length).
                var other = new ROM();
                other.LoadLow("other.gba", new byte[0x1000], "BE8E01");
                CoreState.ROM = other;

                var sink = new Sink();
                // Pass the REAL rom explicitly; CoreState.ROM is the tiny one.
                SongInstrumentSetCore.ExportAll(rom, VOCA_BASE, "vg",
                    sink.WriteFile, sink.WriteLines);

                // The export validated against `rom` (256 KiB), not `other` (4 KiB):
                // the DirectSound row + side file are present.
                Assert.True(sink.Files.ContainsKey("vg0x00.DirectSound.bin"));
                Assert.Equal(12 + 4 + 64, sink.Files["vg0x00.DirectSound.bin"].Length);
                var rows = sink.Indexes["vg.instrument"];
                Assert.Single(rows);
                Assert.Equal("08", Cols(rows[0])[0]);
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
