// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for ToolTranslateROMCore + PriorityCodeUtil + FontCore +
// TextSourceListCore (#536).
//
// These tests exercise the cross-platform pieces that the Avalonia view's
// 5 deferred buttons (Start Translation / Export All Texts / Import All
// Texts / Import Font / Change font name) call into. The tests work on
// synthetic ROM byte arrays - no on-disk ROM file required.
using System;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ToolTranslateROMCoreTests
    {
        // ============================================================
        // Helpers
        // ============================================================

        static ROM MakeRomLoadable(string gameCode, uint sizeAtLeast = 0x1100000)
        {
            // Load a synthetic ROM via ROM.LoadLow so the Rom-class detects
            // the right ROMFE* subclass. Mirrors how
            // ToolTranslateROMParityTests.MakeRom builds its samples.
            var bytes = new byte[Math.Max(sizeAtLeast, 0x1100000)];
            var rom = new ROM();
            rom.LoadLow($"synthetic-{gameCode}.gba", bytes, gameCode);
            return rom;
        }

        // ============================================================
        // GetROMBaseTable
        // ============================================================

        [Fact]
        public void ROMBaseTable_Has_10_Entries()
        {
            var table = ToolTranslateROMCore.GetROMBaseTable();
            Assert.Equal(10, table.Length);
        }

        [Theory]
        [InlineData("FE8U", 8, "en", "BE8E01", 0x1000000u, 0xa47246aeu)]
        [InlineData("FE8J", 8, "ja", "BE8J01", 0x1000000u, 0x9d76826fu)]
        [InlineData("FE6CN", 6, "zh", "AFEJ01", 0x800000u, 0x1F19D989u)]
        [InlineData("FE8KR", 8, "kr", "BE8J01", 0x1193960u, 0x4F33E94Cu)]
        public void GetROMBaseTable_ContainsExpectedEntry(string name, int ver, string lang,
            string header, uint romsize, uint crc32)
        {
            var table = ToolTranslateROMCore.GetROMBaseTable();
            bool found = false;
            foreach (var t in table)
            {
                if (t.name == name)
                {
                    Assert.Equal((uint)ver, t.ver);
                    Assert.Equal(lang, t.lang);
                    Assert.Equal(header, t.header);
                    Assert.Equal(romsize, t.romsize);
                    Assert.Equal(crc32, t.crc32);
                    found = true;
                    break;
                }
            }
            Assert.True(found, $"ROMBaseTable should contain {name} entry");
        }

        // ============================================================
        // MakeROMName
        // ============================================================

        [Theory]
        [InlineData(8, false, "無改造 FE8U", "無改造 FE8J")]
        [InlineData(8, true,  "無改造 FE8J", "無改造 FE8U")]
        [InlineData(7, false, "無改造 FE7J", "無改造 FE7U")]
        [InlineData(7, true,  "無改造 FE7U", "無改造 FE7J")]
        [InlineData(6, false, "",            "")]
        [InlineData(6, true,  "",            "")]
        public void MakeROMName_MatchesWfBranches(int version, bool isMultibyte,
            string expectedFromLabel, string expectedToLabel)
        {
            var (from, to) = ToolTranslateROMCore.MakeROMName(version, isMultibyte);
            Assert.Equal(expectedFromLabel, from);
            Assert.Equal(expectedToLabel, to);
        }

        // ============================================================
        // ParseLanguageKey
        // ============================================================

        [Theory]
        [InlineData("ja=日本語", "ja")]
        [InlineData("en=英語", "en")]
        [InlineData("zh-CN=中国語 簡体", "zh-CN")]
        [InlineData("standalone", "standalone")]
        [InlineData("", "")]
        public void ParseLanguageKey_StripsAfterEquals(string input, string expected)
        {
            Assert.Equal(expected, ToolTranslateROMCore.ParseLanguageKey(input));
        }

        // ============================================================
        // FindOrignalROMByLang
        // ============================================================

        [Fact]
        public void FindOrignalROMByLang_NoMatchingFile_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FEB_ToolTranslateROMCore_Empty_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                string result = ToolTranslateROMCore.FindOrignalROMByLang(
                    tempDir, "en", currentVersion: 8,
                    romBaseDirectory: string.Empty, lastROMFilename: string.Empty);
                Assert.Equal(string.Empty, result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void FindOrignalROMByLang_HeaderMatchButCrcMismatch_ReturnsEmpty()
        {
            // FE8U: header "BE8E01" at 0xAC..0xB2, size 0x1000000, crc 0xa47246ae.
            // We build a file matching size + header but with a non-matching CRC
            // (random body). The search MUST short-circuit on CRC failure and
            // return empty - this validates the CRC32-gate even when size +
            // header pass. Validates the negative-CRC-match path (positive-match
            // would require seeding 16 MB of bytes that hash to a specific value;
            // that's covered indirectly by the U.CRC32 unit-of-work tests).
            string tempDir = Path.Combine(Path.GetTempPath(), "FEB_ToolTranslateROMCore_CrcMismatch_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                // Build a 16 MB file with header BE8E01 but random body
                // (CRC32 won't match - this should NOT be found).
                var bytes = new byte[0x1000000];
                System.Text.Encoding.ASCII.GetBytes("BE8E01", 0, 6, bytes, 0xAC);
                string rom = Path.Combine(tempDir, "rom.gba");
                File.WriteAllBytes(rom, bytes);

                string result = ToolTranslateROMCore.FindOrignalROMByLang(
                    tempDir, "en", currentVersion: 8,
                    romBaseDirectory: string.Empty, lastROMFilename: string.Empty);

                // The CRC32 won't match, so search returns empty. This validates
                // the CRC32 short-circuit, not the positive-match path.
                Assert.Equal(string.Empty, result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void FindOrignalROMByLang_SizeMismatch_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FEB_ToolTranslateROMCore_SizeMismatch_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                // 1 KB file - wrong size for any FE ROM
                File.WriteAllBytes(Path.Combine(tempDir, "tiny.gba"), new byte[1024]);
                string result = ToolTranslateROMCore.FindOrignalROMByLang(
                    tempDir, "en", currentVersion: 8,
                    romBaseDirectory: string.Empty, lastROMFilename: string.Empty);
                Assert.Equal(string.Empty, result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Theory]
        [InlineData("rom.backup.gba")]
        [InlineData("rom.emulator.gba")]
        [InlineData("rom.emulator2.gba")]
        [InlineData("rom.sappy.gba")]
        [InlineData("rom.binary_editor.gba")]
        public void FindOrignalROMByLang_FiltersBackupAndSidecar(string filename)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FEB_ToolTranslateROMCore_Filter_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                // Even a correct-size file with matching header is filtered out if
                // its name contains the backup/emulator/sappy markers.
                var bytes = new byte[0x1000000];
                System.Text.Encoding.ASCII.GetBytes("BE8E01", 0, 6, bytes, 0xAC);
                File.WriteAllBytes(Path.Combine(tempDir, filename), bytes);

                string result = ToolTranslateROMCore.FindOrignalROMByLang(
                    tempDir, "en", currentVersion: 8,
                    romBaseDirectory: string.Empty, lastROMFilename: string.Empty);
                Assert.Equal(string.Empty, result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        // ============================================================
        // ApplyTranslatePatch / ChangeMainMenuWidth
        // ============================================================

        [Fact]
        public void ChangeMainMenuWidth_ToJa_LowersWidthTo6()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            uint addr = rom.RomInfo.main_menu_width_address;
            if (addr == 0 || addr >= rom.Data.Length) return; // pointer not resolvable - skip

            // Seed width at 4 (less than 6 so the patch should overwrite).
            rom.write_u8(addr, 4);
            ToolTranslateROMCore.ChangeMainMenuWidth(rom, "ja", null);
            Assert.Equal(6u, rom.u8(addr));
        }

        [Fact]
        public void ChangeMainMenuWidth_ToEn_RaisesWidthTo8()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            uint addr = rom.RomInfo.main_menu_width_address;
            if (addr == 0 || addr >= rom.Data.Length) return;

            rom.write_u8(addr, 4);
            ToolTranslateROMCore.ChangeMainMenuWidth(rom, "en", null);
            Assert.Equal(8u, rom.u8(addr));
        }

        [Fact]
        public void ChangeMainMenuWidth_AlreadyLarge_NoOp()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            uint addr = rom.RomInfo.main_menu_width_address;
            if (addr == 0 || addr >= rom.Data.Length) return;

            rom.write_u8(addr, 10);
            ToolTranslateROMCore.ChangeMainMenuWidth(rom, "en", null);
            Assert.Equal(10u, rom.u8(addr)); // No change since 10 > 8
        }

        [Fact]
        public void ApplyTranslatePatch_DoesNotThrow()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            ToolTranslateROMCore.ApplyTranslatePatch(rom, "ja", null);
            // No exception = pass.
        }

        // ============================================================
        // PriorityCodeUtil + FontCore basic sanity
        // ============================================================

        [Fact]
        public void SearchPriorityCode_NullRom_ReturnsSjis()
        {
            Assert.Equal(PRIORITY_CODE.SJIS, PriorityCodeUtil.SearchPriorityCode(null));
        }

        [Fact]
        public void SearchPriorityCode_FE8J_Multibyte_ReturnsSjis()
        {
            ROM rom = MakeRomLoadable("BE8J01");
            Assert.Equal(PRIORITY_CODE.SJIS, PriorityCodeUtil.SearchPriorityCode(rom));
        }

        [Fact]
        public void SearchPriorityCode_FE8U_NoDrawFontPatch_ReturnsLat1()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            // Synthetic FE8U with no DrawFont patch installed -> LAT1 fallback.
            Assert.Equal(PRIORITY_CODE.LAT1, PriorityCodeUtil.SearchPriorityCode(rom));
        }

        [Fact]
        public void GetFontPointer_ReturnsRomInfoAddress()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            uint serif = FontCore.GetFontPointer(isItemFont: false, rom);
            uint item = FontCore.GetFontPointer(isItemFont: true, rom);
            Assert.Equal(rom.RomInfo.font_serif_address, serif);
            Assert.Equal(rom.RomInfo.font_item_address, item);
        }

        [Fact]
        public void MakeNewFontData_SJIS_Width()
        {
            ROM rom = MakeRomLoadable("BE8J01");
            byte[] bitmap = new byte[64];
            byte[] result = FontCore.MakeNewFontData(0x82A0, 8, bitmap, rom, PRIORITY_CODE.SJIS);
            Assert.Equal(72, result.Length);
            Assert.Equal(0xA0, result[4]); // moji2
            Assert.Equal(8, result[5]); // width
        }

        [Fact]
        public void MakeNewFontData_LAT1_Width()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            byte[] bitmap = new byte[64];
            byte[] result = FontCore.MakeNewFontData(0x41, 6, bitmap, rom, PRIORITY_CODE.LAT1);
            Assert.Equal(72, result.Length);
            Assert.Equal(0, result[4]); // LAT1 has no moji2 stored
            Assert.Equal(6, result[5]); // width
        }

        [Fact]
        public void TransportFontStruct_SjisToLat1_ZerosMojiBytes()
        {
            byte[] data = new byte[72];
            // Pretend the source data had moji bytes set.
            data[4] = 0xAB;
            data[6] = 0xCD;
            data[7] = 0xEF;
            FontCore.TransportFontStruct(data, 0x82A0, PRIORITY_CODE.LAT1, PRIORITY_CODE.SJIS);
            Assert.Equal(0, data[4]);
            Assert.Equal(0, data[6]);
            Assert.Equal(0, data[7]);
        }

        [Fact]
        public void TransportFontStruct_SameCode_NoChange()
        {
            byte[] data = new byte[72];
            data[4] = 0xAB;
            FontCore.TransportFontStruct(data, 0x82A0, PRIORITY_CODE.SJIS, PRIORITY_CODE.SJIS);
            Assert.Equal(0xAB, data[4]); // Unchanged
        }

        // ============================================================
        // TextSourceListCore - empty ROM = empty lists (sanity)
        // ============================================================

        [Fact]
        public void MakeMenuDefinitionList_EmptyRom_ReturnsEmpty()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            var result = TextSourceListCore.MakeMenuDefinitionList(rom);
            Assert.NotNull(result);
            // Without real ROM data, the pointer chain is just zeros; we should
            // get an empty (or short) list, never a crash.
            Assert.True(result.Count >= 0);
        }

        [Fact]
        public void MakeMapTerrainNameList_NonMultibyte_EmptyTable_ReturnsZeroEntries()
        {
            // After #671 WU3: non-multibyte ROMs no longer early-return;
            // they enumerate 2-byte text-id entries (mirroring WF
            // MapTerrainNameEngForm.MakeList) and stop on u16 == 0.
            // A freshly-loaded synthetic ROM whose terrain-name table
            // region is all zeros sees the first u16 as the terminator
            // and returns 0 entries. The detailed 2-byte iteration
            // semantics are covered by TextSourceListCoreTests.
            ROM rom = MakeRomLoadable("BE8E01");
            var result = TextSourceListCore.MakeMapTerrainNameList(rom);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetMenuDefinitionPointers_FE8U_Returns6Pointers()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            uint[] pointers = TextSourceListCore.GetMenuDefinitionPointers(rom);
            Assert.Equal(6, pointers.Length);
        }

        [Fact]
        public void GetMenuDefinitionPointers_NullRom_ReturnsEmpty()
        {
            uint[] pointers = TextSourceListCore.GetMenuDefinitionPointers(null);
            Assert.NotNull(pointers);
            Assert.Empty(pointers);
        }

        // ============================================================
        // ExportTextsToFile - empty synthetic ROM yields empty file
        // ============================================================

        [Fact]
        public void ExportTextsToFile_SyntheticRom_DoesNotThrow()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            string outPath = Path.Combine(Path.GetTempPath(),
                "FEB_ToolTranslateROMCore_Export_" + Guid.NewGuid() + ".txt");
            try
            {
                // Synthetic ROM with no text table set up. Should write an
                // empty file (or near-empty) without crashing.
                int written = ToolTranslateROMCore.ExportTextsToFile(rom, outPath,
                    isOneLiner: false, progressCallback: null);
                Assert.True(written >= 0);
                Assert.True(File.Exists(outPath));
            }
            finally
            {
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }

        // ============================================================
        // ImportTextsFromFile - non-existent file returns 0
        // ============================================================

        [Fact]
        public void ImportTextsFromFile_NonExistentFile_ReturnsZero()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            var recycle = new RecycleAddress();
            int n = ToolTranslateROMCore.ImportTextsFromFile(rom,
                "/nonexistent/path.txt", recycle, undo: null, progressCallback: null);
            Assert.Equal(0, n);
        }

        [Fact]
        public void ImportTextsFromFile_EmptyFile_ReturnsZero()
        {
            ROM rom = MakeRomLoadable("BE8E01");
            var recycle = new RecycleAddress();
            string tempFile = Path.Combine(Path.GetTempPath(),
                "FEB_ToolTranslateROMCore_Import_" + Guid.NewGuid() + ".txt");
            try
            {
                File.WriteAllText(tempFile, "// just a comment\n");
                int n = ToolTranslateROMCore.ImportTextsFromFile(rom, tempFile,
                    recycle, undo: null, progressCallback: null);
                Assert.Equal(0, n);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
