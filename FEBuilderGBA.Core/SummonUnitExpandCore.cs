using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Summon Unit (FE8) list-expand orchestrator (#1605). Ports
    /// the WinForms <c>SummonUnitForm.AddressListExpandsEvent</c> table-expand
    /// affordance ("リストの拡張") for the 2-byte <c>summon_unit_pointer</c> table.
    ///
    /// <para>This is a faithful clone of
    /// <see cref="StatusGameOptionCore.ExpandGameOptionTable(ROM, uint, Undo.UndoData, out string)"/>
    /// (#1607, itself a clone of <c>MapSettingCore.ExpandMapSettingTable</c> #1085):
    /// same FIRST-fill + complete reference repoint (raw 32-bit + ARM-Thumb LDR
    /// literal-pool) + audit guard + byte-identical (length-aware) fault restore
    /// (#885/#923).</para>
    ///
    /// <para><b>FE8-only.</b> The summon table only exists on FE8 — FE6/FE7 have
    /// <c>summon_unit_pointer == 0</c>. The expand is gated on
    /// <c>rom.RomInfo.version == 8</c> (mirroring the WinForms
    /// <c>version != 8</c> Debug.Assert + cancel).</para>
    ///
    /// <para><b>Summon-specific deltas from the StatusGameOption template:</b></para>
    /// <list type="bullet">
    /// <item>The stride is 2 bytes (<see cref="EntrySize"/>), and a row is valid
    /// while its first byte (the unit id) is non-zero (the editor's predicate,
    /// <c>SummonUnitForm.cs:33</c>).</item>
    /// <item><b>FullZeroTerminatorRow = true is REQUIRED</b> (Copilot plan-review
    /// blocker): the 2-byte <c>u8 == 0</c> scan needs a full 2-byte zero
    /// terminator row to stop at exactly <c>newCount</c>. A 4-byte
    /// <c>0xFFFFFFFF</c> terminator (the default) reads as a phantom VALID row
    /// (its first byte is <c>0xFF != 0</c>), over-counting the list.</item>
    /// <item>After the move, several SPECIFIC hardcoded engine sites that point at
    /// <c>table + 1</c> and several that store <c>count - 1</c> must be rewritten,
    /// with DIFFERENT addresses for FE8J vs FE8U. These are ported verbatim from
    /// <c>SummonUnitForm.cs:78-117</c> (including the two SkillSystems greps that
    /// locate the extra SkillSystems summon sites when that patch is installed).</item>
    /// </list>
    /// </summary>
    public static class SummonUnitExpandCore
    {
        /// <summary>Fixed Summon Unit struct stride in bytes (unit id + summoned id).</summary>
        public const uint EntrySize = 2;

        /// <summary>
        /// Upper bound on the number of summon rows this orchestrator enumerates /
        /// expands to. Mirrors the WinForms <c>count &gt;= 257</c> rejection
        /// (<c>SummonUnitForm.cs:69</c>): a target beyond 256 rows is refused. The
        /// editor itself scans well past this (<c>i &lt; 0x200</c>), so the cap is
        /// a deliberate WF-parity ceiling, not a scan limit.
        /// </summary>
        public const int MaxRows = 0x100; // 256

        /// <summary>
        /// Upper sanity bound on how many references
        /// <see cref="ExpandSummonUnitTable"/> expects to repoint to the summon
        /// table base. A hit count above this is almost certainly a false-positive
        /// flood from a coincidental raw <c>u32 == base</c>, so the audit guard
        /// rejects the expand WITHOUT mutating. Mirrors
        /// <see cref="StatusGameOptionCore.MaxPlausibleRepointSlots"/> (#1085 finding #4).
        /// </summary>
        public const int MaxPlausibleRepointSlots = 64;

        /// <summary>
        /// Count the visible summon rows exactly as the Avalonia / WinForms editor
        /// enumerates them: from the table base, a row is valid while its first
        /// byte (the unit id) is non-zero (<c>Program.ROM.u8(addr) != 0x00</c>,
        /// <c>SummonUnitForm.cs:33</c>); the scan stops at the first zero row,
        /// capped at <see cref="MaxRows"/>. READ-ONLY; never throws.
        /// </summary>
        public static uint CountSummonUnits(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return 0;
            uint pointerAddr = rom.RomInfo.summon_unit_pointer;
            if (pointerAddr == 0) return 0;
            // 4-byte extent guard: rom.p32 only checks `addr >= Length` then
            // reads a u32, so a pointerAddr within the last 3 bytes of a
            // truncated/corrupt ROM would index out of bounds. Guard the full
            // 4-byte read so this enumerator truly never throws.
            if (pointerAddr + 4 > (uint)rom.Data.Length) return 0;
            uint baseAddr = rom.p32(pointerAddr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint count = 0;
            for (int i = 0; i < MaxRows; i++)
            {
                uint addr = baseAddr + (uint)(i * (int)EntrySize);
                if (addr + EntrySize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Expand the FE8 <c>summon_unit_pointer</c> table by
        /// <paramref name="addCount"/> rows using FIRST-fill (so the new rows
        /// enumerate — a zero-filled row would have <c>u8 == 0</c> at +0 and fail
        /// the editor's validity gate) and complete reference repointing (raw
        /// 32-bit + ARM-Thumb LDR literal-pool, whole-ROM) so no engine site is
        /// left pointing into the wiped old region.
        ///
        /// <para><b>Terminator policy:</b> a FULL 2-byte all-zero terminator row
        /// (<c>FullZeroTerminatorRow = true</c>), NOT the single <c>0xFFFFFFFF</c>
        /// dword. The summon scanner reads <c>u8(addr)</c>, so only a full zero
        /// row guarantees <c>u8(terminator) == 0</c> → scan stops at exactly
        /// <c>newCount</c> (Copilot plan-review blocker).</para>
        ///
        /// <para><b>FE8-specific fixups:</b> after the move, the SPECIFIC
        /// hardcoded engine sites that store <c>table + 1</c> and those that store
        /// <c>count - 1</c> are rewritten — DIFFERENT addresses for FE8J vs FE8U,
        /// plus the two SkillSystems summon sites when that patch is installed.
        /// All writes route through the caller's ambient undo scope
        /// (<see cref="ROM.write_u32(uint, uint)"/> /
        /// <see cref="ROM.write_u8(uint, uint)"/>), each EOF-guarded.</para>
        ///
        /// <para><b>Atomic:</b> the whole operation runs under the caller's ambient
        /// undo scope AND a defensive snapshot. On ANY fault — a failed
        /// <see cref="DataExpansionCore.ExpandTableTo(ROM, uint, uint, uint, uint, DataExpansionCore.ExpandOptions)"/>,
        /// a failed audit guard, or an exception — the ROM is restored
        /// byte-identical (bytes AND length) with ZERO net change (#885/#923).</para>
        ///
        /// <para><b>Audit guard (#1085 finding #4):</b> the recorded repointed
        /// slots must (a) be non-empty, (b) include the canonical
        /// <c>summon_unit_pointer</c> slot, and (c) not exceed
        /// <see cref="MaxPlausibleRepointSlots"/>. A failed guard restores the
        /// snapshot, so the outcome is equivalent to a pre-mutation veto.</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="addCount">Number of rows to add (must be &gt;= 1).</param>
        /// <param name="undo">The caller's active undo transaction. On any fault
        /// its <c>list</c> is CLEARED after the snapshot restore so a later
        /// caller-side rollback cannot replay stale ranges over the already-restored
        /// ROM. May be <c>null</c> (tests that drive the ambient scope directly).</param>
        /// <param name="error">Set to a human-readable message on failure; empty on success.</param>
        public static DataExpansionCore.ExpandResult ExpandSummonUnitTable(ROM rom, uint addCount, Undo.UndoData undo, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            {
                error = R._("ROM not loaded.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            if (addCount == 0)
            {
                error = R._("Add count must be at least 1.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // FE8-only gate. The summon table only exists on FE8; FE6/FE7 have
            // summon_unit_pointer == 0. Mirrors WF `version != 8` Debug.Assert+cancel.
            if (rom.RomInfo.version != 8)
            {
                error = R._("Summon unit table is only available on FE8.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            uint pointerAddr = rom.RomInfo.summon_unit_pointer;
            if (pointerAddr == 0)
            {
                error = R._("Summon unit table is only available on FE8.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            // 4-byte extent guard BEFORE any rom.p32(pointerAddr) read (p32 only
            // checks `addr >= Length` then reads a u32) so a truncated/corrupt
            // ROM can never throw inside the no-partial-commit guarantee.
            if (pointerAddr + 4 > (uint)rom.Data.Length)
            {
                error = R._("Summon unit table is only available on FE8.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // currentCount = the editor's enumerated visible row count.
            uint currentCount = CountSummonUnits(rom);
            if (currentCount == 0)
            {
                // FIRST-fill needs a source row 0 to copy; with no enumerated
                // rows there is nothing to seed the new rows from.
                error = R._("Cannot expand: the summon list is empty (no row 0 to copy).");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // Overflow-safe target count.
            if (addCount > uint.MaxValue - currentCount)
            {
                error = R._("Add count overflows the table size.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            uint newCount = currentCount + addCount;

            // Cap at MaxRows (0x100). This also enforces WF's `count >= 257`
            // rejection (SummonUnitForm.cs:69) since MaxRows == 256.
            if (newCount > MaxRows)
            {
                error = R._("Cannot expand: the new summon count ({0}) exceeds the maximum of {1}.", newCount, MaxRows);
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            uint oldBase = rom.p32(pointerAddr);

            // Defensive snapshot for the byte-identical (length-aware) restore on
            // ANY fault — guarantees a FAILED expand mutates ZERO bytes even
            // beyond what the ambient undo scope tracks.
            byte[] snap = (byte[])rom.Data.Clone();

            try
            {
                var result = DataExpansionCore.ExpandTableTo(
                    rom, pointerAddr, EntrySize, currentCount, newCount,
                    new DataExpansionCore.ExpandOptions
                    {
                        Fill = DataExpansionCore.ExpandFill.First,
                        Repoint = DataExpansionCore.ExpandRepoint.RawAndLdrAll,
                        // FULL 2-byte zero terminator so u8(terminator) == 0 and
                        // the +0 unit-id scan stops at exactly newCount (a
                        // 0xFFFFFFFF terminator's first byte is 0xFF → phantom row).
                        FullZeroTerminatorRow = true,
                    });

                if (!result.Success)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = result.Error ?? R._("Summon unit table expansion failed.");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // --- Audit guard (#1085 finding #4) ---------------------------
                var slots = result.RepointedSlots ?? System.Array.Empty<uint>();

                // (a) zero repointed slots ⇒ the canonical pointer was not even
                // found — abort with ZERO net change.
                if (slots.Count == 0)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Summon expand aborted: no references were repointed (expected at least the canonical pointer slot).");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (b) the canonical summon_unit_pointer slot MUST be among them.
                bool canonicalCovered = false;
                foreach (uint s in slots)
                {
                    if (s == pointerAddr) { canonicalCovered = true; break; }
                }
                if (!canonicalCovered)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Summon expand aborted: the canonical pointer slot (0x{0:X}) was not among the repointed references.", pointerAddr);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (c) implausibly large ⇒ likely a false-positive flood — abort.
                if (slots.Count > MaxPlausibleRepointSlots)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Summon expand aborted: {0} references would be repointed, exceeding the plausible maximum ({1}) — likely a false-positive match.", slots.Count, MaxPlausibleRepointSlots);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // --- Summon-specific +1 refs + count bytes (WF parity) --------
                // Port of SummonUnitForm.cs:78-117. These rewrite SPECIFIC
                // hardcoded engine sites (DIFFERENT per FE8J/FE8U) that store
                // `table + 1` and `count - 1`. All writes route through the
                // ambient undo scope (rom.write_u32 / rom.write_u8); each is
                // EOF-guarded so a truncated ROM can never overrun.
                uint newBase = result.NewBaseAddress;

                bool isMulti = rom.RomInfo.is_multibyte;

                // table+1 pointer refs → write toPointer(newBase + 1).
                uint[] plus1Addrs = isMulti
                    ? new uint[] { 0x024450, 0x07D14C }                                       // FE8J
                    : new uint[] { 0x0244A0, 0x07AE04, SearchSkillSystemsSummonPlus1(rom) };  // FE8U
                uint plus1Value = U.toPointer(newBase + 1);
                foreach (uint addr in plus1Addrs)
                {
                    if (addr == U.NOT_FOUND) continue;
                    if (addr + 4 <= (uint)rom.Data.Length)
                        rom.write_u32(addr, plus1Value);
                }

                // count bytes → write (newCount - 1) as u8.
                uint[] countAddrs = isMulti
                    ? new uint[] { 0x07D0B6, 0x0243EA }                                       // FE8J
                    : new uint[] { 0x07AD66, 0x024436, SearchSkillSystemsSummonCount(rom) };  // FE8U
                uint countValue = newCount - 1;
                foreach (uint addr in countAddrs)
                {
                    if (addr == U.NOT_FOUND) continue;
                    if (addr + 1 <= (uint)rom.Data.Length)
                        rom.write_u8(addr, countValue);
                }

                Log.Notify(string.Format(
                    "Summon list expand: base 0x{0:X} -> 0x{1:X}, {2} -> {3} rows, {4} reference(s) repointed.",
                    U.toOffset(oldBase), result.NewBaseAddress, currentCount, newCount, slots.Count));
                return result;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap, undo);
                error = R._("Summon unit table expansion failed: {0}", ex.Message);
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy. After restoring the bytes, CLEARS the caller's
        /// <paramref name="undo"/> position list (when non-null) so a subsequent
        /// caller-side rollback cannot replay the now out-of-date ranges over the
        /// already-restored ROM. Mirrors <c>StatusGameOptionCore.RestoreSnapshot</c>
        /// (#885/#923).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap, Undo.UndoData undo)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
            undo?.list?.Clear();
        }

        // ════════════════════════════════════════════════════════════════════
        // SkillSystems summon-site greps (rom-aware ports of
        // SummonUnitForm.cs:120-152). Each returns U.NOT_FOUND when SkillSystems
        // is not installed (so the caller skips the write) OR when the signature
        // is not present — exactly the WF behavior (the WF warning was already
        // commented out, so a missing match is a silent skip, not a hard error).
        // ════════════════════════════════════════════════════════════════════

        static uint SearchSkillSystemsSummonPlus1(ROM rom)
        {
            if (SkillSystemTextScanner.SearchSkillSystem(rom) != SkillSystemTextScanner.SkillSystemEnum.SkillSystem)
                return U.NOT_FOUND;
            byte[] need = new byte[] { 0xB6, 0x46, 0x00, 0xF8, 0x02, 0x1C, 0x00, 0x2A, 0x12, 0xD0, 0x10, 0x68, 0x00, 0x28, 0x0F, 0xD0, 0x00, 0x79, 0x29, 0x78, 0x88, 0x42, 0x0B, 0xD1, 0xD1, 0x68, 0x04, 0x48, 0x08, 0x40, 0x00, 0x28, 0xCC, 0xD1, 0xBE, 0xE7 };
            uint addr = U.GrepEnd(rom.Data, need, rom.RomInfo.compress_image_borderline_address, 0, 4);
            return addr; // U.GrepEnd returns U.NOT_FOUND when not found
        }

        static uint SearchSkillSystemsSummonCount(ROM rom)
        {
            if (SkillSystemTextScanner.SearchSkillSystem(rom) != SkillSystemTextScanner.SkillSystemEnum.SkillSystem)
                return U.NOT_FOUND;
            byte[] need = new byte[] { 0x1D, 0xE0, 0x03, 0x20, 0x48, 0xE0, 0x00, 0x00, 0x50, 0x4E, 0x00, 0x03, 0xA5, 0x5C, 0x02, 0x08, 0x29, 0xFD, 0x04, 0x08, 0xFF, 0xFF, 0x00, 0x00, 0x30, 0x95, 0x00, 0x09, 0x0B, 0x48, 0x01, 0x40, 0xD1, 0x60, 0x38, 0xE0, 0x01, 0x32 };
            uint addr = U.GrepEnd(rom.Data, need, rom.RomInfo.compress_image_borderline_address, 0, 4);
            return addr;
        }
    }
}
