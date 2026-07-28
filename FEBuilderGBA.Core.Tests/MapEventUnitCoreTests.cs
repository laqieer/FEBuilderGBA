using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapEventUnitCoreTests
    {
        [Fact]
        public void GetCondSlots_WithNullRom_ReturnsEmpty()
        {
            var slots = MapEventUnitCore.GetCondSlots(null);
            Assert.NotNull(slots);
            Assert.Empty(slots);
        }

        [Fact]
        public void IsUnitPlacementType_ReturnsTrueForUnitTypes()
        {
            Assert.True(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.PlayerUnit));
            Assert.True(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.EnemyUnit));
            Assert.True(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.FreemapPlayerUnit));
            Assert.True(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.FreemapEnemyUnit));
        }

        [Fact]
        public void IsUnitPlacementType_ReturnsFalseForNonUnitTypes()
        {
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Turn));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Talk));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Object));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Always));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Tutorial));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Trap));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.StartEvent));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.EndEvent));
            Assert.False(MapEventUnitCore.IsUnitPlacementType(MapEventUnitCore.CondType.Unknown));
        }

        [Fact]
        public void ResolvePlistToEventAddr_WithNullRom_ReturnsNotFound()
        {
            uint result = MapEventUnitCore.ResolvePlistToEventAddr(null, 1);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void ResolvePlistToEventAddr_WithZeroPlist_ReturnsNotFound()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                uint result = MapEventUnitCore.ResolvePlistToEventAddr(null, 0);
                Assert.Equal(U.NOT_FOUND, result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetEventAddrForMap_WithNoRom_ReturnsNotFound()
        {
            uint result = MapEventUnitCore.GetEventAddrForMap(null, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetUnitGroupsForMap_WithNullRom_ReturnsEmpty()
        {
            var list = MapEventUnitCore.GetUnitGroupsForMap(null, 0);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void EnumerateUnits_WithNullRom_ReturnsEmpty()
        {
            var list = MapEventUnitCore.EnumerateUnits(null, 0);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void EnumerateUnits_WithInvalidAddr_ReturnsEmpty()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = MapEventUnitCore.EnumerateUnits(null, 0xFFFFFFFF);
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetAI1Description_ReturnsNonEmpty()
        {
            // Known values
            Assert.Contains("No AI", MapEventUnitCore.GetAI1Description(0x00));
            Assert.Contains("Pursue and attack", MapEventUnitCore.GetAI1Description(0x01));
            Assert.Contains("Boss", MapEventUnitCore.GetAI1Description(0x06));

            // Unknown value
            string desc = MapEventUnitCore.GetAI1Description(0xFF);
            Assert.NotNull(desc);
            Assert.NotEmpty(desc);
        }

        [Fact]
        public void GetAI2Description_ReturnsNonEmpty()
        {
            Assert.Contains("No secondary", MapEventUnitCore.GetAI2Description(0x00));
            Assert.Contains("Pursue when attacked", MapEventUnitCore.GetAI2Description(0x01));

            string desc = MapEventUnitCore.GetAI2Description(0xFF);
            Assert.NotNull(desc);
            Assert.NotEmpty(desc);
        }

        [Fact]
        public void GetAI3Description_ReturnsNonEmpty()
        {
            Assert.Contains("Default target", MapEventUnitCore.GetAI3Description(0x00));

            string desc = MapEventUnitCore.GetAI3Description(0xFF);
            Assert.NotNull(desc);
            Assert.NotEmpty(desc);
        }

        [Fact]
        public void GetAI4Description_ReturnsNonEmpty()
        {
            Assert.Contains("No retreat", MapEventUnitCore.GetAI4Description(0x00));
            Assert.Contains("Retreat when low HP", MapEventUnitCore.GetAI4Description(0x01));

            string desc = MapEventUnitCore.GetAI4Description(0xFF);
            Assert.NotNull(desc);
            Assert.NotEmpty(desc);
        }

        [Fact]
        public void GetCondSlots_FE6_HasPlayerAndEnemySlots()
        {
            // Build a minimal FE6 ROM to get the slot definitions
            var rom = TestHelper.MakeMinimalRom(6);
            var slots = MapEventUnitCore.GetCondSlots(rom);
            Assert.NotEmpty(slots);

            bool hasPlayer = false, hasEnemy = false;
            foreach (var slot in slots)
            {
                if (slot.Type == MapEventUnitCore.CondType.PlayerUnit) hasPlayer = true;
                if (slot.Type == MapEventUnitCore.CondType.EnemyUnit) hasEnemy = true;
            }
            Assert.True(hasPlayer, "FE6 should have PlayerUnit slot");
            Assert.True(hasEnemy, "FE6 should have EnemyUnit slot");
        }

        [Fact]
        public void GetCondSlots_FE7_HasMultipleRouteSlots()
        {
            var rom = TestHelper.MakeMinimalRom(7);
            var slots = MapEventUnitCore.GetCondSlots(rom);
            Assert.NotEmpty(slots);

            int playerCount = 0;
            foreach (var slot in slots)
            {
                if (slot.Type == MapEventUnitCore.CondType.PlayerUnit) playerCount++;
            }
            // FE7 has 4 player slots (Eliwood, Eliwood Hard, Hector, Hector Hard)
            Assert.Equal(4, playerCount);
        }

        [Fact]
        public void GetCondSlots_FE8_Has20Slots()
        {
            var rom = TestHelper.MakeMinimalRom(8);
            var slots = MapEventUnitCore.GetCondSlots(rom);
            Assert.Equal(20, slots.Count);
        }

        [Fact]
        public void StableCondSlots_HaveAuthoritativePerVersionCounts()
        {
            Assert.Equal(7, MapEventUnitCore.GetStableCondSlots(TestHelper.MakeMinimalRom(6)).Count);
            Assert.Equal(16, MapEventUnitCore.GetStableCondSlots(TestHelper.MakeMinimalRom(7)).Count);
            Assert.Equal(20, MapEventUnitCore.GetStableCondSlots(TestHelper.MakeMinimalRom(8)).Count);
        }

        [Fact]
        public void ConfigDataFilename_PureOverload_IsNullSafe()
        {
            string path = U.ConfigDataFilename(null, null, null, null);
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains("config", path);
            Assert.Contains("data", path);
        }

        [Theory]
        [InlineData(6, "eventcond_FE6.en.txt")]
        [InlineData(7, "eventcond_FE7.en.txt")]
        [InlineData(8, "eventcond_FE8.en.txt")]
        public void ConfigDataFilename_UsesShippedPerGameName(int version, string expected)
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_path_" + System.Guid.NewGuid().ToString("N"));
            string dataDir = System.IO.Path.Combine(root, "config", "data");
            System.IO.Directory.CreateDirectory(dataDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dataDir, expected), "");
            try
            {
                string path = U.ConfigDataFilename(
                    "eventcond_", TestHelper.MakeMinimalRom(version), root, "en");
                Assert.Equal(expected, System.IO.Path.GetFileName(path));
            }
            finally
            {
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void StableAndDisplayCondSlots_AreSeparateAndShapeValidated()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_display_" + System.Guid.NewGuid().ToString("N"));
            string dataDir = System.IO.Path.Combine(root, "config", "data");
            System.IO.Directory.CreateDirectory(dataDir);
            ROM rom = TestHelper.MakeMinimalRom(8);
            var stable = MapEventUnitCore.GetStableCondSlots(rom);
            string file = System.IO.Path.Combine(dataDir, "eventcond_FE8.zh.txt");

            var previousBase = CoreState.BaseDirectory;
            var previousLanguage = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "zh";
                MapEventUnitCore.ResetCondSlotCacheForTests();
                System.IO.File.WriteAllLines(file, stable.Select(
                    (slot, index) => CondTypeToken(slot.Type) + "\tLocalized " + index));

                Assert.Equal("Turn Conditions", MapEventUnitCore.GetCondSlots(rom)[0].Name);
                Assert.Equal("Localized 0", MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);

                MapEventUnitCore.ResetCondSlotCacheForTests();
                System.IO.File.WriteAllLines(file, stable.Take(19).Select(
                    (slot, index) => CondTypeToken(slot.Type) + "\tBroken " + index));
                Assert.Equal("Turn Conditions", MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);

                MapEventUnitCore.ResetCondSlotCacheForTests();
                System.IO.File.WriteAllLines(file, stable.Select(
                    (slot, index) => (index == 0 ? "TALK" : CondTypeToken(slot.Type))
                        + "\tWrong type " + index));
                Assert.Equal("Turn Conditions", MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);
            }
            finally
            {
                CoreState.BaseDirectory = previousBase;
                CoreState.Language = previousLanguage;
                MapEventUnitCore.ResetCondSlotCacheForTests();
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void DisplayCondSlots_FallsBackFromPrimaryToFallbackRoot()
        {
            string primaryRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_fallback_" + System.Guid.NewGuid().ToString("N"));
            string fallbackRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_fallback_source_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(primaryRoot);
            string fallbackData = System.IO.Path.Combine(fallbackRoot, "config", "data");
            System.IO.Directory.CreateDirectory(fallbackData);
            ROM rom = TestHelper.MakeMinimalRom(8);
            var stable = MapEventUnitCore.GetStableCondSlots(rom);
            System.IO.File.WriteAllLines(
                System.IO.Path.Combine(fallbackData, "eventcond_FE8.zh.txt"),
                stable.Select((slot, index) =>
                    CondTypeToken(slot.Type) + "\tFallback " + index));

            try
            {
                MapEventUnitCore.ResetCondSlotCacheForTests();

                Assert.Equal("Fallback 0",
                    MapEventUnitCore.GetDisplayCondSlots(
                        rom, primaryRoot, fallbackRoot, "zh")[0].Name);
            }
            finally
            {
                MapEventUnitCore.ResetCondSlotCacheForTests();
                try { System.IO.Directory.Delete(primaryRoot, true); } catch { }
                try { System.IO.Directory.Delete(fallbackRoot, true); } catch { }
            }
        }

        [Fact]
        public void DisplayCondSlots_CacheInvalidatesOnFileAndLanguageChange()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_cache_" + System.Guid.NewGuid().ToString("N"));
            string dataDir = System.IO.Path.Combine(root, "config", "data");
            System.IO.Directory.CreateDirectory(dataDir);
            ROM rom = TestHelper.MakeMinimalRom(8);
            var stable = MapEventUnitCore.GetStableCondSlots(rom);
            string zhFile = System.IO.Path.Combine(dataDir, "eventcond_FE8.zh.txt");
            string jaFile = System.IO.Path.Combine(dataDir, "eventcond_FE8.ja.txt");

            void WriteSlots(string path, string prefix) =>
                System.IO.File.WriteAllLines(path, stable.Select(
                    (slot, index) => CondTypeToken(slot.Type)
                        + "\t" + prefix + " " + index));

            string previousBase = CoreState.BaseDirectory;
            string previousLanguage = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "zh";
                WriteSlots(zhFile, "First");
                WriteSlots(jaFile, "Japanese");
                MapEventUnitCore.ResetCondSlotCacheForTests();
                Assert.Equal("First 0",
                    MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);

                WriteSlots(zhFile, "Updated longer");
                Assert.Equal("Updated longer 0",
                    MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);

                CoreState.Language = "ja";
                Assert.Equal("Japanese 0",
                    MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);
            }
            finally
            {
                CoreState.BaseDirectory = previousBase;
                CoreState.Language = previousLanguage;
                MapEventUnitCore.ResetCondSlotCacheForTests();
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void DisplayCondSlots_AutoUsesTranslationEffectiveLanguage()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "eventcond_auto_" + System.Guid.NewGuid().ToString("N"));
            string dataDir = System.IO.Path.Combine(root, "config", "data");
            string translateDir = System.IO.Path.Combine(root, "config", "translate");
            System.IO.Directory.CreateDirectory(dataDir);
            System.IO.Directory.CreateDirectory(translateDir);
            ROM rom = TestHelper.MakeMinimalRom(8);
            var stable = MapEventUnitCore.GetStableCondSlots(rom);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(translateDir, "zh.txt"), "");
            System.IO.File.WriteAllLines(
                System.IO.Path.Combine(dataDir, "eventcond_FE8.zh.txt"),
                stable.Select((slot, index) =>
                    CondTypeToken(slot.Type) + "\tAuto Localized " + index));

            string previousBase = CoreState.BaseDirectory;
            string previousLanguage = CoreState.Language;
            var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "auto";
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("zh-CN");
                MapEventUnitCore.ResetCondSlotCacheForTests();

                Assert.Equal("Auto Localized 0",
                    MapEventUnitCore.GetDisplayCondSlots(rom)[0].Name);
                Assert.Equal("zh", U.ResolveEffectiveLanguage(
                    CoreState.Language, root));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previousCulture;
                CoreState.BaseDirectory = previousBase;
                CoreState.Language = previousLanguage;
                MapEventUnitCore.ResetCondSlotCacheForTests();
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void EmptyUnitList_UsesEmptyAllegiance()
        {
            ROM rom = TestHelper.MakeMinimalRom(8);
            const uint address = 0x1000;
            Assert.Equal("Empty", MapEventUnitCore.GetUnitListAllegianceName(rom, address));
        }

        [Theory]
        [InlineData(1u, 1u, 0u, "Turn 1 (Player)")]
        [InlineData(2u, 5u, 0x40u, "Turn 2-5 (Ally)")]
        [InlineData(3u, 3u, 0xC0u, "Turn 3 (4th Allegiance)")]
        [InlineData(4u, 6u, 0x20u, "Turn 4-6 (0x20)")]
        public void FormatTurnCondition_UsesSharedPhaseAndRangeContract(
            uint start, uint end, uint phase, string expected)
        {
            Assert.Equal(expected, MapEventUnitCore.FormatTurnCondition(start, end, phase));
        }

        static string CondTypeToken(MapEventUnitCore.CondType type) => type switch
        {
            MapEventUnitCore.CondType.Turn => "TURN",
            MapEventUnitCore.CondType.Talk => "TALK",
            MapEventUnitCore.CondType.Object => "OBJECT",
            MapEventUnitCore.CondType.Always => "ALWAYS",
            MapEventUnitCore.CondType.Tutorial => "TUTORIAL",
            MapEventUnitCore.CondType.Trap => "TRAP",
            MapEventUnitCore.CondType.PlayerUnit => "PLAYER_UNIT",
            MapEventUnitCore.CondType.EnemyUnit => "ENEMY_UNIT",
            MapEventUnitCore.CondType.FreemapPlayerUnit => "FREEMAP_PLAYER_UNIT",
            MapEventUnitCore.CondType.FreemapEnemyUnit => "FREEMAP_ENEMY_UNIT",
            MapEventUnitCore.CondType.StartEvent => "START_EVENT",
            MapEventUnitCore.CondType.EndEvent => "END_EVENT",
            _ => "UNKNOWN",
        };
    }

    /// <summary>Test helper for creating minimal ROM objects.</summary>
    internal static class TestHelper
    {
        public static ROM MakeMinimalRom(int version)
        {
            // ROM.LoadLow requires minimum data sizes:
            // FE7/FE8: 0x1000000 (16MB), FE6: 0x800000 (8MB)
            string versionStr;
            int minSize;
            switch (version)
            {
                case 6:
                    versionStr = "AFEJ01";
                    minSize = 0x800000;
                    break;
                case 7:
                    versionStr = "AE7E01";
                    minSize = 0x1000000;
                    break;
                case 8:
                default:
                    versionStr = "BE8E01";
                    minSize = 0x1000000;
                    break;
            }

            byte[] data = new byte[minSize];
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, versionStr);
            return rom;
        }
    }
}
