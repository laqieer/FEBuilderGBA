using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Provides core operations for expanding ROM data tables:
    /// finding free space, reading table metadata, and copying a table
    /// to a new location with an appended blank entry.
    /// </summary>
    public static class DataExpansionCore
    {
        /// <summary>
        /// Result of a table expansion operation.
        /// </summary>
        public sealed class ExpandResult
        {
            /// <summary>Whether the operation succeeded.</summary>
            public bool Success { get; set; }

            /// <summary>Human-readable error message when Success is false.</summary>
            public string Error { get; set; }

            /// <summary>ROM offset of the new table base.</summary>
            public uint NewBaseAddress { get; set; }

            /// <summary>Entry count after expansion (old count + 1).</summary>
            public uint NewCount { get; set; }
        }

        /// <summary>
        /// Information about an existing ROM data table.
        /// </summary>
        public sealed class TableInfo
        {
            /// <summary>ROM offset the pointer at <c>pointerAddr</c> resolves to.</summary>
            public uint BaseAddress { get; set; }

            /// <summary>
            /// Estimated entry count, computed by scanning forward from
            /// <c>BaseAddress</c> until an all-0x00 entry or end of ROM.
            /// </summary>
            public uint EstimatedCount { get; set; }
        }

        /// <summary>
        /// Default start address for free-space scanning (past typical ROM header/data area).
        /// </summary>
        public const uint DefaultSearchStart = 0x100000;

        /// <summary>
        /// Scan the ROM for a contiguous 4-byte-aligned region of 0xFF bytes
        /// that is at least <paramref name="requiredSize"/> bytes long.
        /// </summary>
        /// <param name="rom">ROM to scan.</param>
        /// <param name="requiredSize">Minimum number of free bytes needed.</param>
        /// <param name="searchStart">Address to begin scanning (default 0x100000).</param>
        /// <returns>
        /// The 4-byte-aligned start offset of the free region,
        /// or <see cref="U.NOT_FOUND"/> if no suitable region exists.
        /// </returns>
        public static uint FindFreeSpace(ROM rom, uint requiredSize, uint searchStart = DefaultSearchStart)
        {
            if (rom == null || rom.Data == null)
                return U.NOT_FOUND;
            if (requiredSize == 0)
                return U.NOT_FOUND;

            byte[] data = rom.Data;
            uint length = (uint)data.Length;

            if (requiredSize > length)
                return U.NOT_FOUND;

            // Ensure 4-byte alignment
            searchStart = U.Padding4(searchStart);

            uint endScan = length - requiredSize;
            for (uint addr = searchStart; addr <= endScan; addr += 4)
            {
                if (data[addr] != 0xFF)
                    continue;

                // Found a candidate start — verify the full run
                uint runLen = 0;
                uint scanEnd = Math.Min(addr + requiredSize, length);
                bool good = true;
                for (uint j = addr; j < scanEnd; j++)
                {
                    if (data[j] != 0xFF)
                    {
                        // Skip past this non-free byte (aligned)
                        addr = U.Padding4(j);
                        // The outer loop will add 4, so subtract 4 to compensate
                        if (addr >= 4) addr -= 4;
                        good = false;
                        break;
                    }
                    runLen++;
                }

                if (good && runLen >= requiredSize)
                    return addr;
            }

            return U.NOT_FOUND;
        }

        /// <summary>
        /// Read information about a pointer-based ROM table.
        /// </summary>
        /// <param name="rom">ROM to inspect.</param>
        /// <param name="pointerAddr">Address in ROM that holds a GBA pointer to the table.</param>
        /// <param name="entrySize">Size of each table entry in bytes.</param>
        /// <returns>A <see cref="TableInfo"/> or null if the pointer is invalid.</returns>
        public static TableInfo GetTableInfo(ROM rom, uint pointerAddr, uint entrySize)
        {
            if (rom == null || rom.Data == null)
                return null;
            if (entrySize == 0)
                return null;
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                return null;

            uint baseAddr = rom.p32(pointerAddr);
            if (baseAddr == 0 || baseAddr >= (uint)rom.Data.Length)
                return null;

            uint count = EstimateEntryCount(rom, baseAddr, entrySize);

            return new TableInfo
            {
                BaseAddress = baseAddr,
                EstimatedCount = count,
            };
        }

        /// <summary>
        /// Expand a pointer-based ROM data table by one entry.
        /// Copies the existing table to free space, appends a zero-initialized entry,
        /// and updates the pointer.
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="pointerAddr">Address of the GBA pointer that references the table base.</param>
        /// <param name="entrySize">Fixed size of each table entry in bytes.</param>
        /// <param name="currentCount">Number of entries currently in the table.</param>
        /// <returns>An <see cref="ExpandResult"/> describing the outcome.</returns>
        public static ExpandResult ExpandTable(ROM rom, uint pointerAddr, uint entrySize, uint currentCount)
        {
            if (rom == null || rom.Data == null)
                return Fail("ROM is null.");
            if (entrySize == 0)
                return Fail("Entry size must be greater than zero.");
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                return Fail("Pointer address is out of ROM bounds.");

            uint oldBase = rom.p32(pointerAddr);
            if (oldBase == 0 || oldBase >= (uint)rom.Data.Length)
                return Fail("Table pointer is invalid (null or out of bounds).");

            uint oldTableSize = currentCount * entrySize;
            uint newTableSize = (currentCount + 1) * entrySize;

            // Verify old table fits in ROM
            if (oldBase + oldTableSize > (uint)rom.Data.Length)
                return Fail("Current table extends beyond ROM bounds.");

            // Find free space for the new (larger) table
            uint newBase = FindFreeSpace(rom, newTableSize);
            if (newBase == U.NOT_FOUND)
            {
                // Try expanding the ROM to make room
                uint newRomSize = U.Padding4((uint)rom.Data.Length + newTableSize);
                if (newRomSize > 0x02000000) // 32 MB max
                    return Fail("Cannot find free space and ROM is at maximum size.");

                bool resized = rom.write_resize_data(newRomSize);
                if (!resized)
                    return Fail("Failed to resize ROM.");

                // The newly allocated area will be 0x00, but we need 0xFF first to find it,
                // or just place the table at the old end.
                newBase = U.Padding4((uint)(rom.Data.Length - newTableSize));
                // Actually, after resize the new area is 0x00. We can just write directly
                // to the end of the old ROM size.
                newBase = U.Padding4((uint)rom.Data.Length - newTableSize);
            }

            // Copy old table data to new location
            Array.Copy(rom.Data, oldBase, rom.Data, newBase, oldTableSize);

            // Zero-fill the new entry
            uint newEntryAddr = newBase + oldTableSize;
            for (uint i = 0; i < entrySize; i++)
            {
                rom.Data[newEntryAddr + i] = 0x00;
            }

            // Clear old table location with 0xFF (free it)
            for (uint i = 0; i < oldTableSize; i++)
            {
                rom.Data[oldBase + i] = 0xFF;
            }

            // Update the pointer to point to the new location (GBA pointer = offset + 0x08000000)
            rom.write_p32(pointerAddr, newBase);

            return new ExpandResult
            {
                Success = true,
                NewBaseAddress = newBase,
                NewCount = currentCount + 1,
            };
        }

        /// <summary>
        /// Estimate the number of entries in a table by scanning forward
        /// until an all-zero entry is found or the ROM ends.
        /// </summary>
        internal static uint EstimateEntryCount(ROM rom, uint baseAddr, uint entrySize)
        {
            byte[] data = rom.Data;
            uint romLen = (uint)data.Length;
            uint count = 0;
            uint maxEntries = (romLen - baseAddr) / entrySize;

            for (uint i = 0; i < maxEntries; i++)
            {
                uint entryStart = baseAddr + i * entrySize;
                if (entryStart + entrySize > romLen)
                    break;

                // Check if the entire entry is zeroed (terminator)
                bool allZero = true;
                for (uint b = 0; b < entrySize; b++)
                {
                    if (data[entryStart + b] != 0x00)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (allZero)
                    break;

                count++;
            }

            return count;
        }

        private static ExpandResult Fail(string error)
        {
            return new ExpandResult { Success = false, Error = error };
        }
    }
}
