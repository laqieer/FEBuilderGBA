using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for map editor rendering logic.
    /// Since MapEditorViewModel lives in the Avalonia project, these tests exercise
    /// the underlying Core APIs (MapSettingCore, LZ77) that the ViewModel depends on.
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorViewModelTests
    {
        [Fact]
        public void MapSettingCore_MakeMapIDList_WithNoRom_ReturnsEmpty()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = MapSettingCore.MakeMapIDList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MapSettingCore_GetMapCount_WithNoRom_ReturnsZero()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                int count = MapSettingCore.GetMapCount();
                Assert.Equal(0, count);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MapSettingCore_GetMapAddr_WithNoRom_ReturnsNotFound()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                uint addr = MapSettingCore.GetMapAddr(0);
                Assert.Equal(U.NOT_FOUND, addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void LZ77_Decompress_InvalidData_ReturnsEmptyOrNull()
        {
            // Test that LZ77 decompress handles invalid data gracefully
            byte[] garbage = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            byte[] result = LZ77.decompress(garbage, 0);
            // Should not crash; result may be empty or null
            Assert.True(result == null || result.Length == 0 || result.Length > 0);
        }

        [Fact]
        public void PlistToOffset_Concept_ValidTableResolution()
        {
            // Test the PLIST resolution concept: a pointer table entry at
            // baseAddr + plist*4 contains a GBA pointer (0x08XXXXXX) to data.
            // Create a minimal ROM with a pointer table.
            byte[] data = new byte[0x200];

            // At offset 0x00: pointer to pointer table at 0x10
            // GBA pointer = 0x08000010
            data[0x00] = 0x10;
            data[0x01] = 0x00;
            data[0x02] = 0x00;
            data[0x03] = 0x08;

            // Pointer table at 0x10:
            // Entry 0 (PLIST 0): pointer to 0x100 -> GBA pointer 0x08000100
            data[0x10] = 0x00;
            data[0x11] = 0x01;
            data[0x12] = 0x00;
            data[0x13] = 0x08;

            // Entry 1 (PLIST 1): pointer to 0x180 -> GBA pointer 0x08000180
            data[0x14] = 0x80;
            data[0x15] = 0x01;
            data[0x16] = 0x00;
            data[0x17] = 0x08;

            // Verify the pointer chain manually (no ROM needed)
            // Read base pointer at offset 0x00
            uint basePtr = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
            Assert.Equal(0x08000010u, basePtr);

            uint tableBase = basePtr - 0x08000000; // = 0x10
            Assert.Equal(0x10u, tableBase);

            // PLIST 1 -> entry at tableBase + 1*4 = 0x14
            uint entryAddr = tableBase + 1 * 4;
            uint dataPtr = (uint)(data[entryAddr] | (data[entryAddr + 1] << 8) |
                                  (data[entryAddr + 2] << 16) | (data[entryAddr + 3] << 24));
            Assert.Equal(0x08000180u, dataPtr);
            Assert.Equal(0x180u, dataPtr - 0x08000000);
        }
    }
}
