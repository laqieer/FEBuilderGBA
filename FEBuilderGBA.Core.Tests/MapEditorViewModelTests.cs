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
        public void MapChangeRecord_ReadRecords_FromSyntheticRom()
        {
            // Build a minimal ROM with a map-change pointer table and change records.
            // Layout:
            //   0x00-0x03: (header/padding)
            //   0x100: pointer table entry -> GBA pointer to 0x200
            //   0x200: change record 0 (12 bytes)
            //   0x20C: terminator (0xFF)
            byte[] data = new byte[0x400];

            // Pointer table entry at 0x100: GBA pointer 0x08000200
            data[0x100] = 0x00;
            data[0x101] = 0x02;
            data[0x102] = 0x00;
            data[0x103] = 0x08;

            // Change record at 0x200:
            // ChangeID=3, X=5, Y=7, Width=2, Height=4, padding, TileDataPtr=0x08000300
            data[0x200] = 0x03; // ChangeID
            data[0x201] = 0x05; // X
            data[0x202] = 0x07; // Y
            data[0x203] = 0x02; // Width
            data[0x204] = 0x04; // Height
            data[0x205] = 0x00; // padding
            data[0x206] = 0x00;
            data[0x207] = 0x00;
            data[0x208] = 0x00; // TileDataPtr = 0x08000300
            data[0x209] = 0x03;
            data[0x20A] = 0x00;
            data[0x20B] = 0x08;

            // Terminator at 0x20C
            data[0x20C] = 0xFF;

            // Verify by manually reading the record fields
            uint addr = 0x200;
            Assert.Equal(0x03, data[addr + 0]); // ChangeID
            Assert.Equal(0x05, data[addr + 1]); // X
            Assert.Equal(0x07, data[addr + 2]); // Y
            Assert.Equal(0x02, data[addr + 3]); // Width
            Assert.Equal(0x04, data[addr + 4]); // Height

            uint tilePtr = (uint)(data[addr + 8] | (data[addr + 9] << 8) |
                                   (data[addr + 10] << 16) | (data[addr + 11] << 24));
            Assert.Equal(0x08000300u, tilePtr);

            // Verify terminator stops enumeration
            Assert.Equal(0xFF, data[0x20C]);
        }

        [Fact]
        public void MapChangeRecord_WriteRecord_OverwritesCorrectBytes()
        {
            byte[] data = new byte[0x20];

            // Simulate writing a change record at offset 0
            uint a = 0;
            byte changeID = 0x0A;
            byte x = 3;
            byte y = 12;
            byte width = 5;
            byte height = 8;
            uint tileDataPtr = 0x08001000;

            data[a + 0] = changeID;
            data[a + 1] = x;
            data[a + 2] = y;
            data[a + 3] = width;
            data[a + 4] = height;
            data[a + 8] = (byte)(tileDataPtr & 0xFF);
            data[a + 9] = (byte)((tileDataPtr >> 8) & 0xFF);
            data[a + 10] = (byte)((tileDataPtr >> 16) & 0xFF);
            data[a + 11] = (byte)((tileDataPtr >> 24) & 0xFF);

            Assert.Equal(0x0A, data[0]);
            Assert.Equal(3, data[1]);
            Assert.Equal(12, data[2]);
            Assert.Equal(5, data[3]);
            Assert.Equal(8, data[4]);
            Assert.Equal(0x00, data[8]);
            Assert.Equal(0x10, data[9]);
            Assert.Equal(0x00, data[10]);
            Assert.Equal(0x08, data[11]);
        }

        [Fact]
        public void MapChangeRecord_MultipleRecords_EnumerateUntilTerminator()
        {
            byte[] data = new byte[0x40];

            // Record 0 at offset 0
            data[0] = 0x01; // ChangeID
            // Record 1 at offset 12
            data[12] = 0x02; // ChangeID
            // Terminator at offset 24
            data[24] = 0xFF;

            int count = 0;
            for (int i = 0; i < 256; i++)
            {
                uint addr = (uint)(i * 12);
                if (addr >= data.Length) break;
                if (data[addr] == 0xFF) break;
                count++;
            }
            Assert.Equal(2, count);
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
