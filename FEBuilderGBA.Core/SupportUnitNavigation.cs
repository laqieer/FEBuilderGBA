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
            // Display hex is 1-based to match WinForms label convention
            // ("01 Eirika").  ResolveUnitTableName takes a 0-based table
            // index (matching Avalonia UnitEditorViewModel.LoadUnitList) so
            // the Support list rows show the same name as the Unit list
            // rows at the same index.
            uint oneBasedDisplay = uid.Value + 1;
            string name = ResolveUnitTableName(rom, uid.Value);
            return U.ToHexString(oneBasedDisplay) + " " + name;
        }

        /// <summary>
        /// Decode the unit name at <paramref name="zeroBasedIndex"/> in the
        /// unit table by reading the u16 text-id at offset 0 of that entry
        /// and decoding via FETextDecode.  Mirrors what
        /// UnitEditorViewModel.LoadUnitList does, so SupportUnit labels stay
        /// in sync with Unit Editor labels.  Returns "???" on decode failure
        /// or invalid input (matching NameResolver.GetUnitName's fallback)
        /// so list rows never render as a bare "06 " trailing-space row.
        /// </summary>
        public static string ResolveUnitTableName(ROM rom, uint zeroBasedIndex)
        {
            if (rom?.RomInfo == null) return "???";
            uint unitBase = GetUnitTableBase(rom);
            if (unitBase == 0) return "???";
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return "???";
            uint entryAddr = unitBase + zeroBasedIndex * dataSize;
            if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
            uint textId = rom.u16(entryAddr);
            // textId 0 is a real condition (unit slot with no name) — match
            // NameResolver.ResolveUnitName which returns "#<id>" in that case.
            if (textId == 0) return $"#{zeroBasedIndex}";
            try
            {
                string name = NameResolver.GetTextById(textId);
                return string.IsNullOrEmpty(name) ? "???" : name;
            }
            catch { return "???"; }
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
        /// Convert a 1-based ROM unit ID (the value stored in event/support/
        /// summon fields) to the file offset of that unit's entry in the unit
        /// table, accounting for FE6's dummy-entry skip via
        /// <see cref="GetUnitTableBase(ROM)"/>. Mirrors the <c>UnitAddrFor</c>
        /// helper in <c>SupportTalkView</c> (#725): the unit table is indexed
        /// with <c>(oneBasedId - 1)</c>, so ID 1 maps to the first LOGICAL unit
        /// (not the next character). This is the 1-based ROM-id →
        /// unit-table-address conversion. Returns <c>0</c> for an invalid ROM,
        /// <c>oneBasedId == 0</c> ("no unit"), a zero data size, or an unsafe
        /// resulting offset. Use this for Jump/Pick navigation so the jump
        /// lands on the same unit whose name
        /// <see cref="ResolveUnitTableName(ROM, uint)"/> (with
        /// <c>oneBasedId - 1</c>) resolves. Fixes the 1-based off-by-one across
        /// the 8 Avalonia editors that previously passed the raw 1-based ID
        /// into <c>base + unitId * dataSize</c> (#937).
        /// </summary>
        public static uint UnitAddrForOneBased(ROM rom, uint oneBasedId)
        {
            if (rom?.RomInfo == null) return 0;
            if (oneBasedId == 0) return 0; // 1-based: 0 is "no unit"
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            uint baseAddr = GetUnitTableBase(rom); // FE6-aware logical base
            if (baseAddr == 0) return 0;
            // 64-bit math so a large user-editable oneBasedId can't wrap the
            // uint multiply, and validate the FULL entry range (start +
            // dataSize), not just the start offset — otherwise Jump/Pick could
            // land on an entry whose tail spills past EOF (#938 review).
            ulong entry = (ulong)baseAddr + (ulong)(oneBasedId - 1) * dataSize;
            if (entry + dataSize > (ulong)rom.Data.Length) return 0;
            uint entryAddr = (uint)entry;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            return entryAddr;
        }

        /// <summary>
        /// Convert a 0-based <c>PickResult.Index</c> (from a Unit-Editor pick)
        /// back to the 1-based ROM unit ID written into the field. Mirrors the
        /// <c>result.Index + 1</c> write-back in <c>SupportTalkView</c> (#725).
        /// Pure conversion, extracted so the Pick half of the #937 fix is
        /// unit-testable without a ROM.
        /// </summary>
        public static uint OneBasedIdFromPickIndex(int zeroBasedIndex) => (uint)zeroBasedIndex + 1;

        // ---- #1149: entry-id resolvers for decomp source-backed writers ----

        /// <summary>
        /// Resolve the 0-based entry id for a support_units record from its ROM address.
        /// Returns <see cref="U.NOT_FOUND"/> on any miss (null rom, null RomInfo, zero pointer,
        /// unsafe base, addr below base, misaligned, out-of-range, or overflows uint).
        /// Mirrors <see cref="MapSettingCore.GetMapIdFromAddr"/> in structure.
        /// </summary>
        /// <param name="rom">ROM to read.</param>
        /// <param name="addr">ROM file offset of the support_units entry.</param>
        /// <param name="blockSize">Per-version support struct byte size (24 for FE7/8, 32 for FE6).</param>
        public static uint GetSupportUnitEntryIdFromAddr(ROM rom, uint addr, uint blockSize)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (addr == U.NOT_FOUND || blockSize == 0) return U.NOT_FOUND;
            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(ptr, rom)) return U.NOT_FOUND;
            if ((ulong)ptr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            if (addr < baseAddr) return U.NOT_FOUND;
            uint delta = addr - baseAddr;
            if (delta % blockSize != 0) return U.NOT_FOUND;
            uint id = delta / blockSize;
            // Sane cap: support tables are always well under 0x1000 entries.
            if (id >= 0x1000) return U.NOT_FOUND;
            // Verify the full row fits in ROM.
            if ((ulong)addr + blockSize > (ulong)rom.Data.Length) return U.NOT_FOUND;
            return id;
        }

        /// <summary>
        /// Resolve the 0-based entry id for a support_attributes record from its ROM address.
        /// Block size is fixed at 8 bytes for all versions. Returns <see cref="U.NOT_FOUND"/> on miss.
        /// </summary>
        public static uint GetSupportAttributeEntryIdFromAddr(ROM rom, uint addr)
        {
            const uint BlockSize = 8;
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (addr == U.NOT_FOUND) return U.NOT_FOUND;
            uint ptr = rom.RomInfo.support_attribute_pointer;
            if (ptr == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(ptr, rom)) return U.NOT_FOUND;
            if ((ulong)ptr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            if (addr < baseAddr) return U.NOT_FOUND;
            uint delta = addr - baseAddr;
            if (delta % BlockSize != 0) return U.NOT_FOUND;
            uint id = delta / BlockSize;
            if (id >= 0x1000) return U.NOT_FOUND;
            if ((ulong)addr + BlockSize > (ulong)rom.Data.Length) return U.NOT_FOUND;
            return id;
        }

        /// <summary>
        /// Resolve the 0-based entry id for a support_talks record from its ROM address.
        /// Returns <see cref="U.NOT_FOUND"/> on miss.
        /// </summary>
        /// <param name="rom">ROM to read.</param>
        /// <param name="addr">ROM file offset of the support_talks entry.</param>
        /// <param name="blockSize">Per-version struct size (16 for FE8/FE6, 20 for FE7).</param>
        public static uint GetSupportTalkEntryIdFromAddr(ROM rom, uint addr, uint blockSize)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (addr == U.NOT_FOUND || blockSize == 0) return U.NOT_FOUND;
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(ptr, rom)) return U.NOT_FOUND;
            if ((ulong)ptr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            if (addr < baseAddr) return U.NOT_FOUND;
            uint delta = addr - baseAddr;
            if (delta % blockSize != 0) return U.NOT_FOUND;
            uint id = delta / blockSize;
            if (id >= 0x1000) return U.NOT_FOUND;
            if ((ulong)addr + blockSize > (ulong)rom.Data.Length) return U.NOT_FOUND;
            return id;
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
