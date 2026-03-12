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
