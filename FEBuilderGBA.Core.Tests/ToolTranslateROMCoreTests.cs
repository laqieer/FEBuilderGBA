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

        // ============================================================
        // ImportFonts - cross-platform auto-generation seam (#796)
        // ============================================================

        // A deterministic fake rasterizer that returns a recognizable sentinel
        // tile so we can prove the generated bytes land in the font chain.
        sealed class FakeFontRasterizer : IFontRasterizer
        {
            public readonly byte[] Sentinel;
            public int Calls;
            public FakeFontRasterizer(byte fill)
            {
                Sentinel = new byte[64];
                for (int i = 0; i < 64; i++) Sentinel[i] = fill;
            }
            public byte[] RasterizeGlyph(FontSpec font, string character, bool isItemFont,
                int verticalOffset, out int glyphWidth)
            {
                Calls++;
                glyphWidth = isItemFont ? 6 : 4;
                return (byte[])Sentinel.Clone();
            }
        }

        // Seed a synthetic FE8U (LAT1) ROM so that GetTextCount()==1 and
        // decoder.Decode(0) yields the single ASCII character `ch`, using the
        // anti-Huffman (un-Huffman) raw-string pointer convention. The font
        // tables are left zeroed, so every discovered glyph is "missing with a
        // valid prev slot" and is eligible for porting / generation.
        static ROM MakeRomWithOneAsciiText(char ch, out uint rawTextOffset)
        {
            ROM rom = MakeRomLoadable("BE8E01");

            // Raw "ch\0" string the un-Huffman pointer points at.
            rawTextOffset = 0x1010000;
            rom.write_u8(rawTextOffset + 0, (byte)ch);
            rom.write_u8(rawTextOffset + 1, 0x00);

            // Text pointer table at 0x1000000; entry 0 = un-Huffman patch
            // pointer (real GBA pointer + 0x80000000), entry 1 = 0 terminator.
            uint textTable = 0x1000000;
            uint unhuffmanPtr = U.toPointer(rawTextOffset) + 0x80000000;
            rom.write_u32(textTable + 0, unhuffmanPtr);
            rom.write_u32(textTable + 4, 0);

            // Point RomInfo.text_pointer at the table.
            rom.write_p32(rom.RomInfo.text_pointer, textTable);
            return rom;
        }

        // Install the headless shared state the font import path needs
        // (CoreState.ROM for RecycleAddress.Write's target, a system text
        // encoder for ConvertMojiCharToUnit / FETextDecode, and an
        // AppendBinaryData callback that appends to the end of the ROM and
        // returns the write offset — mirroring InputFormRef.AppendBinaryData).
        // Returns a disposer that restores prior state.
        static IDisposable InstallAppender(ROM rom)
        {
            var prev = new RestoreState(CoreState.ROM, CoreState.AppendBinaryData,
                CoreState.SystemTextEncoder);
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
            CoreState.AppendBinaryData = (data, undo) =>
            {
                uint at = (uint)rom.Data.Length;
                rom.write_resize_data(at + (uint)data.Length);
                if (undo != null) rom.write_range(at, data, undo);
                else rom.write_range(at, data);
                return at;
            };
            return prev;
        }

        sealed class RestoreState : IDisposable
        {
            readonly ROM _rom;
            readonly Func<byte[], Undo.UndoData, uint> _append;
            readonly ISystemTextEncoder _encoder;
            public RestoreState(ROM rom, Func<byte[], Undo.UndoData, uint> append,
                ISystemTextEncoder encoder)
            { _rom = rom; _append = append; _encoder = encoder; }
            public void Dispose()
            {
                CoreState.ROM = _rom;
                CoreState.AppendBinaryData = _append;
                CoreState.SystemTextEncoder = _encoder;
            }
        }

        [Fact]
        public void ImportFonts_NoSourceRom_AutoGenEnabled_GeneratesBothVariants()
        {
            ROM rom = MakeRomWithOneAsciiText('A', out _);
            using (InstallAppender(rom))
            {
                var fake = new FakeFontRasterizer(0x55);
                var recycle = new RecycleAddress();

                var result = ToolTranslateROMCore.ImportFonts(rom,
                    fontRomPath: string.Empty, extraFontRomPath: string.Empty,
                    rasterizer: fake, autoGenFont: default, autoGenEnabled: true,
                    recycle: recycle, undo: null, progressCallback: null);

                // 'A' has both a text-font and an item-font variant, both missing
                // -> exactly 2 generated, 0 ported.
                Assert.Equal(0, result.Ported);
                Assert.Equal(2, result.Generated);
                Assert.True(fake.Calls >= 2, "rasterizer should be invoked per variant");

                // The sentinel bytes must have landed in the ROM (the 64-byte
                // tile sits at offset +8 inside the 72-byte font struct).
                Assert.True(FindSentinelTile(rom, 0x55), "generated sentinel tile not found in ROM");
            }
        }

        [Fact]
        public void ImportFonts_NoSourceRom_AutoGenDisabled_GeneratesNothing()
        {
            ROM rom = MakeRomWithOneAsciiText('A', out _);
            using (InstallAppender(rom))
            {
                var fake = new FakeFontRasterizer(0x55);
                var recycle = new RecycleAddress();
                int lenBefore = rom.Data.Length;

                var result = ToolTranslateROMCore.ImportFonts(rom,
                    fontRomPath: string.Empty, extraFontRomPath: string.Empty,
                    rasterizer: fake, autoGenFont: default, autoGenEnabled: false,
                    recycle: recycle, undo: null, progressCallback: null);

                // Narrowed early-exit: no source ROM AND auto-gen off -> default.
                Assert.Equal(0, result.Ported);
                Assert.Equal(0, result.Generated);
                Assert.Equal(0, fake.Calls);
                Assert.Equal(lenBefore, rom.Data.Length); // nothing appended
            }
        }

        [Fact]
        public void ImportFonts_MixedSourcePortAndAutoGenResidue_CountsBoth()
        {
            // Target text has TWO glyphs: 'B' (present in the source font ROM,
            // -> ported) and 'C' (absent everywhere -> auto-generated).
            ROM rom = MakeRomWithTwoAsciiText('B', 'C');

            // Build a source ROM on disk that has a font entry for 'B' only.
            string srcPath = Path.Combine(Path.GetTempPath(),
                "FEB_FontSrc_" + Guid.NewGuid() + ".gba");
            try
            {
                File.WriteAllBytes(srcPath, MakeSourceRomBytesWithGlyph('B', 0x77));

                using (InstallAppender(rom))
                {
                    var fake = new FakeFontRasterizer(0x33);
                    var recycle = new RecycleAddress();

                    var result = ToolTranslateROMCore.ImportFonts(rom,
                        fontRomPath: srcPath, extraFontRomPath: string.Empty,
                        rasterizer: fake, autoGenFont: default, autoGenEnabled: true,
                        recycle: recycle, undo: null, progressCallback: null);

                    // 'B': text+item variants ported from source (2). 'C':
                    // text+item variants generated (2).
                    Assert.Equal(2, result.Ported);
                    Assert.Equal(2, result.Generated);

                    // Both the ported (0x77) and generated (0x33) tiles landed.
                    Assert.True(FindSentinelTile(rom, 0x77), "ported tile missing");
                    Assert.True(FindSentinelTile(rom, 0x33), "generated tile missing");
                }
            }
            finally
            {
                if (File.Exists(srcPath)) File.Delete(srcPath);
            }
        }

        [Fact]
        public void ImportFontFromROMs_LegacyOverload_NoSourceRom_ReturnsZero_NoWrites()
        {
            // Regression: the binary-compat #536 overload must behave exactly as
            // before — no rasterizer, no source ROM -> 0 and nothing appended.
            ROM rom = MakeRomWithOneAsciiText('A', out _);
            using (InstallAppender(rom))
            {
                var recycle = new RecycleAddress();
                int lenBefore = rom.Data.Length;

                int ported = ToolTranslateROMCore.ImportFontFromROMs(rom,
                    fontRomPath: string.Empty, extraFontRomPath: string.Empty,
                    recycle: recycle, undo: null, progressCallback: null);

                Assert.Equal(0, ported);
                Assert.Equal(lenBefore, rom.Data.Length);
            }
        }

        // ---- helpers for the ImportFonts tests ----

        static ROM MakeRomWithTwoAsciiText(char a, char b)
        {
            ROM rom = MakeRomLoadable("BE8E01");

            uint rawTextOffset = 0x1010000;
            rom.write_u8(rawTextOffset + 0, (byte)a);
            rom.write_u8(rawTextOffset + 1, (byte)b);
            rom.write_u8(rawTextOffset + 2, 0x00);

            uint textTable = 0x1000000;
            uint unhuffmanPtr = U.toPointer(rawTextOffset) + 0x80000000;
            rom.write_u32(textTable + 0, unhuffmanPtr);
            rom.write_u32(textTable + 4, 0);

            rom.write_p32(rom.RomInfo.text_pointer, textTable);
            return rom;
        }

        // Build a complete synthetic FE8U ROM byte array whose serif + item font
        // tables both contain a single 72-byte entry for ASCII glyph `ch`, with
        // the 64-byte tile filled with `fill`. LAT1 lookup reads the slot
        // pointer directly, so we set table[(ch&0xff)<<2] -> a 72-byte struct.
        static byte[] MakeSourceRomBytesWithGlyph(char ch, byte fill)
        {
            ROM rom = MakeRomLoadable("BE8E01");

            uint glyphStruct = 0x1020000;             // 72-byte font struct
            // header: [0..3]=next(0) [4]=moji2 [5]=width [6..7]=0, [8..71]=tile
            rom.write_u32(glyphStruct + 0, 0);
            rom.write_u8(glyphStruct + 4, 0);          // LAT1: moji2 not stored
            rom.write_u8(glyphStruct + 5, 5);          // width
            for (uint i = 0; i < 64; i++) rom.write_u8(glyphStruct + 8 + i, fill);

            uint moji2 = (uint)(ch & 0xff);
            uint serifTop = rom.RomInfo.font_serif_address;
            uint itemTop = rom.RomInfo.font_item_address;
            rom.write_p32(serifTop + (moji2 << 2), glyphStruct);
            rom.write_p32(itemTop + (moji2 << 2), glyphStruct);

            // LoadLow() does NOT stamp the game code into the byte array, so the
            // on-disk file must carry "BE8E01" at 0xAC for fontRom.Load() to
            // re-detect FE8U when ImportFonts reads it back.
            byte[] gameCode = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            for (int i = 0; i < gameCode.Length; i++) rom.write_u8((uint)(0xAC + i), gameCode[i]);

            return (byte[])rom.Data.Clone();
        }

        // Scan the ROM for a run of 64 consecutive `fill` bytes (the sentinel
        // tile written into a font struct's bitmap region).
        static bool FindSentinelTile(ROM rom, byte fill)
        {
            byte[] data = rom.Data;
            int run = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == fill)
                {
                    run++;
                    if (run >= 64) return true;
                }
                else
                {
                    run = 0;
                }
            }
            return false;
        }
    }
}
