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
        ///   <item>Writes a terminator at <c>newBase + newCount * entrySize</c>
        ///         so row scans stop at exactly <paramref name="newCount"/> rows
        ///         even when the helper resizes the ROM into a freshly-zeroed
        ///         region. By default (<paramref name="fullZeroTerminatorRow"/>
        ///         <c>== false</c>, the #501 caller) this is a single
        ///         <c>0xFFFFFFFF</c> dword for pointer-first scan predicates
        ///         (<c>!U.isSafetyPointerOrNull(D0)</c>) and the allocation
        ///         request size is <c>(newCount * entrySize) + 4</c>. When
        ///         <paramref name="fullZeroTerminatorRow"/> <c>== true</c> (the
        ///         #1078 Unit-Palette caller) the terminator is a FULL
        ///         <paramref name="entrySize"/>-byte all-zero row (NO
        ///         <c>0xFFFFFFFF</c> dword) and the allocation request size is
        ///         <c>(newCount * entrySize) + entrySize</c>.</item>
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
        /// <para><b>Single-slot repoint by design:</b> this helper updates ONLY
        /// the one pointer at <c>pointerAddr</c>. That is correct for an
        /// unshared table. When a table base may be referenced from multiple
        /// raw pointers AND/OR ARM LDR literal-pool loads, use the opt-in
        /// <see cref="RepointAllReferences(ROM, uint, uint, Undo.UndoData)"/>
        /// helper, which rescans the whole ROM (raw +
        /// <c>U.GrepPointerAllOnLDR</c>) and repoints every unique slot — this
        /// closes the former LDR-rescan gap (#781).</para>
        ///
        /// <para><b>Out-of-scope KnownGap</b> (no follow-up issue — see #501):
        /// freed-space reuse (WF <c>SearchFreeSpace</c> match-reuse branch).</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="pointerAddr">Address of the GBA pointer that references the table base.</param>
        /// <param name="entrySize">Fixed size of each table entry in bytes.</param>
        /// <param name="currentCount">Number of rows currently in the table
        /// (use the editor's row count — <i>not</i> <see cref="EstimateEntryCount"/>).</param>
        /// <param name="newCount">Target row count. Must be &gt;= <paramref name="currentCount"/>.</param>
        /// <param name="fullZeroTerminatorRow">Terminator policy for the row
        /// immediately past <paramref name="newCount"/>. <c>false</c> (default —
        /// byte-identical to the #501 caller) reserves and writes a single
        /// <c>0xFFFFFFFF</c> terminator dword (4 bytes). <c>true</c> reserves and
        /// writes a FULL <paramref name="entrySize"/>-byte all-zero terminator
        /// row (NO <c>0xFFFFFFFF</c> dword) — required when the row-scan
        /// predicate treats a <c>0xFFFFFFFF</c>-first/zero-tail row as a phantom
        /// valid entry (e.g. the Unit Palette editor's
        /// <c>P12==0 &amp;&amp; name!=0</c> acceptance, #1078). The allocation
        /// request size is therefore <c>(newCount * entrySize) +
        /// (fullZeroTerminatorRow ? entrySize : 4)</c>.</param>
        public static ExpandResult ExpandTableTo(ROM rom, uint pointerAddr, uint entrySize, uint currentCount, uint newCount, bool fullZeroTerminatorRow = false)
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

            // Bytes reserved past the last row for the terminator. The default
            // (#501) policy is a single 0xFFFFFFFF dword (4 bytes); the
            // full-zero-row policy (#1078) reserves a whole entrySize-byte row.
            uint terminatorReserve = fullZeroTerminatorRow ? entrySize : 4;

            // Overflow guards for both `currentCount * entrySize` and
            // `newCount * entrySize` — without these, a malicious / corrupt
            // `currentCount` could wrap to a tiny value that passes the
            // subsequent `oldBase + oldTableSize` ROM-bounds check, then the
            // ROM-byte writes use the unwrapped (huge) size and corrupt the
            // file. (Copilot bot review on PR #635 inline #1.)
            if (entrySize != 0 && currentCount > uint.MaxValue / entrySize)
                return Fail("currentCount * entrySize overflows 32-bit address space.");
            uint oldTableSize = currentCount * entrySize;
            // Overflow guard for newCount * entrySize (newCount >= currentCount,
            // so currentCount can't bypass this on a single check). Reserve the
            // terminator bytes so the `newTableSize + terminatorReserve` alloc
            // below also can't wrap.
            if (entrySize != 0 && newCount > (uint.MaxValue - terminatorReserve) / entrySize)
                return Fail("newCount * entrySize overflows 32-bit address space.");
            uint newTableSize = newCount * entrySize;

            // Verify old table fits in ROM. Use a wrap-safe `size > Length - addr`
            // form (oldBase < Length already, so Length - oldBase can't wrap).
            if (oldTableSize > (uint)rom.Data.Length - oldBase)
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

            // Allocate newCount * entrySize + terminatorReserve bytes (the
            // reserve is either the 4-byte 0xFFFFFFFF terminator dword, or a
            // full entrySize-byte all-zero terminator row — see
            // fullZeroTerminatorRow).
            uint allocSize = newTableSize + terminatorReserve;
            uint newBase = FindFreeSpace(rom, allocSize);
            if (newBase == U.NOT_FOUND)
            {
                // Try expanding the ROM to make room.
                // Overflow guard: `rom.Data.Length + allocSize` can wrap on a
                // very large ROM + allocSize combo and silently bypass the
                // 32 MB cap. Use a checked add. (Copilot bot review on PR #635
                // inline #2.)
                uint romLen = (uint)rom.Data.Length;
                if (allocSize > uint.MaxValue - romLen)
                    return Fail("ROM resize required size overflows 32-bit address space.");
                uint requiredEndUnpadded = romLen + allocSize;
                // Padding4 rounds up — guard the +3 overflow too.
                if (requiredEndUnpadded > uint.MaxValue - 3)
                    return Fail("ROM resize required size overflows 32-bit address space.");
                uint requiredEnd = U.Padding4(requiredEndUnpadded);
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

            // Write the explicit terminator at newBase + newCount * entrySize.
            // Required so row scans stop at exactly newCount even when
            // surrounding bytes are 0x00 (e.g. after a ROM resize into a zeroed
            // region). Two policies (see fullZeroTerminatorRow):
            //   false (#501): a single 0xFFFFFFFF dword.
            //   true  (#1078): a FULL entrySize-byte all-zero row, so a scan
            //                  predicate that accepts a 0xFFFFFFFF-first row
            //                  (or reads past a 4-byte terminator into the
            //                  next row's fields) still stops here.
            uint termAddr = newBase + newTableSize;
            if (fullZeroTerminatorRow)
                rom.write_fill(termAddr, entrySize, 0x00);
            else
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
        /// Repoint EVERY reference to <paramref name="oldBase"/> &#8594;
        /// <paramref name="newBase"/>: raw 32-bit pointers
        /// (<see cref="U.GrepPointerAll(byte[], uint, uint, uint)"/>) AND ARM
        /// Thumb LDR literal-pool loads
        /// (<see cref="U.GrepPointerAllOnLDR(byte[], uint)"/>). Mirrors WF
        /// <c>MoveToFreeSapceForm.SearchPointer</c>'s repoint (minus the
        /// WinForms UI dialogs, the event-aware
        /// <c>GrepPointerAllOnEvent</c> pass, and the <c>IsFixedASM</c> ASM-code
        /// guard — those depend on <c>InputFormRef</c> / the WinForms ASM cache
        /// and are out of scope). Returns the count of slots actually repointed.
        ///
        /// <para>Both scanners already normalise <paramref name="oldBase"/> to a
        /// GBA pointer internally (<c>U.toPointer</c> is idempotent), so the
        /// helper accepts either a pointer (<c>0x08xxxxxx</c>) or a raw offset
        /// and is robust to either form. The combined hit list is de-duplicated
        /// with a <see cref="System.Collections.Generic.HashSet{T}"/> because a
        /// valid LDR literal-pool slot is ALSO a raw pointer hit — each unique
        /// slot is written exactly once.</para>
        ///
        /// <para><b>Writes only literal/pointer SLOTS</b> via
        /// <c>rom.write_p32(slot, newBase)</c> — never an instruction. The write
        /// respects the ambient undo scope opened by the caller
        /// (<c>ROM.BeginUndoScope</c>); pass a non-null <paramref name="undo"/>
        /// to have the helper open one for the duration of the call.</para>
        ///
        /// <para><b>Per-slot danger-zone gate (#782 review):</b> each candidate
        /// slot is itself gated with
        /// <see cref="U.isSafetyOffset(uint, ROM)"/> (+ an explicit
        /// <c>slot + 4 &lt;= Length</c> bounds check) BEFORE writing. A
        /// false-positive raw/LDR hit whose slot lands in the 0x0–0x200 header
        /// danger zone (or out of ROM) is skipped — never written — so the
        /// cartridge header can never be corrupted. The returned count reflects
        /// only the slots actually repointed (skipped hits are excluded).</para>
        ///
        /// <para><b>Refuses safely:</b> if <paramref name="oldBase"/> resolves to
        /// the 0x0–0x200 danger zone or is otherwise not a safe ROM offset
        /// (<see cref="U.isSafetyOffset(uint, ROM)"/>), returns 0 without
        /// touching the ROM — mirroring the scanners' own <c>start = 0x100</c>
        /// floor and WF <c>SearchPointer</c>'s <c>isSafetyOffset</c> gate. A
        /// no-reference ROM also returns 0 (no throw).</para>
        ///
        /// <para>This is a NEW opt-in helper. <see cref="ExpandTable"/> /
        /// <see cref="ExpandTableTo"/> are intentionally left alone — their
        /// single-slot repoint is correct for unshared tables.</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="oldBase">Old table base — pointer (<c>0x08xxxxxx</c>) or offset.</param>
        /// <param name="newBase">New table base — pointer (<c>0x08xxxxxx</c>) or offset.</param>
        /// <param name="undo">Optional undo buffer. When non-null the helper
        /// opens a <c>ROM.BeginUndoScope(undo)</c> for the duration so every
        /// slot write is recorded; when null the helper relies on whatever
        /// ambient undo scope the caller already opened.</param>
        /// <returns>The number of slots actually repointed — excludes any
        /// danger-zone / out-of-ROM hits that were skipped (0 if none / refused).</returns>
        public static int RepointAllReferences(ROM rom, uint oldBase, uint newBase, Undo.UndoData? undo)
        {
            if (rom == null || rom.Data == null)
                return 0;

            // Refuse safely on the 0x0–0x200 danger zone / out-of-ROM offsets.
            // Mirrors WF SearchPointer's isSafetyOffset(toOffset(moveAddress))
            // gate and the scanners' own start=0x100 floor.
            uint oldOffset = U.toOffset(oldBase);
            if (!U.isSafetyOffset(oldOffset, rom))
                return 0;

            // Pass the pointer form to both scanners. They each call
            // U.toPointer(need) internally (idempotent), so either form works;
            // we normalise here for clarity and to match WF SearchPointer which
            // does `moveAddress = U.toPointer(moveAddress)` before scanning.
            uint oldPtr = U.toPointer(oldBase);

            // Collect raw 32-bit pointer hits AND ARM LDR literal-pool hits,
            // then de-dup: a valid LDR slot is also a raw hit.
            var slots = new System.Collections.Generic.HashSet<uint>();
            foreach (uint slot in U.GrepPointerAll(rom.Data, oldPtr))
                slots.Add(slot);
            foreach (uint slot in U.GrepPointerAllOnLDR(rom.Data, oldPtr))
                slots.Add(slot);

            if (slots.Count == 0)
                return 0;

            int written = 0;
            IDisposable scope = (undo != null) ? ROM.BeginUndoScope(undo) : null;
            try
            {
                foreach (uint slot in slots)
                {
                    // #782 review: skip danger-zone (0x0–0x200) + out-of-ROM
                    // slots — repointing a false-positive raw/LDR hit whose slot
                    // lands inside the cartridge header would corrupt it. Use
                    // the explicit-ROM isSafetyOffset overload (NOT CoreState.ROM)
                    // so the gate honours the ROM being modified. isSafetyOffset
                    // only checks `slot < Length`; keep the explicit
                    // `slot + 4 > Length` check as defense for a slot at Length-1.
                    if (!U.isSafetyOffset(slot, rom) || slot + 4 > (uint)rom.Data.Length)
                        continue;
                    rom.write_p32(slot, newBase);
                    written++;
                }
            }
            finally
            {
                scope?.Dispose();
            }

            // Return only the slots actually repointed (skipped danger-zone /
            // out-of-ROM hits are excluded from the count). (#782 review.)
            return written;
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
