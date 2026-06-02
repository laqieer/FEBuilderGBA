// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the cross-platform SkillSystems anime EXPORT seam (#910).
    ///
    /// Two kinds of test:
    ///   * Synthetic-FE8J render round-trip — builds a complete anime config
    ///     (frames + LZ77 OBJ/TSA + palettes) in a synthetic ROM and asserts
    ///     ExportSkillAnimation returns the expected frames at 240×(>=160) with
    ///     the right D/S/wait script structure. NO real ROM file needed.
    ///   * Real-template FE8U SkipCode — reads the genuine
    ///     skillanimtemplate*.dmp files (config/patch2 submodule) and asserts
    ///     SkipCode finds the template prefix + the defender flag. Needs the
    ///     config/patch2 submodule checked out (BaseDirectory), NOT a ROM.
    ///
    /// [Collection("SharedState")] because the tests mutate CoreState.ROM /
    /// CoreState.ImageService / CoreState.BaseDirectory.
    /// </summary>
    [Collection("SharedState")]
    public class SkillSystemsAnimeExportCoreTests
    {
        // ---------------------------------------------------------------
        // Synthetic FE8J render round-trip (no real ROM needed)
        // ---------------------------------------------------------------

        [Fact]
        public void ExportSkillAnimation_FE8J_TwoFrames_ReturnsFramesWithStructure()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 3, 5 },
                    soundId: 0x1A);

                var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeOffset);

                Assert.Equal("", result.Error);
                Assert.False(result.IsDefender); // FE8J never sets defender
                Assert.Equal(0x1Au, result.SoundId);
                Assert.Equal(2, result.Frames.Count);

                // Each frame: 240 wide, height >= 160 (the WF min clamp).
                foreach (var f in result.Frames)
                {
                    Assert.NotNull(f.Image);
                    Assert.Equal(240, f.Image.Width);
                    Assert.True(f.Image.Height >= 160,
                        $"frame height {f.Image.Height} should be >= 160");
                }

                Assert.Equal(3u, result.Frames[0].Wait);
                Assert.Equal(5u, result.Frames[1].Wait);

                // BuildScriptLines: no D (FE8J), one S, then 2 wait/filename lines.
                var lines = SkillSystemsAnimeExportCore.BuildScriptLines(result, "skill_");
                Assert.DoesNotContain(lines, l => l.StartsWith("D"));
                Assert.Contains(lines, l => l.StartsWith("S001A"));
                Assert.Contains(lines, l => l.StartsWith("3 ") && l.EndsWith(".png"));
                Assert.Contains(lines, l => l.StartsWith("5 ") && l.EndsWith(".png"));
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void ExportSkillAnimation_FE8J_TerminatorStopsFrameLoop()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // 4 frames then 0xFFFF terminator; a stray 5th frame after the
                // terminator must NOT be exported.
                uint animeOffset = BuildSyntheticAnime(rom.Data,
                    waits: new uint[] { 1, 1, 1, 1 },
                    soundId: 0,
                    trailingGarbageFrame: true);

                var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeOffset);

                Assert.Equal("", result.Error);
                Assert.Equal(4, result.Frames.Count);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void BuildScriptLines_Defender_EmitsDLine()
        {
            var result = new SkillAnimeExportResult
            {
                IsDefender = true,
                SoundId = 0,
                Frames = new List<SkillAnimeFrame>
                {
                    new SkillAnimeFrame { Id = 2, Wait = 7, Image = new StubImage(240, 160) },
                },
            };

            var lines = SkillSystemsAnimeExportCore.BuildScriptLines(result, "abc_");
            Assert.StartsWith("D", lines[0]);
            Assert.Contains(lines, l => l == "7 abc_g002.png");
        }

        [Fact]
        public void CalcHeightByTsa_MatchesWinFormsFormula()
        {
            // 240/8 = 30 cols. 60 entries (120 bytes) → 2 rows; WF then does
            // "if (height % align != 0) height += align" (adds a FULL align,
            // not round-to-multiple): 2 + 8 = 10 rows → 80px.
            Assert.Equal(80, SkillSystemsAnimeExportCore.CalcHeightByTsa(240, 120));
            // 0 entries → 0 rows; 0 % 8 == 0 so no align bump → 0px (the WF
            // result; the >=160 clamp lives in the render path, not here).
            Assert.Equal(0, SkillSystemsAnimeExportCore.CalcHeightByTsa(240, 0));
            // Exactly 30 entries (60 bytes) = 1 full row; 1 % 8 != 0 → +8 → 9
            // rows → 72px (matches WF CalcHeightbyTSA).
            Assert.Equal(72, SkillSystemsAnimeExportCore.CalcHeightByTsa(240, 60));
        }

        // ---------------------------------------------------------------
        // Guards
        // ---------------------------------------------------------------

        [Fact]
        public void ExportSkillAnimation_NullRom_ReturnsError()
        {
            var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(null, 0x300u);
            Assert.NotEqual("", result.Error);
            Assert.Empty(result.Frames);
        }

        [Fact]
        public void ExportSkillAnimation_ForeignRom_Refused()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM active = MakeFE8JRom();
                ROM foreign = MakeFE8JRom();
                CoreState.ROM = active;
                CoreState.ImageService = new StubImageService();

                // Passing a ROM instance that is NOT CoreState.ROM is refused
                // (rom-identity guard) — no throw, just an error result.
                var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(foreign, 0x300u);
                Assert.NotEqual("", result.Error);
                Assert.Empty(result.Frames);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void ExportSkillAnimation_UnsafePointer_HandledNoThrow()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Pointer of 0 (and 1) is below the 0x200 safety floor.
                var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, 0u);
                Assert.NotEqual("", result.Error);
                Assert.Empty(result.Frames);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // FE8U-template SkipCode (real dmp files — needs config/patch2, NOT a ROM)
        // ---------------------------------------------------------------

        [Fact]
        public void SkipCode_FE8U_NonDefenderTemplate_ReturnsConfigAfterTemplate()
        {
            string baseDir = FindRepoRoot();
            string dmp = Path.Combine(baseDir, "config", "patch2", "FE8U",
                "skill", "skillanimtemplate_2016_11_04.dmp");
            if (!File.Exists(dmp))
            {
                throw new InvalidOperationException(
                    "config/patch2 submodule not checked out — needed for the SkipCode template test. " +
                    "Run: git submodule update --init config/patch2");
            }
            byte[] template = File.ReadAllBytes(dmp);

            var prevRom = CoreState.ROM;
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;

                // Plant: template ++ marker config bytes at a safe offset.
                uint start = 0x1000u;
                Array.Copy(template, 0, rom.Data, (int)start, template.Length);
                for (int i = 0; i < 20; i++)
                    rom.Data[start + template.Length + i] = (byte)(0xA0 + i);

                uint cfg = SkillSystemsAnimeExportCore.SkipCode(rom, start, out bool isDef);
                Assert.Equal(start + (uint)template.Length, cfg);
                Assert.False(isDef);
            }
            finally { CoreState.ROM = prevRom; CoreState.BaseDirectory = prevBase; }
        }

        [Fact]
        public void SkipCode_FE8U_DefenderTemplate_SetsDefenderFlag()
        {
            string baseDir = FindRepoRoot();
            string dmp = Path.Combine(baseDir, "config", "patch2", "FE8U",
                "skill", "skillanimtemplate_defender_2017_01_24.dmp");
            if (!File.Exists(dmp))
            {
                throw new InvalidOperationException(
                    "config/patch2 submodule not checked out — needed for the SkipCode defender test.");
            }
            byte[] template = File.ReadAllBytes(dmp);

            var prevRom = CoreState.ROM;
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;

                uint start = 0x2000u;
                Array.Copy(template, 0, rom.Data, (int)start, template.Length);

                uint cfg = SkillSystemsAnimeExportCore.SkipCode(rom, start, out bool isDef);
                Assert.Equal(start + (uint)template.Length, cfg);
                Assert.True(isDef);
            }
            finally { CoreState.ROM = prevRom; CoreState.BaseDirectory = prevBase; }
        }

        [Fact]
        public void SkipCode_FE8U_NonMatchingPrefix_ReturnsNotFound()
        {
            string baseDir = FindRepoRoot();
            string dmp = Path.Combine(baseDir, "config", "patch2", "FE8U",
                "skill", "skillanimtemplate_2016_11_04.dmp");
            if (!File.Exists(dmp))
            {
                throw new InvalidOperationException(
                    "config/patch2 submodule not checked out — needed for the SkipCode no-match test.");
            }

            var prevRom = CoreState.ROM;
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;

                // Fill the head with bytes that do NOT match either template.
                uint start = 0x3000u;
                for (int i = 0; i < 0x150; i++) rom.Data[start + i] = 0xCC;

                uint cfg = SkillSystemsAnimeExportCore.SkipCode(rom, start, out bool isDef);
                Assert.Equal(U.NOT_FOUND, cfg);
                Assert.False(isDef);
            }
            finally { CoreState.ROM = prevRom; CoreState.BaseDirectory = prevBase; }
        }

        [Fact]
        public void SkipCode_FE8J_ReturnsAddressDirectly()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;

                uint cfg = SkillSystemsAnimeExportCore.SkipCode(rom, 0x4000u, out bool isDef);
                Assert.Equal(0x4000u, cfg); // FE8J: config == anime address
                Assert.False(isDef);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        static ROM MakeFE8JRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8j.gba", new byte[0x1000000], "BE8J01");
            return rom;
        }

        static ROM MakeFE8URom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8u.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        // Build a complete FE8J anime structure into the ROM and return the
        // anime OFFSET (== config for FE8J). Layout:
        //   config @ 0x300 : 5 pointers (frames, tsalist, graphiclist, palettelist, sound)
        //   frames @ 0x400 : (u16 id, u16 wait) per frame + 0xFFFF terminator
        //   graphiclist @ 0x500, tsalist @ 0x600, palettelist @ 0x700 : per-id pointers
        //   obj LZ77 @ 0x1000, tsa LZ77 @ 0x2000, palette @ 0x3000 (shared by all ids)
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

            // config (pointers are GBA pointers; rom.p32 strips them).
            WriteU32(data, config + 0,  ToPtr(frames));
            WriteU32(data, config + 4,  ToPtr(tsalist));
            WriteU32(data, config + 8,  ToPtr(graphiclist));
            WriteU32(data, config + 12, ToPtr(palettelist));
            WriteU32(data, config + 16, soundId);

            // graphiclist/tsalist/palettelist: id 0 → the shared resources.
            WriteU32(data, graphiclist + 0, ToPtr(objLz));
            WriteU32(data, tsalist + 0,     ToPtr(tsaLz));
            WriteU32(data, palettelist + 0, ToPtr(palOff));

            // frames: each frame uses id 0 with the given wait.
            uint fp = frames;
            for (int i = 0; i < waits.Length; i++)
            {
                WriteU16(data, fp + 0, 0);                 // id
                WriteU16(data, fp + 2, (ushort)waits[i]);  // wait
                fp += 4;
            }
            WriteU16(data, fp + 0, 0xFFFF); // terminator
            WriteU16(data, fp + 2, 0xFFFF);
            fp += 4;
            if (trailingGarbageFrame)
            {
                WriteU16(data, fp + 0, 0);   // stray frame after terminator
                WriteU16(data, fp + 2, 99);
            }

            // OBJ + TSA LZ77 (small zero-filled buffers). 60 TSA entries (120
            // bytes) → CalcHeightByTsa(240,120)=64, then clamped to 160.
            PlantZeroLZ77(data, objLz, 0x800);
            PlantZeroLZ77(data, tsaLz, 120);
            PlantPalette(data, palOff);

            return config; // FE8J: anime offset == config
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
                data[pos++] = 0x00; // 8 literal flags
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

        static string FindRepoRoot()
        {
            string asm = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(asm);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate FEBuilderGBA.sln from " + asm);
        }
    }
}
