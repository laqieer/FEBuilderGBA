using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Status Screen Option (Game Option) list-expand orchestrator
    /// (#1607). Ports the WinForms <c>StatusOptionForm</c> table-expand affordance
    /// (the <c>AddressListExpandsButton</c> "リストの拡張" + the
    /// <c>OnPreGameOptionExtendsWarningHandler</c> repoint warning) for the 44-byte
    /// <c>status_game_option</c> table.
    ///
    /// <para>This is a faithful clone of
    /// <see cref="MapSettingCore.ExpandMapSettingTable(ROM, uint, Undo.UndoData, out string)"/>
    /// (#1085): same FIRST-fill + complete reference repoint + audit guard +
    /// byte-identical (length-aware) fault restore (#885/#923). The only deltas
    /// are the table identity (the <c>status_game_option_pointer</c> pointer + the
    /// fixed 44-byte stride) and the per-row validity predicate (a valid ASM
    /// pointer at offset <c>+40</c>, matching the editor's enumeration).</para>
    /// </summary>
    public static class StatusGameOptionCore
    {
        /// <summary>Fixed Status Option (Game Option) struct stride in bytes.</summary>
        public const uint EntrySize = 44;

        /// <summary>
        /// Upper bound on the number of game-option rows the editor / this
        /// orchestrator enumerate. Matches the canonical
        /// <c>StructExportCore.status_options</c> cap (<c>i &lt;= 0xFF</c>), so a
        /// row past 255 is treated as out of range (#1607 plan-review finding #2).
        /// </summary>
        public const int MaxRows = 0x100;

        /// <summary>
        /// Upper sanity bound on how many references
        /// <see cref="ExpandGameOptionTable"/> expects to repoint to the
        /// game-option table base. A hit count above this is almost certainly a
        /// false-positive flood from a coincidental raw <c>u32 == base</c>, so the
        /// audit guard rejects the expand WITHOUT mutating. Mirrors
        /// <see cref="MapSettingCore.MaxPlausibleRepointSlots"/> (#1085 finding #4).
        /// </summary>
        public const int MaxPlausibleRepointSlots = 64;

        /// <summary>
        /// Count the visible game-option rows exactly as the Avalonia / WinForms
        /// editor enumerates them: from the table base, a row is valid while the
        /// ASM pointer at offset <c>+40</c> is a real pointer
        /// (<see cref="U.isPointer(uint)"/>); the scan stops at the first invalid
        /// row, capped at <see cref="MaxRows"/>. READ-ONLY; never throws.
        /// </summary>
        public static uint CountGameOptions(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return 0;
            uint pointerAddr = rom.RomInfo.status_game_option_pointer;
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
                if (!U.isPointer(rom.u32(addr + 40))) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Expand the <c>status_game_option</c> table by <paramref name="addCount"/>
        /// rows using FIRST-fill (so the new rows are valid and ENUMERATE — a
        /// zero-filled row has <c>0x00000000</c> at <c>+40</c> and would NOT pass
        /// the editor's <see cref="U.isPointer(uint)"/> validity gate) and complete
        /// reference repointing (raw 32-bit + ARM-Thumb LDR literal-pool,
        /// whole-ROM) so no engine site is left pointing into the wiped old region.
        ///
        /// <para><b>Terminator policy:</b> a FULL 44-byte all-zero terminator row
        /// (<c>FullZeroTerminatorRow = true</c>), NOT the single <c>0xFFFFFFFF</c>
        /// dword. The Status Option scanner reads <c>u32(addr+40)</c>, so only a
        /// full zero row guarantees <c>terminator+40 == 0</c> → scan stops at
        /// exactly <c>newCount</c> (#1607 plan-review finding #1).</para>
        ///
        /// <para><b>Atomic:</b> the whole operation runs under the caller's ambient
        /// undo scope AND a defensive snapshot. On ANY fault — a failed
        /// <see cref="DataExpansionCore.ExpandTableTo(ROM, uint, uint, uint, uint, DataExpansionCore.ExpandOptions)"/>,
        /// a failed audit guard, or an exception — the ROM is restored
        /// byte-identical (bytes AND length) with ZERO net change (#885/#923).</para>
        ///
        /// <para><b>Audit guard (#1085 finding #4):</b> the recorded repointed
        /// slots must (a) be non-empty, (b) include the canonical
        /// <c>status_game_option_pointer</c> slot, and (c) not exceed
        /// <see cref="MaxPlausibleRepointSlots"/>. A failed guard restores the
        /// snapshot, so the outcome is equivalent to a pre-mutation veto.</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="addCount">Number of rows to add (must be &gt;= 1).</param>
        /// <param name="undo">The caller's active undo transaction (the same
        /// <see cref="Undo.UndoData"/> passed to the surrounding
        /// <c>UndoService.Begin</c> / <c>ROM.BeginUndoScope</c>). On any fault its
        /// <c>list</c> is CLEARED after the snapshot restore so a later
        /// caller-side rollback cannot replay stale ranges over the already-restored
        /// ROM. May be <c>null</c> (tests that drive the ambient scope directly).</param>
        /// <param name="error">Set to a human-readable message on failure; empty on success.</param>
        public static DataExpansionCore.ExpandResult ExpandGameOptionTable(ROM rom, uint addCount, Undo.UndoData undo, out string error)
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

            uint pointerAddr = rom.RomInfo.status_game_option_pointer;
            if (pointerAddr == 0)
            {
                error = R._("Game option table is not available for this ROM.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // currentCount = the editor's enumerated visible row count.
            uint currentCount = CountGameOptions(rom);
            if (currentCount == 0)
            {
                // FIRST-fill needs a source row 0 to copy; with no enumerated
                // rows there is nothing to seed the new rows from.
                error = R._("Cannot expand: the game option list is empty (no row 0 to copy).");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // Overflow-safe target count.
            if (addCount > uint.MaxValue - currentCount)
            {
                error = R._("Add count overflows the table size.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            uint newCount = currentCount + addCount;

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
                        // FULL 44-byte zero terminator so terminator+40 == 0 and
                        // the +40 scan stops at exactly newCount (#1607 finding #1).
                        FullZeroTerminatorRow = true,
                    });

                if (!result.Success)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = result.Error ?? R._("Game option table expansion failed.");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // --- Audit guard (#1085 finding #4) ---------------------------
                var slots = result.RepointedSlots ?? System.Array.Empty<uint>();

                // (a) zero repointed slots ⇒ the canonical pointer was not even
                // found — abort with ZERO net change.
                if (slots.Count == 0)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Game option expand aborted: no references were repointed (expected at least the canonical pointer slot).");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (b) the canonical status_game_option_pointer slot MUST be among them.
                bool canonicalCovered = false;
                foreach (uint s in slots)
                {
                    if (s == pointerAddr) { canonicalCovered = true; break; }
                }
                if (!canonicalCovered)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Game option expand aborted: the canonical pointer slot (0x{0:X}) was not among the repointed references.", pointerAddr);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (c) implausibly large ⇒ likely a false-positive flood — abort.
                if (slots.Count > MaxPlausibleRepointSlots)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Game option expand aborted: {0} references would be repointed, exceeding the plausible maximum ({1}) — likely a false-positive match.", slots.Count, MaxPlausibleRepointSlots);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                Log.Notify(string.Format(
                    "GameOption list expand: base 0x{0:X} -> 0x{1:X}, {2} -> {3} rows, {4} reference(s) repointed.",
                    U.toOffset(oldBase), result.NewBaseAddress, currentCount, newCount, slots.Count));
                return result;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap, undo);
                error = R._("Game option table expansion failed: {0}", ex.Message);
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy. After restoring the bytes, CLEARS the caller's
        /// <paramref name="undo"/> position list (when non-null) so a subsequent
        /// caller-side rollback cannot replay the now out-of-date ranges over the
        /// already-restored ROM. Mirrors <c>MapSettingCore.RestoreSnapshot</c>
        /// (#885/#923).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap, Undo.UndoData undo)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
            undo?.list?.Clear();
        }
    }
}
