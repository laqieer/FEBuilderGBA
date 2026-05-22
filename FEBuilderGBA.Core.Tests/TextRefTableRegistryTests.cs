using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="TextRefTableRegistry"/>. Verifies the descriptor
    /// list returned per ROM version matches the WinForms
    /// <c>U.MakeVarsIDArray</c> dispatch (FE6 / FE7 multibyte / FE7 non-multibyte / FE8).
    ///
    /// All tests build a 32 MB ROM with the version byte populated and load it
    /// via <c>rom.LoadLow("fake.gba", data, versionString)</c> which is the
    /// same path the application uses on real ROMs (just over a zero-filled
    /// buffer). The ROMFEINFO constructor reads from rom.Data for patch
    /// detection but proceeds even when the read returns zeros.
    /// </summary>
    public class TextRefTableRegistryTests
    {
        // ---------- Helpers ----------

        static ROM MakeRom(string versionTag)
        {
            var rom = new ROM();
            var data = new byte[0x200_0000]; // 32 MB
            rom.LoadLow("fake.gba", data, versionTag);
            return rom;
        }

        static ROM MakeRomFE6() => MakeRom("AFEJ01");
        static ROM MakeRomFE7JP() => MakeRom("AE7J01");
        static ROM MakeRomFE7U() => MakeRom("AE7E01");
        static ROM MakeRomFE8JP() => MakeRom("BE8J01");
        static ROM MakeRomFE8U() => MakeRom("BE8E01");

        // ---------- Basic robustness ----------

        [Fact]
        public void BuildForRom_NullRom_ReturnsEmptyList()
        {
            var list = TextRefTableRegistry.BuildForRom(null!);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void BuildForRom_RomWithoutInfo_ReturnsEmptyList()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x1000]);
            // RomInfo is null until LoadLow runs
            var list = TextRefTableRegistry.BuildForRom(rom);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        // ---------- Per-version "all expected kinds" tests ----------

        [Fact]
        public void BuildForRom_FE8U_IncludesExpectedKinds()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var kinds = tables.Select(t => t.Kind).ToHashSet();

            Assert.Contains("Unit", kinds);
            Assert.Contains("Class", kinds);
            Assert.Contains("Item", kinds);
            Assert.Contains("MapSetting", kinds);
            Assert.Contains("SupportTalk", kinds);
            Assert.Contains("Haiku", kinds);
            Assert.Contains("BattleTalk", kinds);
            Assert.Contains("SoundRoom", kinds);
            Assert.Contains("WorldMapPoint", kinds);
            Assert.Contains("ED_Epithet", kinds);
            Assert.Contains("ED_Epilogue_A", kinds);
            Assert.Contains("ED_Epilogue_B", kinds);
            Assert.Contains("OPClassDemo", kinds);
            Assert.Contains("StatusOption", kinds);
            Assert.Contains("UnitsMenu", kinds);
            Assert.Contains("Dic", kinds);
            Assert.Contains("DicChapter", kinds);
            Assert.Contains("DicTitle", kinds);
            // FE8U is NOT multibyte so MapTerrain IS expected
            Assert.Contains("MapTerrain", kinds);
            // FE7-only descriptors must NOT appear
            Assert.DoesNotContain("FinalSerif", kinds);
            Assert.DoesNotContain("Senseki", kinds);
            Assert.DoesNotContain("ED_Lyn", kinds);
        }

        [Fact]
        public void BuildForRom_FE8JP_IsMultibyte_NoMapTerrain()
        {
            var rom = MakeRomFE8JP();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var kinds = tables.Select(t => t.Kind).ToHashSet();

            // FE8J is multibyte: MapTerrain should NOT be included.
            Assert.DoesNotContain("MapTerrain", kinds);
            // OPClassDemo present
            Assert.Contains("OPClassDemo", kinds);
            // OPClassDemo for FE8J uses size 28 (multibyte) with offset {4}
            var op = tables.First(t => t.Kind == "OPClassDemo");
            Assert.Equal(28u, op.EntrySize);
            Assert.Equal(new uint[] { 4 }, op.TextIdOffsets);
        }

        [Fact]
        public void BuildForRom_FE7JP_IncludesExpectedKinds()
        {
            var rom = MakeRomFE7JP();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var kinds = tables.Select(t => t.Kind).ToHashSet();

            Assert.Contains("MapSetting", kinds);
            Assert.Contains("SupportTalk", kinds);
            Assert.Contains("Haiku", kinds);
            Assert.Contains("BattleTalk", kinds);
            Assert.Contains("BattleTalk2", kinds);
            Assert.Contains("SoundRoom", kinds);
            Assert.Contains("ED_Epithet", kinds);
            Assert.Contains("ED_Eliwood", kinds);
            Assert.Contains("ED_Hector", kinds);
            Assert.Contains("ED_Lyn", kinds); // FE7 only
            Assert.Contains("OPClassDemo", kinds);
            Assert.Contains("FinalSerif", kinds); // FE7 only
            Assert.Contains("Senseki", kinds); // FE7 only
            // FE7J is multibyte: MapTerrain not included
            Assert.DoesNotContain("MapTerrain", kinds);
            // FE8-only kinds must not appear
            Assert.DoesNotContain("WorldMapPoint", kinds);
            Assert.DoesNotContain("Dic", kinds);

            // MapSetting for FE7J uses 8-offset list
            var ms = tables.First(t => t.Kind == "MapSetting");
            Assert.Equal(8, ms.TextIdOffsets.Length);
            // Specifically: { 112, 114, 118, 120, 122, 124, 136, 138 }
            Assert.Equal(new uint[] { 112, 114, 118, 120, 122, 124, 136, 138 }, ms.TextIdOffsets);
        }

        [Fact]
        public void BuildForRom_FE7U_UsesTenOffsetMapSetting()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var ms = tables.First(t => t.Kind == "MapSetting");
            // FE7U MapSetting offsets:
            // { 112,114,116,118,122,124,126,128,140,142 } — 10 offsets
            Assert.Equal(new uint[] { 112, 114, 116, 118, 122, 124, 126, 128, 140, 142 }, ms.TextIdOffsets);

            // FE7U is NOT multibyte: MapTerrain included.
            var kinds = tables.Select(t => t.Kind).ToHashSet();
            Assert.Contains("MapTerrain", kinds);
            // OPClassDemo size 28 (non-multibyte) with offset {4}
            var op = tables.First(t => t.Kind == "OPClassDemo");
            Assert.Equal(28u, op.EntrySize);
            Assert.Equal(new uint[] { 4 }, op.TextIdOffsets);
        }

        [Fact]
        public void BuildForRom_FE6JP_IncludesExpectedKinds()
        {
            var rom = MakeRomFE6();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var kinds = tables.Select(t => t.Kind).ToHashSet();

            Assert.Contains("MapSetting", kinds);
            Assert.Contains("SupportTalk", kinds);
            Assert.Contains("Haiku", kinds);
            Assert.Contains("BattleTalk", kinds);
            Assert.Contains("BattleTalk2", kinds); // FE6 also has event_ballte_talk2_pointer
            Assert.Contains("SoundRoom", kinds);
            Assert.Contains("ED", kinds);

            // FE6 doesn't have these
            Assert.DoesNotContain("WorldMapPoint", kinds);
            Assert.DoesNotContain("StatusOption", kinds);
            Assert.DoesNotContain("UnitsMenu", kinds);
            Assert.DoesNotContain("Dic", kinds);
            Assert.DoesNotContain("FinalSerif", kinds);
            Assert.DoesNotContain("Senseki", kinds);
            Assert.DoesNotContain("ED_Lyn", kinds);
            Assert.DoesNotContain("OPClassDemo", kinds); // commented out in WinForms U.MakeVarsIDArray

            // MapSetting offsets for FE6: { 48, 50, 52, 60 }
            var ms = tables.First(t => t.Kind == "MapSetting");
            Assert.Equal(new uint[] { 48, 50, 52, 60 }, ms.TextIdOffsets);

            // SoundRoom offsets for FE6: { 4, 8 }
            var sr = tables.First(t => t.Kind == "SoundRoom");
            Assert.Equal(new uint[] { 4, 8 }, sr.TextIdOffsets);

            // ED offsets for FE6: { 0, 2, 4, 6 }
            var ed = tables.First(t => t.Kind == "ED");
            Assert.Equal(new uint[] { 0, 2, 4, 6 }, ed.TextIdOffsets);
            Assert.Equal(8u, ed.EntrySize);
        }

        // ---------- Specific offset / entry-size sanity checks ----------

        [Fact]
        public void FE8U_SupportTalk_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var st = tables.First(t => t.Kind == "SupportTalk");
            // FE8 SupportTalkForm uses size 16 and offsets { 4, 6, 8 }
            Assert.Equal(16u, st.EntrySize);
            Assert.Equal(new uint[] { 4, 6, 8 }, st.TextIdOffsets);
        }

        [Fact]
        public void FE7U_SupportTalk_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var st = tables.First(t => t.Kind == "SupportTalk");
            // FE7 SupportTalkFE7Form uses size 20 and offsets { 4, 8, 12 }
            Assert.Equal(20u, st.EntrySize);
            Assert.Equal(new uint[] { 4, 8, 12 }, st.TextIdOffsets);
        }

        [Fact]
        public void FE6_SupportTalk_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE6();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var st = tables.First(t => t.Kind == "SupportTalk");
            // FE6 SupportTalkFE6Form uses size 16 and offsets { 4, 8, 12 }
            Assert.Equal(16u, st.EntrySize);
            Assert.Equal(new uint[] { 4, 8, 12 }, st.TextIdOffsets);
        }

        [Fact]
        public void FE6_EventHaiku_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE6();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var h = tables.First(t => t.Kind == "Haiku");
            // FE6 EventHaikuFE6Form uses size 16 and offsets { 4, 12 }
            Assert.Equal(16u, h.EntrySize);
            Assert.Equal(new uint[] { 4, 12 }, h.TextIdOffsets);
        }

        [Fact]
        public void FE7U_EventHaiku_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var h = tables.First(t => t.Kind == "Haiku");
            // FE7 EventHaikuFE7Form: size 16, offsets { 4 } (event ptr at 8 deferred)
            Assert.Equal(16u, h.EntrySize);
            Assert.Equal(new uint[] { 4 }, h.TextIdOffsets);
        }

        [Fact]
        public void FE8U_EventHaiku_HasCorrectOffsetsAndSize()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var h = tables.First(t => t.Kind == "Haiku");
            // FE8 EventHaikuForm: size 12, offset { 6 } (event ptr at 8 deferred)
            Assert.Equal(12u, h.EntrySize);
            Assert.Equal(new uint[] { 6 }, h.TextIdOffsets);
        }

        [Fact]
        public void FE6_EventBattleTalk_HasCorrectSizes()
        {
            var rom = MakeRomFE6();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var bt = tables.First(t => t.Kind == "BattleTalk");
            Assert.Equal(12u, bt.EntrySize); // FE6 main = 12
            Assert.Equal(new uint[] { 4 }, bt.TextIdOffsets);
            var bt2 = tables.First(t => t.Kind == "BattleTalk2");
            Assert.Equal(16u, bt2.EntrySize); // FE6 N table = 16
            Assert.Equal(new uint[] { 4 }, bt2.TextIdOffsets);
        }

        [Fact]
        public void FE7U_EventBattleTalk_HasCorrectSizes()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var bt = tables.First(t => t.Kind == "BattleTalk");
            Assert.Equal(16u, bt.EntrySize); // FE7 main = 16
            Assert.Equal(new uint[] { 4 }, bt.TextIdOffsets);
            var bt2 = tables.First(t => t.Kind == "BattleTalk2");
            Assert.Equal(12u, bt2.EntrySize); // FE7 N1 table = 12
        }

        [Fact]
        public void FE8U_EventBattleTalk_HasSize16Offset8()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var bt = tables.First(t => t.Kind == "BattleTalk");
            Assert.Equal(16u, bt.EntrySize);
            Assert.Equal(new uint[] { 8 }, bt.TextIdOffsets);
        }

        [Fact]
        public void FE7_ED_Lyn_UsesDirectBaseNotPointerField()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var ed = tables.First(t => t.Kind == "ED_Lyn");
            // ed_3c_pointer is a DIRECT BASE in FE7, not a pointer field —
            // see ROMFE7*.cs comment "ポインタ指定できない".
            Assert.NotEqual(0u, ed.DirectBase);
            Assert.Equal(0u, ed.PointerField);
            Assert.Equal(12u, ed.EntrySize);
            Assert.Equal(new uint[] { 4, 8 }, ed.TextIdOffsets);
        }

        [Fact]
        public void FE8U_MapSetting_HasFourOffsets()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var ms = tables.First(t => t.Kind == "MapSetting");
            // FE8 MapSettingForm.MakeVarsIDArray uses { 112, 114, 136, 138 } —
            // 4 offsets, NOT the 8-offset list (that's FE7J).
            Assert.Equal(new uint[] { 112, 114, 136, 138 }, ms.TextIdOffsets);
        }

        [Fact]
        public void FE7U_Senseki_HasThreeOffsets()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var s = tables.First(t => t.Kind == "Senseki");
            // EDSensekiCommentForm.MakeVarsIDArray uses { 4, 8, 12 } — 3 offsets.
            Assert.Equal(new uint[] { 4, 8, 12 }, s.TextIdOffsets);
            Assert.Equal(16u, s.EntrySize);
        }

        [Fact]
        public void FE7_FinalSerif_HasSize8Offset4()
        {
            var rom = MakeRomFE7U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var fs = tables.First(t => t.Kind == "FinalSerif");
            Assert.Equal(8u, fs.EntrySize);
            Assert.Equal(new uint[] { 4 }, fs.TextIdOffsets);
        }

        [Fact]
        public void FE8U_StatusOption_HasNineOffsets()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var so = tables.First(t => t.Kind == "StatusOption");
            // StatusOptionForm.MakeVarsIDArray uses { 0,4,6,12,14,20,22,28,30 }
            Assert.Equal(new uint[] { 0, 4, 6, 12, 14, 20, 22, 28, 30 }, so.TextIdOffsets);
            Assert.Equal(44u, so.EntrySize);
        }

        [Fact]
        public void FE8U_WorldMapPoint_HasSize32Offset28()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var wmp = tables.First(t => t.Kind == "WorldMapPoint");
            Assert.Equal(32u, wmp.EntrySize);
            Assert.Equal(new uint[] { 28 }, wmp.TextIdOffsets);
        }

        [Fact]
        public void FE8U_TextDic_ThreeSubTables()
        {
            var rom = MakeRomFE8U();
            var tables = TextRefTableRegistry.BuildForRom(rom);
            var dic = tables.First(t => t.Kind == "Dic");
            Assert.Equal(12u, dic.EntrySize);
            Assert.Equal(new uint[] { 2, 4 }, dic.TextIdOffsets);
            var dch = tables.First(t => t.Kind == "DicChapter");
            Assert.Equal(4u, dch.EntrySize);
            Assert.Equal(9u, dch.MaxCount);
            var dt = tables.First(t => t.Kind == "DicTitle");
            Assert.Equal(2u, dt.EntrySize);
            Assert.Equal(12u, dt.MaxCount);
        }

        // ---------- Configuration validity sweep ----------

        [Fact]
        public void All_Descriptors_HaveValidConfiguration()
        {
            // For each per-version ROM, every descriptor must satisfy:
            //   - exactly one of PointerField or DirectBase is non-zero
            //   - EntrySize > 0
            //   - MaxCount > 0
            //   - TextIdOffsets non-empty
            var roms = new (string Name, ROM Rom)[]
            {
                ("FE6JP",  MakeRomFE6()),
                ("FE7JP",  MakeRomFE7JP()),
                ("FE7U",   MakeRomFE7U()),
                ("FE8JP",  MakeRomFE8JP()),
                ("FE8U",   MakeRomFE8U()),
            };
            foreach (var (name, rom) in roms)
            {
                var tables = TextRefTableRegistry.BuildForRom(rom);
                foreach (var t in tables)
                {
                    Assert.True(t.PointerField != 0 || t.DirectBase != 0,
                        $"{name}: descriptor {t.Kind} has no PointerField and no DirectBase");
                    Assert.True(t.EntrySize > 0,
                        $"{name}: descriptor {t.Kind} has EntrySize == 0");
                    Assert.True(t.MaxCount > 0,
                        $"{name}: descriptor {t.Kind} has MaxCount == 0");
                    Assert.NotEmpty(t.TextIdOffsets);
                }
            }
        }

        [Fact]
        public void All_Descriptors_HaveUniqueKindLabels_PerVersion()
        {
            // Sanity: each (version, Kind) combination must be unique.
            // Duplicates would cause ambiguous references in the UI.
            var roms = new (string Name, ROM Rom)[]
            {
                ("FE6JP",  MakeRomFE6()),
                ("FE7JP",  MakeRomFE7JP()),
                ("FE7U",   MakeRomFE7U()),
                ("FE8JP",  MakeRomFE8JP()),
                ("FE8U",   MakeRomFE8U()),
            };
            foreach (var (name, rom) in roms)
            {
                var tables = TextRefTableRegistry.BuildForRom(rom);
                var dupes = tables.GroupBy(t => t.Kind)
                                  .Where(g => g.Count() > 1)
                                  .Select(g => g.Key)
                                  .ToList();
                Assert.Empty(dupes);
            }
        }
    }
}
