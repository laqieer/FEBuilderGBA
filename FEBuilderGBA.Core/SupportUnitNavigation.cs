// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-editor navigation helpers for the Support Unit Editor (#358).
//
// In WinForms, each Support Unit list row is keyed by the *unit that points
// at that support entry* via `UnitForm.GetUnitIDWhereSupportAddr(addr)`.
// The label is `hex(uid+1) + " " + UnitForm.GetUnitName(uid+1)`. This file
// extracts that lookup so Avalonia viewmodels (FE6, FE7/8) can mirror the
// same behavior and so headless tests can assert it without spinning up the
// WinForms `InputFormRef` plumbing.
//
// Pure functions, no UI dependencies, no ROM mutation. The companion
// "ChangeSupportPointer" write-back is intentionally NOT exposed here — the
// Support Unit Editor in this PR shows the source unit read-only and does
// not write the unit table's support pointer. The mutation path is tracked
// under #437 to land separately.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure helpers that mirror WinForms <c>UnitForm.GetUnitIDWhereSupportAddr</c>
    /// and <c>UnitForm.GetSupportAddrWhereUnitID</c> so Avalonia editors can
    /// derive owner-based labels and Unit-Editor jumps without depending on
    /// WinForms.
    /// </summary>
    public static class SupportUnitNavigation
    {
        // Byte offset inside the unit struct where the support pointer lives.
        // Same across FE6/FE7/FE8 (UnitForm.ChangeSupportPointer hardcodes it).
        public const uint SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT = 44;

        /// <summary>
        /// Resolve the 0-based unit-table index of the unit whose <c>+44</c> support
        /// pointer (after normalization) equals <paramref name="supportPointerOrFileOffset"/>.
        /// Accepts both raw <c>0x08xxxxxx</c> GBA pointers and already-normalized
        /// file offsets — the input is run through <see cref="U.toOffset(uint)"/> first.
        /// Returns <c>null</c> if no unit points at this support entry, mirroring
        /// the WinForms <c>U.NOT_FOUND</c> return convention.
        /// </summary>
        public static uint? GetUnitIdAtSupportAddr(ROM rom, uint supportPointerOrFileOffset)
        {
            if (rom?.RomInfo == null) return null;
            uint targetOffset = U.toOffset(supportPointerOrFileOffset);
            if (targetOffset == 0 || targetOffset == U.NOT_FOUND) return null;

            uint unitBase = GetUnitTableBase(rom);
            if (unitBase == 0) return null;
            uint unitSize = rom.RomInfo.unit_datasize;
            if (unitSize == 0) return null;
            uint unitMax = rom.RomInfo.unit_maxcount;
            if (unitMax == 0) unitMax = 0x100; // FE6/7/8 all fit in 0x100; defensive

            for (uint i = 0; i < unitMax; i++)
            {
                uint addr = unitBase + i * unitSize;
                if (!U.isSafetyOffset(addr + SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT + 3, rom))
                    break;
                // p32 dereferences a GBA pointer field (returns file offset).
                uint supportFileOffset = rom.p32(addr + SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT);
                if (supportFileOffset == targetOffset)
                {
                    return i;
                }
            }
            return null;
        }

        /// <summary>
        /// Resolve the support file-offset that the unit with 1-based ID
        /// <paramref name="unitId1Based"/> points at. Returns <c>null</c> for
        /// invalid IDs or when the pointer doesn't resolve to a safe offset.
        /// Mirrors WinForms <c>UnitForm.GetSupportAddrWhereUnitID</c>.
        /// </summary>
        public static uint? GetSupportAddrForUnitId(ROM rom, uint unitId1Based)
        {
            if (rom?.RomInfo == null) return null;
            if (unitId1Based == 0) return null;

            uint unitBase = GetUnitTableBase(rom);
            if (unitBase == 0) return null;
            uint unitSize = rom.RomInfo.unit_datasize;
            if (unitSize == 0) return null;
            uint unitMax = rom.RomInfo.unit_maxcount;
            if (unitMax == 0) unitMax = 0x100;
            // 1-based -> 0-based
            uint i = unitId1Based - 1;
            if (i >= unitMax) return null;

            uint addr = unitBase + i * unitSize;
            if (!U.isSafetyOffset(addr + SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT + 3, rom))
                return null;
            uint supportFileOffset = rom.p32(addr + SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT);
            if (supportFileOffset == 0 || !U.isSafetyOffset(supportFileOffset, rom))
                return null;
            return supportFileOffset;
        }

        /// <summary>
        /// Build the WinForms-style row label for a Support Unit list entry:
        /// <c>"{hex(uid+1)} {GetUnitName(uid+1)}"</c> when the entry is owned,
        /// or <c>"-EMPTY-"</c> when no unit points at <paramref name="supportFileOffset"/>.
        /// Mirrors the second callback of WinForms <c>SupportUnitForm.Init</c> and
        /// <c>SupportUnitFE6Form.Init</c>.
        /// </summary>
        public static string FormatSupportRowLabel(ROM rom, uint supportFileOffset)
        {
            uint? uid = GetUnitIdAtSupportAddr(rom, supportFileOffset);
            if (uid == null)
            {
                return "-EMPTY-";
            }
            uint oneBasedId = uid.Value + 1;
            string name = NameResolver.GetUnitName(oneBasedId) ?? "???";
            return U.ToHexString(oneBasedId) + " " + name;
        }

        /// <summary>
        /// Enumerate support-unit entries in WinForms order, applying the same
        /// owner-keyed continuation rule used by <c>SupportUnitForm.Init</c> /
        /// <c>SupportUnitFE6Form.Init</c>: an entry is included if its first
        /// word/byte is non-zero OR (when zero) if any of the next 4 entries
        /// has an owning unit. Stops at the first window of 5 consecutive
        /// unowned, all-zero rows. Caller decides what to do with each
        /// <c>(addr, ownerUid)</c> pair.
        /// </summary>
        /// <param name="rom">ROM to read.</param>
        /// <param name="blockSize">Per-version support struct size: 24 for FE7/8, 32 for FE6.</param>
        /// <param name="firstFieldByteWidth">
        /// Width of the field used by the WinForms heuristic to detect a
        /// non-empty row: 2 for FE7/8 (u16 at offset 0), 1 for FE6 (u8 at offset 0).
        /// </param>
        /// <param name="maxRows">Hard cap on iteration; safety net against malformed pointers.</param>
        public static IEnumerable<(uint addr, uint? ownerUid)> EnumerateSupportEntries(
            ROM rom,
            uint blockSize,
            int firstFieldByteWidth,
            int maxRows = 0x100)
        {
            if (rom?.RomInfo == null) yield break;
            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) yield break;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) yield break;

            for (int i = 0; i < maxRows; i++)
            {
                uint addr = baseAddr + (uint)i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) yield break;

                bool firstFieldNonzero = firstFieldByteWidth == 1
                    ? rom.u8(addr) != 0
                    : rom.u16(addr) != 0;

                uint? ownerUid = GetUnitIdAtSupportAddr(rom, addr);

                if (!firstFieldNonzero)
                {
                    // WinForms looks ahead 4 more blocks; keep the row if any of
                    // them are owned, otherwise stop.
                    bool hasMoreOwned = ownerUid != null;
                    if (!hasMoreOwned)
                    {
                        for (int n = 1; n <= 4; n++)
                        {
                            uint look = addr + (uint)n * blockSize;
                            if (look + blockSize > (uint)rom.Data.Length) break;
                            if (GetUnitIdAtSupportAddr(rom, look) != null)
                            {
                                hasMoreOwned = true;
                                break;
                            }
                        }
                    }
                    if (!hasMoreOwned) yield break;
                }

                yield return (addr, ownerUid);
            }
        }

        /// <summary>
        /// Resolve the actual unit-table base (file offset) accounting for
        /// FE6's first-entry skip. FE6's unit table at <c>p32(unit_pointer)</c>
        /// has a dummy entry at index 0, so the table effectively starts at
        /// <c>p32(unit_pointer) + unit_datasize</c>. Matches
        /// <c>UnitForm.Init</c>'s <c>ReInit</c> branch for FE6.
        /// </summary>
        internal static uint GetUnitTableBase(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            if (rom.RomInfo.version == 6)
            {
                baseAddr += rom.RomInfo.unit_datasize;
            }
            return baseAddr;
        }
    }
}
