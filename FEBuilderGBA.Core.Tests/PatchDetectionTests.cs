using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchDetectionTests : IDisposable
    {
        readonly ROM? _savedRom;

        public PatchDetectionTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            PatchDetection.ClearAllCaches();
        }

        [Fact]
        public void SearchPriorityCode_NullROM_ReturnsLAT1()
        {
            CoreState.ROM = null;
            var result = PatchDetection.SearchPriorityCode();
            Assert.Equal(PatchDetection.PRIORITY_CODE.LAT1, result);
        }

        [Fact]
        public void SearchDrawFontPatch_NullROM_ReturnsNO()
        {
            CoreState.ROM = null;
            PatchDetection.ClearCacheDrawFont();
            var result = PatchDetection.SearchDrawFontPatch();
            Assert.Equal(PatchDetection.draw_font_enum.NO, result);
        }

        [Fact]
        public void SearchPatchBool_NullROM_ReturnsFalse()
        {
            CoreState.ROM = null;
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt
                {
                    name = "Test", ver = "FE8U", addr = 0x100,
                    data = new byte[] { 0x00 }
                }
            };
            Assert.False(PatchDetection.SearchPatchBool(table));
        }

        [Fact]
        public void SearchPatch_NullROM_ReturnsDefault()
        {
            CoreState.ROM = null;
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt
                {
                    name = "Test", ver = "FE8U", addr = 0x100,
                    data = new byte[] { 0x00 }
                }
            };
            var result = PatchDetection.SearchPatch(table);
            Assert.Equal(0u, result.addr);
        }

        [Fact]
        public void SearchPriorityCode_WithRomParam_NullROM_ReturnsSJIS()
        {
            // The ROM-parameter overload returns SJIS for null
            var result = PatchDetection.SearchPriorityCode(null);
            Assert.Equal(PatchDetection.PRIORITY_CODE.SJIS, result);
        }

        [Fact]
        public void ClearAllCaches_ResetsDrawFontAndTextEngine()
        {
            PatchDetection.ClearAllCaches();
            // After clearing, no-arg overloads should re-evaluate
            // With null ROM, they return safe defaults
            CoreState.ROM = null;
            Assert.Equal(PatchDetection.draw_font_enum.NO, PatchDetection.SearchDrawFontPatch());
        }

        // ---------------------------------------------------------------
        // OPClassReel patch detectors (added for gap-sweep #419).
        //
        // - OPClassReelAnimationIDOver255 has ONLY an FE8J signature in
        //   PatchUtil; FE8U must always return false.
        // - OPClassReelSort has BOTH FE8J and FE8U signatures; tests
        //   below assert both versions detect the patch correctly.
        //   (Copilot bot review thread PRRT_kwDOH0Mc1M6ETZ4g on PR #544
        //   flagged the previous wording as misleading.)
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a minimal FE8J ROM (BE8J01 signature, 0x1100000 bytes
        /// so Rom.LoadLow assigns ROMFE8JP). Optionally plants the patch
        /// signature bytes at the well-known addresses.
        /// </summary>
        static ROM MakeFe8jRom(bool plantOver255 = false, bool plantReelSort = false)
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            if (plantOver255)
            {
                // Source: PatchUtil.OPClassReelAnimationIDOver255Low — FE8J @ 0xB86B0 / {0x59, 0x8A}.
                data[0xB86B0] = 0x59;
                data[0xB86B0 + 1] = 0x8A;
            }
            if (plantReelSort)
            {
                // Source: PatchUtil.OPClassReelSortPatchLow — FE8J @ 0xB8C80 / {0x04, 0x4B, 0x1B, 0x68}.
                data[0xB8C80] = 0x04;
                data[0xB8C80 + 1] = 0x4B;
                data[0xB8C80 + 2] = 0x1B;
                data[0xB8C80 + 3] = 0x68;
            }
            rom.LoadLow("synthetic-fe8j.gba", data, "BE8J01");
            return rom;
        }

        /// <summary>Build a minimal FE8U ROM (BE8E01) — Over255 must always be false here.</summary>
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
            return rom;
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(null));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8J_PatchAbsent_ReturnsFalse()
        {
            var rom = MakeFe8jRom(plantOver255: false);
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8J_PatchPresent_ReturnsTrue()
        {
            var rom = MakeFe8jRom(plantOver255: true);
            Assert.True(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8U_AlwaysFalse()
        {
            // The signature only matches "FE8J" — even if we synthetically
            // wrote 0x59, 0x8A at 0xB86B0 on an FE8U ROM, the version
            // filter rejects it.
            var rom = MakeFe8uRom();
            rom.Data[0xB86B0] = 0x59;
            rom.Data[0xB86B0 + 1] = 0x8A;
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(null));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8J_PatchAbsent_ReturnsFalse()
        {
            var rom = MakeFe8jRom(plantReelSort: false);
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8J_PatchPresent_ReturnsTrue()
        {
            var rom = MakeFe8jRom(plantReelSort: true);
            Assert.True(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8U_PatchPresent_ReturnsTrue()
        {
            // OPClassReelSort has both FE8J and FE8U entries (FE8U @ 0xB40EC).
            var rom = MakeFe8uRom();
            rom.Data[0xB40EC] = 0x04;
            rom.Data[0xB40EC + 1] = 0x4B;
            rom.Data[0xB40EC + 2] = 0x1B;
            rom.Data[0xB40EC + 3] = 0x68;
            Assert.True(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8U_PatchAtJpOffset_StillFalse()
        {
            // Writing the FE8J signature at the FE8J offset on an FE8U
            // ROM must NOT match — the version filter rejects it.
            var rom = MakeFe8uRom();
            rom.Data[0xB8C80] = 0x04;
            rom.Data[0xB8C80 + 1] = 0x4B;
            rom.Data[0xB8C80 + 2] = 0x1B;
            rom.Data[0xB8C80 + 3] = 0x68;
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        // ---------------------------------------------------------------
        // IsClassSkillExtendsDetect (added for gap-sweep #415).
        // Mirrors WF SkillConfigSkillSystemForm.IsClassSkillExtendsLow:
        // U.Grep over rom.Data for a 68-byte THUMB code pattern in
        // [0xB00000, 0xC00000) with blocksize=4. Both Avalonia and
        // WinForms now route through this Core helper so the X_LV add
        // panel (Level + 4 mode checkboxes) shows in both UIs when and
        // only when the SkillSystem "class skill extends" patch is installed.
        // ---------------------------------------------------------------
        static readonly byte[] CLASS_SKILL_EXTENDS_PATTERN = new byte[] {
            0xF0, 0xE7, 0x02, 0x2B, 0x12, 0xD0, 0x03, 0x2B, 0x06, 0xD1,
            0x0D, 0x48, 0x42, 0x21, 0x41, 0x5C, 0x20, 0x22, 0x11, 0x42,
            0x0A, 0xD1, 0xE5, 0xE7, 0x04, 0x2B, 0x06, 0xD1, 0x08, 0x48,
            0x14, 0x21, 0x41, 0x5C, 0x40, 0x22, 0x11, 0x42, 0x01, 0xD1,
            0xDC, 0xE7, 0xDB, 0xE7, 0x63, 0x78, 0x33, 0x70, 0x01, 0x36,
            0xD7, 0xE7, 0x00, 0x20, 0x30, 0x70, 0x06, 0xBC, 0xF1, 0xBC,
            0x70, 0x47, 0x00, 0x00, 0xF0, 0xBC, 0x02, 0x02
        };

        [Fact]
        public void IsClassSkillExtendsDetect_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.IsClassSkillExtendsDetect(null));
        }

        [Fact]
        public void IsClassSkillExtendsDetect_PatternAbsent_ReturnsFalse()
        {
            var rom = MakeFe8uRom();
            Assert.False(PatchDetection.IsClassSkillExtendsDetect(rom));
        }

        [Fact]
        public void IsClassSkillExtendsDetect_PlantedPattern_ReturnsTrue()
        {
            // U.Grep scans [0xB00000, 0xC00000) with blocksize=4, so the
            // planted offset must be 4-byte aligned within that window.
            var rom = MakeFe8uRom();
            const uint plantOffset = 0xB00100;
            for (int i = 0; i < CLASS_SKILL_EXTENDS_PATTERN.Length; i++)
            {
                rom.Data[plantOffset + i] = CLASS_SKILL_EXTENDS_PATTERN[i];
            }
            Assert.True(PatchDetection.IsClassSkillExtendsDetect(rom));
        }

        [Fact]
        public void IsClassSkillExtendsDetect_PatternOutsideWindow_ReturnsFalse()
        {
            // Plant the pattern at 0xA00000 - below the [0xB00000, 0xC00000)
            // scan window. The detector must NOT find it.
            var rom = MakeFe8uRom();
            const uint plantOffset = 0xA00000;
            for (int i = 0; i < CLASS_SKILL_EXTENDS_PATTERN.Length; i++)
            {
                rom.Data[plantOffset + i] = CLASS_SKILL_EXTENDS_PATTERN[i];
            }
            Assert.False(PatchDetection.IsClassSkillExtendsDetect(rom));
        }
    }
}
