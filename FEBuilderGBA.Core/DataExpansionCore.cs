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

            // Copy old table data to new location.
            // Route through rom.write_range so the ambient-undo scope
            // captures the pre-copy bytes at newBase (#419 — undo
            // completeness fix flagged by Copilot CLI plan-review round 3).
            byte[] copyBytes = rom.getBinaryData(oldBase, oldTableSize);
            rom.write_range(newBase, copyBytes);

            // Zero-fill the new entry — route through rom.write_fill
            // so the ambient-undo scope captures the bytes that were
            // there before the fill.
            uint newEntryAddr = newBase + oldTableSize;
            rom.write_fill(newEntryAddr, entrySize, 0x00);

            // Clear old table location with 0xFF (free it) — same.
            rom.write_fill(oldBase, oldTableSize, 0xFF);

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
        /// Expand a pointer-based ROM data table to a specific row count
        /// <paramref name="newCount"/>. Mirrors WinForms
        /// <c>InputFormRef.ExpandsArea(ExpandsFillOption.NO, ...)</c> byte-for-byte:
        /// <list type="bullet">
        ///   <item>Allocates <paramref name="newCount"/> * <paramref name="entrySize"/> bytes
        ///         at fresh free space (or resizes the ROM).</item>
        ///   <item>Copies the existing <paramref name="currentCount"/> rows verbatim — the
        ///         caller is responsible for supplying the editor's row count
        ///         (e.g. from <c>InputFormRef.DataCount</c> or the Avalonia
        ///         <c>ViewModel.ReadCount</c>). The helper does NOT call
        ///         <see cref="EstimateEntryCount"/> internally because the
        ///         action-anime predicate <c>U.isSafetyPointerOrNull</c>
        ///         counts row 0 as valid even when it is all zero.</item>
        ///   <item>Zero-fills the new (<paramref name="newCount"/> -
        ///         <paramref name="currentCount"/>) rows.</item>
        ///   <item>Writes a <c>0xFFFFFFFF</c> terminator at
        ///         <c>newBase + newCount * entrySize</c> so pointer-first
        ///         scan predicates (<c>!U.isSafetyPointerOrNull(D0)</c>) stop
        ///         at exactly <paramref name="newCount"/> rows even when the
        ///         helper resizes the ROM into a freshly-zeroed region. The
        ///         allocation request size is therefore <c>(newCount * entrySize) + 4</c>.</item>
        ///   <item>Wipes the OLD region with <c>0x00</c> (matches WF
        ///         <c>InputFormRef.ExpandsArea</c> at line 10787; intentionally
        ///         differs from <see cref="ExpandTable"/> which uses
        ///         <c>0xFF</c> recycling).</item>
        ///   <item>Updates the GBA pointer at <paramref name="pointerAddr"/>
        ///         to point at the new base.</item>
        ///   <item>Repoints <c>CoreState.CommentCache</c> and
        ///         <c>CoreState.LintCache</c> entries from
        ///         <c>[oldBase, oldBase + currentCount * entrySize)</c> to the
        ///         corresponding new addresses so per-row comments/lint
        ///         metadata follow the moved table.</item>
        /// </list>
        ///
        /// <para><b>Stop-row guarantee scope:</b> the <c>0xFFFFFFFF</c>
        /// terminator stops <i>pointer-first row scans</i> — predicates that
        /// reject <c>!U.isSafetyPointerOrNull</c> values (e.g.
        /// <c>ImageMapActionAnimationViewModel.LoadList</c> and WF
        /// <c>InputFormRef</c> row-validity callbacks). It does NOT change
        /// <see cref="EstimateEntryCount"/> behavior (which stops at the first
        /// all-zero row, not at <c>0xFFFFFFFF</c>). Callers that need a row
        /// count from a freshly expanded table should use
        /// <c><paramref name="newCount"/></c> directly or rescan with the
        /// editor's own predicate, not call <see cref="EstimateEntryCount"/>.</para>
        ///
        /// <para><b>Cache repoint is forward-only:</b> ROM-level undo restores
        /// byte ranges only; the cache-key repoint is NOT reversed on rollback.
        /// This matches WF's <c>MoveToFreeSapceForm.RepointEtcData</c> behavior
        /// (also forward-only). KnownGap — no follow-up issue filed.</para>
        ///
        /// <para><b>Out-of-scope KnownGaps</b> (no follow-up issues — see #501):
        /// LDR-pointer rescan (WF <c>MoveToFreeSapceForm.SearchPointer</c>)
        /// and freed-space reuse (WF <c>SearchFreeSpace</c> match-reuse
        /// branch).</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="pointerAddr">Address of the GBA pointer that references the table base.</param>
        /// <param name="entrySize">Fixed size of each table entry in bytes.</param>
        /// <param name="currentCount">Number of rows currently in the table
        /// (use the editor's row count — <i>not</i> <see cref="EstimateEntryCount"/>).</param>
        /// <param name="newCount">Target row count. Must be &gt;= <paramref name="currentCount"/>.</param>
        public static ExpandResult ExpandTableTo(ROM rom, uint pointerAddr, uint entrySize, uint currentCount, uint newCount)
        {
            if (rom == null || rom.Data == null)
                return Fail("ROM is null.");
            if (entrySize == 0)
                return Fail("Entry size must be greater than zero.");
            if (newCount < currentCount)
                return Fail("newCount must be greater than or equal to currentCount.");
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                return Fail("Pointer address is out of ROM bounds.");

            uint oldBase = rom.p32(pointerAddr);
            if (oldBase == 0 || oldBase >= (uint)rom.Data.Length)
                return Fail("Table pointer is invalid (null or out of bounds).");

            uint oldTableSize = currentCount * entrySize;
            // Overflow guard for newCount * entrySize on 32-bit math.
            if (entrySize != 0 && newCount > (uint.MaxValue - 4) / entrySize)
                return Fail("newCount * entrySize overflows 32-bit address space.");
            uint newTableSize = newCount * entrySize;

            // Verify old table fits in ROM.
            if (oldBase + oldTableSize > (uint)rom.Data.Length)
                return Fail("Current table extends beyond ROM bounds.");

            // No-op fast path — newCount == currentCount.
            if (newCount == currentCount)
            {
                return new ExpandResult
                {
                    Success = true,
                    NewBaseAddress = oldBase,
                    NewCount = currentCount,
                };
            }

            // Allocate newCount * entrySize + 4 bytes (the +4 is the
            // explicit 0xFFFFFFFF terminator dword).
            uint allocSize = newTableSize + 4;
            uint newBase = FindFreeSpace(rom, allocSize);
            if (newBase == U.NOT_FOUND)
            {
                // Try expanding the ROM to make room.
                uint requiredEnd = U.Padding4((uint)rom.Data.Length + allocSize);
                if (requiredEnd > 0x02000000) // 32 MB max
                    return Fail("Cannot find free space and ROM is at maximum size.");

                bool resized = rom.write_resize_data(requiredEnd);
                if (!resized)
                    return Fail("Failed to resize ROM.");

                // Place the table at the previous end (rounded down to 4-byte
                // alignment) so the new region is fully inside the
                // newly-resized area.
                newBase = U.Padding4((uint)rom.Data.Length - allocSize);
            }

            // Copy old rows verbatim. Route through rom.write_range so the
            // ambient-undo scope captures the pre-copy bytes at newBase.
            if (oldTableSize > 0)
            {
                byte[] copyBytes = rom.getBinaryData(oldBase, oldTableSize);
                rom.write_range(newBase, copyBytes);
            }

            // Zero-fill the new rows. Route through rom.write_fill so the
            // ambient-undo scope captures the bytes that were there before.
            uint newRowsStart = newBase + oldTableSize;
            uint newRowsSize = newTableSize - oldTableSize;
            if (newRowsSize > 0)
            {
                rom.write_fill(newRowsStart, newRowsSize, 0x00);
            }

            // Write the explicit 0xFFFFFFFF terminator at
            // newBase + newCount * entrySize. Required so pointer-first row
            // scans stop at exactly newCount even when surrounding bytes are
            // 0x00 (e.g. after a ROM resize into a zeroed region).
            uint termAddr = newBase + newTableSize;
            rom.write_u32(termAddr, 0xFFFFFFFF);

            // Wipe the OLD region with 0x00 — matches WF
            // InputFormRef.ExpandsArea line 10787. NOTE: ExpandTable (the +1
            // wrapper) uses 0xFF for recycling — intentional divergence,
            // documented in both helpers' XML docs.
            if (oldTableSize > 0)
            {
                rom.write_fill(oldBase, oldTableSize, 0x00);
            }

            // Update the pointer to point to the new location.
            rom.write_p32(pointerAddr, newBase);

            // Repoint comment/lint cache entries. Forward-only (matches WF
            // MoveToFreeSapceForm.RepointEtcData behavior). KnownGap — ROM
            // undo does NOT reverse this; the caches stay pointing at the
            // new addresses after rollback.
            if (oldTableSize > 0)
            {
                CoreState.CommentCache?.RepointEtcData(oldBase, oldTableSize, newBase);
                CoreState.LintCache?.RepointEtcData(oldBase, oldTableSize, newBase);
            }

            return new ExpandResult
            {
                Success = true,
                NewBaseAddress = newBase,
                NewCount = newCount,
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
