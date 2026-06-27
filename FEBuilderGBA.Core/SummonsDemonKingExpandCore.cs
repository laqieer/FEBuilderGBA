// SPDX-License-Identifier: GPL-3.0-or-later
// SummonsDemonKingExpandCore — the Core-side, ROM-MUTATING list-expand for the
// Demon-King Summon editor (#1606). Ports the WinForms
// SummonsDemonKingForm.AddressListExpandsEvent behaviour (SummonsDemonKingForm.cs:
// 23-24,68-72): grow the 20-byte `summons_demon_king` table AND write the new
// entry count to `summons_demon_king_count_address`.
//
// FE8-ONLY: summons_demon_king_pointer / summons_demon_king_count_address are 0
// on FE6/FE7, so IsEnabled gates the editor to FE8J/FE8U.
//
// Count-byte semantics (preserved from WinForms + the #1424 read guard):
//   The read-side loops `i <= countByte`, so a count byte of N yields N+1 visible
//   rows (rows 0..N). To add a new summon the caller picks a new count-byte value
//   N (> current, < 100); this helper grows the physical table to N+1 rows and
//   writes N to the count address.
//
// Atomicity (#885 recipe, mirroring MapSettingCore.ExpandMapSettingTable):
//   DataExpansionCore.ExpandTableTo can call rom.write_resize_data, which is NOT
//   captured by the ambient undo scope. So Expand keeps a pre-mutation snapshot
//   and, on ANY failed result or exception, performs a length-aware byte-identical
//   restore (down-resize to the snapshot length + Array.Copy) and clears the
//   caller's recorded undo ranges so a later UndoService.Rollback cannot replay
//   stale ranges over the already-restored ROM.
using System;

namespace FEBuilderGBA
{
    public static class SummonsDemonKingExpandCore
    {
        /// <summary>20-byte fixed entry size of the summons_demon_king table.</summary>
        public const uint EntrySize = 20u;

        /// <summary>
        /// Count bytes &gt;= this value are treated as corrupt (matches the #1424
        /// read guard and WinForms SummonsDemonKingForm.cs:37). The largest valid
        /// count byte is therefore 99.
        /// </summary>
        public const uint CorruptCountThreshold = 100u;

        /// <summary>Largest count byte the editor will accept (99).</summary>
        public const uint MaxCountByte = CorruptCountThreshold - 1u;

        /// <summary>Result of an <see cref="Expand"/> attempt.</summary>
        public struct ExpandResult
        {
            /// <summary>Whether the table was grown and the count byte written.</summary>
            public bool Success;
            /// <summary>Human-readable error when <see cref="Success"/> is false.</summary>
            public string Error;
            /// <summary>The count byte written on success.</summary>
            public uint NewCountByte;
            /// <summary>New table base offset on success.</summary>
            public uint NewBaseAddress;
        }

        /// <summary>
        /// True when the Demon-King Summon table can be expanded for this ROM:
        /// both the table pointer and the count address are defined and safe
        /// (FE8J/FE8U only; FE6/FE7 leave both at 0).
        /// </summary>
        public static bool IsEnabled(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return false;
            uint ptr = rom.RomInfo.summons_demon_king_pointer;
            uint countAddr = rom.RomInfo.summons_demon_king_count_address;
            if (ptr == 0 || countAddr == 0)
                return false;
            if (!U.isSafetyOffset(ptr, rom) || !U.isSafetyOffset(countAddr, rom))
                return false;
            // isSafetyOffset only checks addr < Length; p32 reads 4 bytes, so a
            // pointer in the last 3 bytes of the ROM would read out of bounds.
            // Guard the full 4-byte span before resolving the table base.
            if (ptr + 4 > (uint)rom.Data.Length)
                return false;
            // The pointer slot must resolve to a safe table base.
            uint baseAddr = rom.p32(ptr);
            return U.isSafetyOffset(baseAddr, rom);
        }

        /// <summary>
        /// Read the raw count byte from summons_demon_king_count_address. Returns 0
        /// when the editor is unavailable.
        /// </summary>
        public static uint ReadCountByte(ROM rom)
        {
            if (!IsEnabled(rom))
                return 0;
            return rom.u8(rom.RomInfo.summons_demon_king_count_address);
        }

        /// <summary>
        /// Grow the summons_demon_king table so it holds <paramref name="newCountByte"/>+1
        /// rows and write <paramref name="newCountByte"/> to the count address, all
        /// under the caller's ambient undo scope. Rejects (ZERO mutation) when the
        /// current count is corrupt (&gt;= 100), when <paramref name="newCountByte"/>
        /// is &gt;= 100, or when it does not exceed the current count. On any failed
        /// expand result or exception the ROM is restored byte-for-byte (length-aware)
        /// and the caller's undo ranges are cleared.
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="newCountByte">New count-byte value (&gt; current, &lt; 100).</param>
        /// <param name="undo">The caller's active <see cref="Undo.UndoData"/> (the
        /// ambient scope). Used for the stale-range clear on fault restore. May be
        /// null in headless callers that drive the ambient scope another way.</param>
        /// <param name="error">Human-readable error on failure.</param>
        public static ExpandResult Expand(ROM rom, uint newCountByte, Undo.UndoData undo, out string error)
        {
            error = "";
            if (!IsEnabled(rom))
            {
                error = R._("Demon King Summon table is not available for this ROM.");
                return Fail(error);
            }

            uint pointerAddr = rom.RomInfo.summons_demon_king_pointer;
            uint countAddr = rom.RomInfo.summons_demon_king_count_address;
            uint currentCountByte = rom.u8(countAddr);

            if (currentCountByte >= CorruptCountThreshold)
            {
                error = R._("The current Demon King Summon count byte (0x{0:X}) is corrupt — cannot expand.", currentCountByte);
                return Fail(error);
            }
            if (newCountByte >= CorruptCountThreshold)
            {
                error = R._("New count must be at most {0}.", MaxCountByte);
                return Fail(error);
            }
            if (newCountByte <= currentCountByte)
            {
                error = R._("New count must be greater than the current count ({0}).", currentCountByte);
                return Fail(error);
            }

            // Visible rows = countByte + 1 (rows 0..countByte). currentCountByte < 100
            // so these +1 additions cannot overflow.
            uint currentRows = currentCountByte + 1;
            uint newRows = newCountByte + 1;

            // Pre-mutation snapshot for the byte-identical (length-aware) restore on
            // ANY fault — guarantees a FAILED expand mutates ZERO bytes even beyond
            // what the ambient undo scope tracks (a free-space resize-append can grow
            // rom.Data).
            byte[] snap = (byte[])rom.Data.Clone();

            try
            {
                var result = DataExpansionCore.ExpandTableTo(
                    rom, pointerAddr, EntrySize, currentRows, newRows);

                if (!result.Success)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = result.Error ?? R._("Demon King Summon table expansion failed.");
                    return Fail(error);
                }

                // Write the new count byte LAST so the grown rows are already present
                // when the read-side honours it. Routed through the ambient undo scope.
                rom.write_u8(countAddr, (byte)newCountByte);

                return new ExpandResult
                {
                    Success = true,
                    NewCountByte = newCountByte,
                    NewBaseAddress = result.NewBaseAddress,
                };
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap, undo);
                error = R._("Demon King Summon table expansion failed: {0}", ex.Message);
                return Fail(error);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore (mirrors
        /// <c>MapSettingCore.RestoreSnapshot</c> / WaitIconImportCore #885/#923): a
        /// free-space resize-append can GROW rom.Data, so down-resize back to the
        /// snapshot length BEFORE the in-place copy. After restoring, CLEAR the
        /// caller's recorded undo ranges so a subsequent UndoService.Rollback cannot
        /// replay the now out-of-date ranges against the already-restored ROM.
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap, Undo.UndoData undo)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
            undo?.list?.Clear();
        }

        static ExpandResult Fail(string error) => new ExpandResult { Success = false, Error = error };
    }
}
