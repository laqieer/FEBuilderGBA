// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side extraction of ImageBGForm's helpers that the Avalonia
// view needs (#429). Mirrors ImageBattleBGCore in shape but reads the
// generic BG (background image) pointer table at `RomInfo.bg_pointer`
// instead of the battle-BG slot.
//
// Helpers:
//
//   1. `ExpandList`     - 255-cap list-expansion semantics from
//                         `InputFormRef.OnAddressListExpandsEventHandler`,
//                         minus the interactive `MoveToFreeSpaceForm`
//                         UI. Allocates new space via
//                         `ROM.FindFreeSpace`, preserves old rows,
//                         fills new rows from row[0], repoints
//                         `bg_pointer`, records undo data through
//                         the ambient ROM undo scope.
//   2. `IsReserveBgId`  - returns true if the supplied ID matches one
//                         of the system reserves (`bg_reserve_black_bgid`
//                         or `bg_reserve_random_bgid`). The View shows
//                         a warning banner and confirms before
//                         overwriting.
//   3. `Is255BG`        - mirror of `ImageBGForm.Is255BG`: under
//                         `PatchUtil.BG256Color`, an entry with
//                         non-zero P0 and P4==0 is a 255-color
//                         cutscene background.
//   4. `IsValidEntry`   - mirror of the WF `InputFormRef.Init` callback
//                         in `ImageBGForm.Init`. Under `BG256Color`,
//                         `P4 <= 1` is accepted on `isPointerOrNULL(P0)`
//                         alone; otherwise both P0 and P4 must be
//                         pointer-or-NULL. Critical for `LoadList`
//                         to not truncate the table at P4=1.
//
// All helpers are platform-independent and reusable from WinForms,
// Avalonia, and CLI.
using System;

namespace FEBuilderGBA
{
    public static class ImageBGCore
    {
        /// <summary>
        /// Size of one BG table entry in ROM bytes.
        ///
        /// Entry layout:
        ///   +0  u32 image   pointer (LZ77-compressed graphic)
        ///   +4  u32 tsa     pointer (RAW header-packed TSA) OR
        ///                   0/1 flag under BG256Color patch
        ///   +8  u32 palette pointer (RAW palette data)
        /// </summary>
        public const uint EntrySize = 12;

        /// <summary>
        /// Maximum count the WinForms `AddressListExpandsButton_255`
        /// button accepts (the suffix is the inclusive cap; see
        /// `InputFormRef.GetAddressListExpandsMax`). New counts greater
        /// than this are rejected to prevent unbounded ROM growth.
        /// </summary>
        public const uint MaxListCount = 255;

        /// <summary>
        /// Expand the BG pointer table to <paramref name="newCount"/>
        /// entries — ambient-scope flavour.
        ///
        /// Mirrors the WinForms `AddressListExpandsButton_255` flow
        /// without the interactive `MoveToFreeSpaceForm` dialog:
        /// <list type="number">
        ///   <item>Validate inputs (caps at <see cref="MaxListCount"/>,
        ///     refuses zero <paramref name="oldCount"/>, refuses shrinks).</item>
        ///   <item>Find a contiguous free region big enough for
        ///     <c>newCount * EntrySize</c> bytes via
        ///     <see cref="ROM.FindFreeSpace"/>.</item>
        ///   <item>Copy the <paramref name="oldCount"/> existing rows to
        ///     the new region byte-for-byte.</item>
        ///   <item>Fill the new rows by duplicating row[0] (matches WF's
        ///     "fill from first record" behavior).</item>
        ///   <item>Repoint <c>rom.RomInfo.bg_pointer</c> to the new
        ///     region.</item>
        /// </list>
        ///
        /// Undo tracking is recorded through the ROM's ambient undo
        /// scope (<see cref="ROM.BeginUndoScope"/>). The caller MUST
        /// open the scope before calling this overload — the non-explicit
        /// ROM write overloads append each write to the active scope's
        /// UndoData automatically. This avoids the double-snapshot
        /// pitfall where explicit-overload writes also re-append to the
        /// ambient list (precedent: PR #513 ImageBattleBGCore).
        /// </summary>
        /// <returns>New base ROM offset, or <see cref="U.NOT_FOUND"/>
        ///   on failure.</returns>
        public static uint ExpandList(ROM rom, uint oldCount, uint newCount)
        {
            if (rom == null || rom.RomInfo == null)
                return U.NOT_FOUND;
            if (oldCount == 0)
                return U.NOT_FOUND;
            if (newCount <= oldCount)
                return U.NOT_FOUND; // refuse shrinks and no-ops
            if (newCount > MaxListCount)
                return U.NOT_FOUND;

            uint pointerSlot = rom.RomInfo.bg_pointer;
            if (pointerSlot == 0)
                return U.NOT_FOUND;

            uint origBase = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(origBase))
                return U.NOT_FOUND;
            if (origBase + oldCount * EntrySize > (uint)rom.Data.Length)
                return U.NOT_FOUND;

            // Snapshot row[0] before we relocate — it's the template for
            // every new row.
            byte[] row0 = rom.getBinaryData(origBase, EntrySize);

            // Find a contiguous free region. Start the search past the
            // existing data area so we don't overlap the old table.
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint needSize = newCount * EntrySize;
            uint newBase = rom.FindFreeSpace(searchStart, needSize);
            if (newBase == U.NOT_FOUND)
            {
                // Fallback to a conservative search start when the
                // high-half is fragmented.
                newBase = rom.FindFreeSpace(0x100, needSize);
            }
            if (newBase == U.NOT_FOUND)
                return U.NOT_FOUND;

            // 1. Copy old rows. Undo captured by ambient scope.
            byte[] oldRows = rom.getBinaryData(origBase, oldCount * EntrySize);
            rom.write_range(newBase, oldRows);

            // 2. Fill the new rows by cloning row[0].
            for (uint i = oldCount; i < newCount; i++)
            {
                uint rowAddr = newBase + i * EntrySize;
                rom.write_range(rowAddr, row0);
            }

            // 3. Repoint bg_pointer to the new base.
            rom.write_p32(pointerSlot, newBase);

            return newBase;
        }

        /// <summary>
        /// Compatibility overload — opens an ambient undo scope around
        /// the supplied <paramref name="undo"/> (or reuses the one
        /// already active for the same UndoData) and dispatches to the
        /// parameterless variant. Single source of truth: every write
        /// records into exactly one undo list.
        /// </summary>
        public static uint ExpandList(ROM rom, uint oldCount, uint newCount, Undo.UndoData undo)
        {
            if (undo == null)
                return U.NOT_FOUND;
            if (ROM.GetAmbientUndoData() == undo)
            {
                return ExpandList(rom, oldCount, newCount);
            }
            using (ROM.BeginUndoScope(undo))
            {
                return ExpandList(rom, oldCount, newCount);
            }
        }

        /// <summary>
        /// True when the BG ID matches one of the system-reserved BG
        /// slots (system black background or support-conversation random
        /// background). Both reserves can be <see cref="U.NOT_FOUND"/>
        /// when the running ROM version does not expose them
        /// (e.g. FE6 has neither, FE7 has only black).
        /// </summary>
        public static bool IsReserveBgId(ROM rom, uint id)
        {
            if (rom?.RomInfo == null)
                return false;

            uint black = rom.RomInfo.bg_reserve_black_bgid;
            uint random = rom.RomInfo.bg_reserve_random_bgid;

            if (black != U.NOT_FOUND && id == black)
                return true;
            if (random != U.NOT_FOUND && id == random)
                return true;
            return false;
        }

        /// <summary>
        /// True when the entry described by <paramref name="p0"/>
        /// (image pointer) and <paramref name="p4"/> (TSA pointer)
        /// represents a 255-color cutscene background under the
        /// <c>BG256Color</c> patch. Mirrors
        /// <c>ImageBGForm.Is255BG</c>.
        /// </summary>
        public static bool Is255BG(ROM rom, uint p0, uint p4)
        {
            if (!PatchDetection.HasBG256ColorPatch(rom))
                return false;
            return p0 != 0 && p4 == 0;
        }

        /// <summary>
        /// True when an entry with image pointer <paramref name="p0"/>
        /// and TSA pointer/flag <paramref name="p4"/> is a valid table
        /// row. Mirrors the WF <c>ImageBGForm.Init</c> callback exactly:
        /// <list type="bullet">
        ///   <item>Under <c>BG256Color</c> patch: if <c>p4 &lt;= 1</c>,
        ///     accept on <c>U.isPointerOrNULL(p0)</c> alone (P4 is the
        ///     255/224 mode flag, not a pointer).</item>
        ///   <item>Otherwise: require both
        ///     <c>U.isPointerOrNULL(p0)</c> and
        ///     <c>U.isPointerOrNULL(p4)</c>.</item>
        /// </list>
        /// Critical for <c>LoadList</c> to not truncate the table at
        /// the first 255/224-color row.
        /// </summary>
        public static bool IsValidEntry(ROM rom, uint p0, uint p4)
        {
            if (PatchDetection.HasBG256ColorPatch(rom))
            {
                if (p4 <= 1)
                {
                    return U.isPointerOrNULL(p0);
                }
            }
            return U.isPointerOrNULL(p0) && U.isPointerOrNULL(p4);
        }
    }
}
