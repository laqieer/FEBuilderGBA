// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SkillSystemsAnimeExportCore.CountSkillFrames (#1115 probe-before-open).
//
// CountSkillFrames is the cheap "would the seed populate?" probe used by the
// SkillConfig "Jump to Animation Creator" handlers. It walks the SAME frame
// stream as ExportSkillAnimation but renders NOTHING, so it needs neither the
// ImageService nor the active-ROM identity guard. These tests assert:
//   * frame count == ExportSkillAnimation's frame count (parity) on a synthetic
//     FE8J anime,
//   * the 0xFFFF terminator stops the walk (a trailing garbage frame is excluded),
//   * structural faults (null rom / unsafe pointer / bad config) return 0 (never throw).
using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SkillSystemsAnimeExportCoreCountTests
    {
        [Fact]
        public void CountSkillFrames_FE8J_TwoFrames_MatchesExportCount()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 3, 5 }, soundId: 0x1A);

                // Probe (no render) must agree with the heavy export's frame count.
                int probe = SkillSystemsAnimeExportCore.CountSkillFrames(rom, animeOffset);
                var export = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeOffset);

                Assert.Equal("", export.Error);
                Assert.Equal(2, probe);
                Assert.Equal(export.Frames.Count, probe);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void CountSkillFrames_FE8J_TerminatorStopsWalk()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;

                // 4 real frames + 0xFFFF terminator + a stray 5th frame AFTER it.
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 1, 1, 1, 1 }, soundId: 0,
                    trailingGarbageFrame: true);

                // Counts the 4 pre-terminator frames only — no render / ImageService.
                Assert.Equal(4, SkillSystemsAnimeExportCore.CountSkillFrames(rom, animeOffset));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void CountSkillFrames_TerminatorFirst_ReturnsZero()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;

                // Empty stream: the first frame entry is the terminator.
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: Array.Empty<uint>(), soundId: 0);

                Assert.Equal(0, SkillSystemsAnimeExportCore.CountSkillFrames(rom, animeOffset));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void CountSkillFrames_NullRom_ReturnsZero()
        {
            Assert.Equal(0, SkillSystemsAnimeExportCore.CountSkillFrames(null, 0x300u));
        }

        [Fact]
        public void CountSkillFrames_UnsafePointer_ReturnsZeroNoThrow()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                // 0 is below the 0x200 safety floor.
                Assert.Equal(0, SkillSystemsAnimeExportCore.CountSkillFrames(rom, 0u));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void CountSkillFrames_BadConfigPointers_ReturnsZeroNoThrow()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                // Point at a config whose frames pointer is garbage (all-FF region).
                const uint cfg = 0x5000;
                WriteU32(rom.Data, cfg + 0, 0xFFFFFFFF); // frames ptr (unsafe)
                Assert.Equal(0, SkillSystemsAnimeExportCore.CountSkillFrames(rom, cfg));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void CountSkillFrames_InvalidTsaListPointer_ReturnsZero()
        {
            // Mirrors ExportSkillAnimation's pre-loop validation: an invalid
            // tsa/graphic/palette list pointer must make the probe report 0 so a
            // non-zero count reliably means a SEEDABLE animation (Copilot PR #1137).
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 1, 1 }, soundId: 0);
                // Corrupt the tsalist pointer (config + 4) to an unsafe value.
                WriteU32(rom.Data, animeOffset + 4, 0xFFFFFFFF);

                Assert.Equal(0, SkillSystemsAnimeExportCore.CountSkillFrames(rom, animeOffset));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ReadFrameMetas_ReturnsIdAndWaitPerFrame()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 3, 5, 7 }, soundId: 0);

                var metas = SkillSystemsAnimeExportCore.ReadFrameMetas(rom, animeOffset);
                Assert.Equal(3, metas.Count);
                Assert.Equal(3u, metas[0].Wait);
                Assert.Equal(5u, metas[1].Wait);
                Assert.Equal(7u, metas[2].Wait);
                // The synthetic builder uses id 0 for every frame.
                Assert.All(metas, m => Assert.Equal(0u, m.Id));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ReadFrameMetas_InvalidPaletteList_ReturnsEmpty()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 1 }, soundId: 0);
                // Corrupt the palettelist pointer (config + 12).
                WriteU32(rom.Data, animeOffset + 12, 0xFFFFFFFF);

                Assert.Empty(SkillSystemsAnimeExportCore.ReadFrameMetas(rom, animeOffset));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ReadFrameMetas_NonEmpty_ButExportErrors_OnBadPerFrameResource()
        {
            // Documents WHY the jump helper probes with the FULL ExportSkillAnimation
            // (Copilot PR #1137 review): ReadFrameMetas validates the four LIST base
            // pointers but NOT each frame's resolved OBJ/TSA/pal offset. A config whose
            // graphiclist[0] dereferences to an UNSAFE obj offset passes the meta read
            // (frames non-empty) yet ExportSkillAnimation reports an error — so the
            // helper must gate the window-open on the full export, not the count.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 1 }, soundId: 0);

                // The list pointers stay valid (so ReadFrameMetas succeeds), but
                // graphiclist[0] now points to an UNSAFE obj offset (below the 0x200
                // safety floor) so RenderFrame -> ExportSkillAnimation errors.
                // graphiclist is at config + 8 -> deref -> [id 0] u32.
                uint graphiclist = U.toOffset(rom.p32(animeOffset + 8));
                WriteU32(rom.Data, graphiclist + 0, 0x08000010); // -> offset 0x10 (unsafe)

                Assert.NotEmpty(SkillSystemsAnimeExportCore.ReadFrameMetas(rom, animeOffset));
                var export = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeOffset);
                Assert.NotEqual("", export.Error);
                Assert.Empty(export.Frames);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void CountSkillFrames_DoesNotRequireImageService()
        {
            // CountSkillFrames must work even with NO image service — it renders
            // nothing (unlike ExportSkillAnimation which errors without it).
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = null; // explicitly none

                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 2, 2, 2 }, soundId: 0);

                Assert.Equal(3, SkillSystemsAnimeExportCore.CountSkillFrames(rom, animeOffset));
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ===============================================================
        // Helpers (mirror SkillSystemsAnimeExportCoreTests synthetic builder)
        // ===============================================================

        static ROM MakeFE8JRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8j.gba", new byte[0x1000000], "BE8J01");
            return rom;
        }

        static uint BuildSyntheticAnime(byte[] data, uint[] waits, uint soundId,
            bool trailingGarbageFrame = false)
        {
            const uint config = 0x300;
            const uint frames = 0x400;
            const uint graphiclist = 0x500;
            const uint tsalist = 0x600;
            const uint palettelist = 0x700;
            const uint objLz = 0x1000;
            const uint tsaLz = 0x2000;
            const uint palOff = 0x3000;

            WriteU32(data, config + 0,  ToPtr(frames));
            WriteU32(data, config + 4,  ToPtr(tsalist));
            WriteU32(data, config + 8,  ToPtr(graphiclist));
            WriteU32(data, config + 12, ToPtr(palettelist));
            WriteU32(data, config + 16, soundId);

            WriteU32(data, graphiclist + 0, ToPtr(objLz));
            WriteU32(data, tsalist + 0,     ToPtr(tsaLz));
            WriteU32(data, palettelist + 0, ToPtr(palOff));

            uint fp = frames;
            for (int i = 0; i < waits.Length; i++)
            {
                WriteU16(data, fp + 0, 0);
                WriteU16(data, fp + 2, (ushort)waits[i]);
                fp += 4;
            }
            WriteU16(data, fp + 0, 0xFFFF);
            WriteU16(data, fp + 2, 0xFFFF);
            fp += 4;
            if (trailingGarbageFrame)
            {
                WriteU16(data, fp + 0, 0);
                WriteU16(data, fp + 2, 99);
            }

            PlantZeroLZ77(data, objLz, 0x800);
            PlantZeroLZ77(data, tsaLz, 120);
            PlantPalette(data, palOff);
            return config;
        }

        static uint ToPtr(uint off) => off + 0x08000000u;

        static void PlantZeroLZ77(byte[] data, uint offset, int uncompressedSize)
        {
            data[offset + 0] = 0x10;
            data[offset + 1] = (byte)(uncompressedSize & 0xFF);
            data[offset + 2] = (byte)((uncompressedSize >> 8) & 0xFF);
            data[offset + 3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int remaining = uncompressedSize;
            int pos = (int)offset + 4;
            while (remaining > 0 && pos + 1 < data.Length)
            {
                int count = remaining < 8 ? remaining : 8;
                data[pos++] = 0x00;
                for (int k = 0; k < count && pos < data.Length; k++, remaining--)
                    data[pos++] = 0x00;
            }
        }

        static void PlantPalette(byte[] data, uint offset)
        {
            for (int i = 0; i < 16; i++)
            {
                ushort color = (i == 0) ? (ushort)0 : (ushort)0x7FFF;
                data[offset + i * 2 + 0] = (byte)(color & 0xFF);
                data[offset + i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
        }

        static void WriteU16(byte[] data, uint offset, int value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteU32(byte[] data, uint offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
