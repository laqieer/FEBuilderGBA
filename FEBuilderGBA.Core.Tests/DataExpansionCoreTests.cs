using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DataExpansionCoreTests
    {
        /// <summary>Helper: build a minimal ROM with LoadLow using ROMFE0 ("NAZO").</summary>
        private static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            // Fill with 0x00 by default; tests will set up 0xFF free regions.
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        /// <summary>Write a GBA pointer (offset + 0x08000000) at the given ROM address.</summary>
        private static void WritePointer(ROM rom, uint addr, uint offset)
        {
            uint gbaPtr = offset + 0x08000000;
            rom.Data[addr + 0] = (byte)(gbaPtr & 0xFF);
            rom.Data[addr + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((gbaPtr >> 24) & 0xFF);
        }

        // ────────────────────────────────────────────────
        // FindFreeSpace
        // ────────────────────────────────────────────────

        [Fact]
        public void FindFreeSpace_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(null, 16));
        }

        [Fact]
        public void FindFreeSpace_ZeroSize_ReturnsNotFound()
        {
            var rom = MakeRom(256);
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 0));
        }

        [Fact]
        public void FindFreeSpace_SizeExceedsRom_ReturnsNotFound()
        {
            var rom = MakeRom(256);
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 0x1000000));
        }

        [Fact]
        public void FindFreeSpace_FindsFF_Region()
        {
            var rom = MakeRom(0x200000);
            // Place a 64-byte 0xFF region at 0x100100 (4-byte aligned)
            for (int i = 0; i < 64; i++)
                rom.Data[0x100100 + i] = 0xFF;

            uint result = DataExpansionCore.FindFreeSpace(rom, 32, 0x100000);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.True(result >= 0x100100 && result <= 0x100100 + 32);
            // Verify 4-byte alignment
            Assert.Equal(0u, result % 4);
        }

        [Fact]
        public void FindFreeSpace_SkipsNonFF_Bytes()
        {
            var rom = MakeRom(0x200000);
            // Put some non-FF at the start of the search region
            rom.Data[0x100000] = 0xFF;
            rom.Data[0x100001] = 0xFF;
            rom.Data[0x100002] = 0x42; // break

            // Put real free space later
            for (int i = 0; i < 32; i++)
                rom.Data[0x100100 + i] = 0xFF;

            uint result = DataExpansionCore.FindFreeSpace(rom, 16, 0x100000);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.True(result >= 0x100100);
        }

        [Fact]
        public void FindFreeSpace_NoFreeRegion_ReturnsNotFound()
        {
            // ROM filled with 0x00 (not 0xFF) and we search for FF
            var rom = MakeRom(512);
            // Fill everything with non-FF/non-00 to be safe
            for (int i = 0; i < rom.Data.Length; i++)
                rom.Data[i] = 0x42;

            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 16, 0));
        }

        // ────────────────────────────────────────────────
        // GetTableInfo
        // ────────────────────────────────────────────────

        [Fact]
        public void GetTableInfo_NullRom_ReturnsNull()
        {
            Assert.Null(DataExpansionCore.GetTableInfo(null, 0, 4));
        }

        [Fact]
        public void GetTableInfo_ZeroEntrySize_ReturnsNull()
        {
            var rom = MakeRom(256);
            Assert.Null(DataExpansionCore.GetTableInfo(rom, 0, 0));
        }

        [Fact]
        public void GetTableInfo_InvalidPointer_ReturnsNull()
        {
            var rom = MakeRom(256);
            // Pointer at 0x00 is all zeros → resolved offset is 0 via toOffset → invalid
            Assert.Null(DataExpansionCore.GetTableInfo(rom, 0, 4));
        }

        [Fact]
        public void GetTableInfo_ValidTable_ReturnsCorrectInfo()
        {
            var rom = MakeRom(0x1000);
            uint tableBase = 0x100;
            uint entrySize = 8;
            uint pointerAddr = 0x10;

            // Write pointer at pointerAddr → tableBase
            WritePointer(rom, pointerAddr, tableBase);

            // Write 3 non-zero entries, then a zero entry (terminator)
            for (int entry = 0; entry < 3; entry++)
            {
                uint addr = (uint)(tableBase + entry * entrySize);
                rom.Data[addr] = (byte)(entry + 1); // non-zero first byte
            }
            // Entry 3 is all zeros (terminator) — already 0x00 by default

            var info = DataExpansionCore.GetTableInfo(rom, pointerAddr, entrySize);
            Assert.NotNull(info);
            Assert.Equal(tableBase, info.BaseAddress);
            Assert.Equal(3u, info.EstimatedCount);
        }

        [Fact]
        public void GetTableInfo_AllZeroFirstEntry_CountIsZero()
        {
            var rom = MakeRom(0x1000);
            uint tableBase = 0x100;
            uint pointerAddr = 0x10;

            WritePointer(rom, pointerAddr, tableBase);
            // First entry is all zeros → count = 0

            var info = DataExpansionCore.GetTableInfo(rom, pointerAddr, 8);
            Assert.NotNull(info);
            Assert.Equal(0u, info.EstimatedCount);
        }

        // ────────────────────────────────────────────────
        // EstimateEntryCount
        // ────────────────────────────────────────────────

        [Fact]
        public void EstimateEntryCount_StopsAtZeroEntry()
        {
            var rom = MakeRom(256);
            uint baseAddr = 0x10;
            uint entrySize = 4;

            // 2 non-zero entries
            rom.Data[0x10] = 0x01;
            rom.Data[0x14] = 0x02;
            // 0x18 is all zeros → terminator

            uint count = DataExpansionCore.EstimateEntryCount(rom, baseAddr, entrySize);
            Assert.Equal(2u, count);
        }

        [Fact]
        public void EstimateEntryCount_StopsAtEndOfRom()
        {
            var rom = MakeRom(32);
            // Fill everything with non-zero
            for (int i = 0; i < rom.Data.Length; i++)
                rom.Data[i] = 0x42;

            uint count = DataExpansionCore.EstimateEntryCount(rom, 0, 4);
            Assert.Equal((uint)(rom.Data.Length / 4), count);
        }

        // ────────────────────────────────────────────────
        // ExpandTable
        // ────────────────────────────────────────────────

        [Fact]
        public void ExpandTable_NullRom_Fails()
        {
            var result = DataExpansionCore.ExpandTable(null, 0, 8, 1);
            Assert.False(result.Success);
            Assert.Contains("null", result.Error);
        }

        [Fact]
        public void ExpandTable_ZeroEntrySize_Fails()
        {
            var rom = MakeRom(256);
            var result = DataExpansionCore.ExpandTable(rom, 0, 0, 1);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_InvalidPointer_Fails()
        {
            var rom = MakeRom(256);
            var result = DataExpansionCore.ExpandTable(rom, 0, 8, 1);
            Assert.False(result.Success);
            Assert.Contains("invalid", result.Error.ToLower());
        }

        [Fact]
        public void ExpandTable_CopiesDataAndAddsEntry()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint entryCount = 3;

            // Set up the pointer
            WritePointer(rom, pointerAddr, tableBase);

            // Write 3 entries with known data
            for (uint e = 0; e < entryCount; e++)
            {
                for (uint b = 0; b < entrySize; b++)
                {
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);
                }
            }

            // Create free space for the expanded table (need 4 * 8 = 32 bytes of 0xFF)
            uint freeAddr = 0x100100;
            for (int i = 0; i < 64; i++)
                rom.Data[freeAddr + (uint)i] = 0xFF;

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, entryCount);

            Assert.True(result.Success, result.Error);
            Assert.Equal(entryCount + 1, result.NewCount);
            Assert.NotEqual(tableBase, result.NewBaseAddress);

            // Verify the pointer was updated
            uint newBase = rom.p32(pointerAddr);
            Assert.Equal(result.NewBaseAddress, newBase);

            // Verify old data was copied correctly
            for (uint e = 0; e < entryCount; e++)
            {
                for (uint b = 0; b < entrySize; b++)
                {
                    Assert.Equal((byte)(0x10 + e), rom.Data[newBase + e * entrySize + b]);
                }
            }

            // Verify new entry is zero-filled
            uint newEntryStart = newBase + entryCount * entrySize;
            for (uint b = 0; b < entrySize; b++)
            {
                Assert.Equal(0x00, rom.Data[newEntryStart + b]);
            }

            // Verify old table location was freed (0xFF)
            for (uint i = 0; i < entryCount * entrySize; i++)
            {
                Assert.Equal(0xFF, rom.Data[tableBase + i]);
            }
        }

        [Fact]
        public void ExpandTable_PointerOutOfBounds_Fails()
        {
            var rom = MakeRom(256);
            // Pointer address beyond ROM
            var result = DataExpansionCore.ExpandTable(rom, 0x1000, 8, 1);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_TableExceedsRom_Fails()
        {
            var rom = MakeRom(0x1000);
            uint pointerAddr = 0x10;
            // Point to near the end of ROM so table overflows
            WritePointer(rom, pointerAddr, 0xFF0);

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, 32, 5);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_PreservesOriginalEntryValues()
        {
            // Verify that specific byte patterns survive the copy
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 4;
            uint entryCount = 2;

            WritePointer(rom, pointerAddr, tableBase);

            // Entry 0: [0xAA, 0xBB, 0xCC, 0xDD]
            rom.Data[tableBase + 0] = 0xAA;
            rom.Data[tableBase + 1] = 0xBB;
            rom.Data[tableBase + 2] = 0xCC;
            rom.Data[tableBase + 3] = 0xDD;
            // Entry 1: [0x11, 0x22, 0x33, 0x44]
            rom.Data[tableBase + 4] = 0x11;
            rom.Data[tableBase + 5] = 0x22;
            rom.Data[tableBase + 6] = 0x33;
            rom.Data[tableBase + 7] = 0x44;

            // Free space
            for (int i = 0; i < 64; i++)
                rom.Data[0x100200 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, entryCount);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            Assert.Equal(0xAA, rom.Data[nb + 0]);
            Assert.Equal(0xBB, rom.Data[nb + 1]);
            Assert.Equal(0xCC, rom.Data[nb + 2]);
            Assert.Equal(0xDD, rom.Data[nb + 3]);
            Assert.Equal(0x11, rom.Data[nb + 4]);
            Assert.Equal(0x22, rom.Data[nb + 5]);
            Assert.Equal(0x33, rom.Data[nb + 6]);
            Assert.Equal(0x44, rom.Data[nb + 7]);
        }
    }
}
