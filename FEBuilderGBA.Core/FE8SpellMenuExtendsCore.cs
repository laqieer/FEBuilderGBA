// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side read/write logic for the FE8 SkillSystems spell-menu (Gaiden-style
// spell list) patch. Mirrors WinForms FE8SpellMenuExtendsForm's master/detail
// model so the Avalonia editor and CLI share one implementation (issue #1167).
//
// Data model (mirrors WF):
//   - assignLevelUpP  : a u32 pointer slot resolved by FE8SpellMenuPatchScanner.
//   - *assignLevelUpP : the PER-UNIT pointer-table base. Slot at base + 4*unit
//                       is a u32 GBA pointer to unit `unit`'s spell-list block.
//   - spell-list block: a 0x0000-terminated u16 array. Each u16 entry:
//                         B0 (u8 @+0) = level | promoted-flag (0x80)
//                         B1 (u8 @+1) = item / spell id
//
// MakeB0 / SplitB0 mirror WF FE8SpellMenuExtendsForm.MakeB0 and the
// N1_B0_ValueChanged split exactly (Level = B0 & 0x7F, Promoted = B0 & 0x80).
//
// No CoreState reads. All entry points take an explicit ROM parameter.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    public static class FE8SpellMenuExtendsCore
    {
        public const uint MASTER_BLOCK_SIZE = 4;   // per-unit u32 pointer slot
        public const uint N1_BLOCK_SIZE = 2;       // u16 [B0|B1] entry
        public const uint UNIT_MAX_ROWS = 0xFF;    // WF read-max predicate (i < 0xFF)
        public const uint N1_MAX_ROWS = 1024;       // safety cap for sub-list walk
        public const byte PROMOTED_BIT = 0x80;
        public const byte LEVEL_MASK = 0x7F;

        /// <summary>Compose a B0 byte from a level (0..0x7F) + promoted flag.
        /// Faithful to WF FE8SpellMenuExtendsForm.MakeB0.</summary>
        public static byte MakeB0(uint level, bool promoted)
        {
            byte b0 = (byte)(level & LEVEL_MASK);
            if (promoted)
            {
                b0 |= PROMOTED_BIT;
            }
            return b0;
        }

        /// <summary>Split a B0 byte into level (B0 &amp; 0x7F) and promoted
        /// (B0 &amp; 0x80). Faithful to WF N1_B0_ValueChanged.</summary>
        public static void SplitB0(uint b0, out uint level, out bool promoted)
        {
            level = b0 & LEVEL_MASK;
            promoted = (b0 & PROMOTED_BIT) == PROMOTED_BIT;
        }

        /// <summary>
        /// Resolve the per-unit pointer-table base (= *assignLevelUpP). Returns
        /// NOT_FOUND when the slot or its dereference is unsafe.
        /// </summary>
        public static uint GetUnitTableBase(ROM rom, uint assignLevelUpP)
        {
            if (rom == null || assignLevelUpP == U.NOT_FOUND) return U.NOT_FOUND;
            if (!U.isSafetyOffset(assignLevelUpP + 3, rom)) return U.NOT_FOUND;
            uint baseAddr = rom.p32(assignLevelUpP);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            return baseAddr;
        }

        /// <summary>Address of unit <paramref name="unitId"/>'s u32 pointer slot
        /// (unitTableBase + 4*unitId).</summary>
        public static uint GetUnitSlotAddr(uint unitTableBase, uint unitId)
        {
            return unitTableBase + unitId * MASTER_BLOCK_SIZE;
        }

        /// <summary>
        /// Read the spell-list block for one unit. Returns the list of (b0, b1)
        /// pairs walking the 0x0000-terminated u16 array starting at
        /// <paramref name="listBase"/>. Stops on a 0x0000 / 0xFFFF u16, an unsafe
        /// offset, or N1_MAX_ROWS — never throws.
        /// </summary>
        public static List<(uint addr, uint b0, uint b1)> ReadSpellList(ROM rom, uint listBase)
        {
            var result = new List<(uint, uint, uint)>();
            if (rom == null) return result;
            if (!U.isSafetyOffset(listBase, rom)) return result;

            uint cursor = listBase;
            for (uint n = 0; n < N1_MAX_ROWS; n++, cursor += N1_BLOCK_SIZE)
            {
                if (!U.isSafetyOffset(cursor + 1, rom)) break;
                uint pair = rom.u16(cursor);
                if (pair == 0x0000 || pair == 0xFFFF) break;
                uint b0 = rom.u8(cursor + 0);
                uint b1 = rom.u8(cursor + 1);
                result.Add((cursor, b0, b1));
            }
            return result;
        }

        /// <summary>
        /// Write a single N1 (B0,B1) entry in place. Returns false (no write)
        /// when the address is unsafe.
        /// </summary>
        public static bool WriteN1Entry(ROM rom, uint entryAddr, uint b0, uint b1)
        {
            if (rom == null) return false;
            if (!U.isSafetyOffset(entryAddr + 1, rom)) return false;
            rom.write_u8(entryAddr + 0, b0);
            rom.write_u8(entryAddr + 1, b1);
            return true;
        }

        /// <summary>
        /// Write the per-unit pointer slot to point at <paramref name="listOffset"/>.
        /// Mirrors WF WriteButton_Click (write_p32). Returns false on unsafe slot.
        /// </summary>
        public static bool WriteUnitPointer(ROM rom, uint unitTableBase, uint unitId, uint listOffset)
        {
            if (rom == null) return false;
            uint slot = GetUnitSlotAddr(unitTableBase, unitId);
            if (!U.isSafetyOffset(slot + 3, rom)) return false;
            rom.write_p32(slot, U.toOffset(listOffset));
            return true;
        }

        /// <summary>
        /// Expand a unit's spell list to <paramref name="newCount"/> entries
        /// (each B0|B1). A fresh block of (newCount * 2) + 2 bytes is allocated
        /// in free space of the PASSED <paramref name="rom"/> (the +2 is the
        /// 0x0000 terminator), the existing entries are copied, and the unit's
        /// pointer slot is repointed at the new block. Mirrors WF
        /// N1_InputFormRef_AddressListExpandsEvent (allocate a new
        /// 0x0000-terminated block + repoint the unit slot).
        ///
        /// All allocation and writes go to the EXPLICIT <paramref name="rom"/>
        /// (NOT CoreState.ROM) and are recorded into the EXPLICIT
        /// <paramref name="undodata"/> — honouring this class's "no CoreState"
        /// contract. The View opens the UndoService scope and hands its active
        /// UndoData here; pass null to write without undo capture.
        /// </summary>
        /// <returns>The new block OFFSET on success, or NOT_FOUND on failure
        /// (no ROM, unsafe slot, zero/oversized count, or free-space exhaustion).</returns>
        public static uint ExpandSpellList(ROM rom, uint unitTableBase, uint unitId, uint newCount,
            Undo.UndoData undodata)
        {
            if (rom == null || rom.Data == null) return U.NOT_FOUND;
            uint slot = GetUnitSlotAddr(unitTableBase, unitId);
            if (!U.isSafetyOffset(slot + 3, rom)) return U.NOT_FOUND;
            if (newCount == 0 || newCount > N1_MAX_ROWS) return U.NOT_FOUND;

            // Read the existing entries so the expand preserves them.
            uint oldListBase = rom.p32(slot);
            var existing = U.isSafetyOffset(oldListBase, rom)
                ? ReadSpellList(rom, oldListBase)
                : new List<(uint addr, uint b0, uint b1)>();

            // Build the new block: newCount entries (copied where available,
            // 0x0001/0x00 default otherwise) + a 0x0000 u16 terminator.
            int blockBytes = (int)(newCount * N1_BLOCK_SIZE + N1_BLOCK_SIZE);
            byte[] buffer = new byte[blockBytes];
            for (uint n = 0; n < newCount; n++)
            {
                int off = (int)(n * N1_BLOCK_SIZE);
                if (n < existing.Count)
                {
                    buffer[off + 0] = (byte)existing[(int)n].b0;
                    buffer[off + 1] = (byte)existing[(int)n].b1;
                }
                else
                {
                    // New rows default to level 1, skill 0 (non-zero B0 so the
                    // row is not mistaken for the terminator at read time).
                    buffer[off + 0] = 0x01;
                    buffer[off + 1] = 0x00;
                }
            }
            // Trailing two bytes already 0x00 0x00 → the terminator.

            // Allocate against the PASSED rom (upper half, then lower half,
            // then resize the tail) — a CoreState-free re-implementation of the
            // RecycleAddress freespace fallback.
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint newAddr = rom.FindFreeSpace(searchStart, (uint)buffer.Length);
            if (newAddr == U.NOT_FOUND)
            {
                newAddr = rom.FindFreeSpace(0x100u, (uint)buffer.Length);
            }
            if (newAddr == U.NOT_FOUND)
            {
                uint newSize = U.Padding4((uint)rom.Data.Length + (uint)buffer.Length);
                if (newSize > 0x02000000u) return U.NOT_FOUND;
                uint tail = (uint)rom.Data.Length;
                if (!rom.write_resize_data(newSize)) return U.NOT_FOUND;
                newAddr = tail;
            }

            if (undodata != null)
            {
                rom.write_range(newAddr, buffer, undodata);
                rom.write_p32(slot, newAddr, undodata);
            }
            else
            {
                rom.write_range(newAddr, buffer);
                rom.write_p32(slot, newAddr);
            }
            return newAddr;
        }

        /// <summary>
        /// Export the entire spell-menu table to a TSV file: one line per unit,
        /// tab-separated, the per-unit list OFFSET followed by [b0 b1] pairs.
        ///   unit_list_offset_hex   [b0_hex b1_hex]*
        /// Mirrors the WF MakeAllDataLength enumeration shape (per-unit blocks).
        /// </summary>
        public static bool ExportAllData(ROM rom, uint assignLevelUpP, uint unitCount, string filename)
        {
            if (rom == null || rom.Data == null) return false;
            if (string.IsNullOrEmpty(filename)) return false;
            uint unitTableBase = GetUnitTableBase(rom, assignLevelUpP);
            if (unitTableBase == U.NOT_FOUND) return false;

            try
            {
                var lines = new List<string>();
                for (uint i = 0; i < unitCount; i++)
                {
                    uint slot = GetUnitSlotAddr(unitTableBase, i);
                    if (!U.isSafetyOffset(slot + 3, rom)) break;

                    var sb = new StringBuilder();
                    uint listOffset = rom.p32(slot);
                    sb.Append(U.ToHexString(listOffset));

                    if (U.isSafetyOffset(listOffset, rom))
                    {
                        foreach (var (addr, b0, b1) in ReadSpellList(rom, listOffset))
                        {
                            sb.Append('\t');
                            sb.Append(U.ToHexString(b0));
                            sb.Append('\t');
                            sb.Append(U.ToHexString(b1));
                        }
                    }
                    lines.Add(sb.ToString());
                }
                File.WriteAllLines(filename, lines);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Import the spell-menu table from a TSV file produced by
        /// <see cref="ExportAllData"/>. Walks each unit's EXISTING referenced
        /// spell-list block and writes the [b0 b1] pairs in place (shared-table
        /// mutation; the per-unit pointer is left untouched). Stops a unit early
        /// if the existing block terminates before the TSV pairs are exhausted.
        /// </summary>
        public static bool ImportAllData(ROM rom, uint assignLevelUpP, uint unitCount, string filename)
        {
            if (rom == null || rom.Data == null) return false;
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename)) return false;
            uint unitTableBase = GetUnitTableBase(rom, assignLevelUpP);
            if (unitTableBase == U.NOT_FOUND) return false;

            try
            {
                string[] lines = File.ReadAllLines(filename);
                if (lines == null) return false;

                for (uint i = 0; i < unitCount; i++)
                {
                    if (i >= lines.Length) break;
                    uint slot = GetUnitSlotAddr(unitTableBase, i);
                    if (!U.isSafetyOffset(slot + 3, rom)) break;

                    string[] sp = lines[i].Split('\t');
                    if (sp.Length < 1) continue;

                    uint listOffset = rom.p32(slot);
                    if (!U.isSafetyOffset(listOffset, rom)) continue;

                    uint cursor = listOffset;
                    int pairCount = (sp.Length - 1) / 2;
                    for (int n = 0; n < pairCount && n < (int)N1_MAX_ROWS; n++, cursor += N1_BLOCK_SIZE)
                    {
                        if (!U.isSafetyOffset(cursor + 1, rom)) break;
                        // Mirror the read terminator: never write past the
                        // existing 0x0000-terminated block.
                        uint pair = rom.u16(cursor);
                        if (pair == 0x0000 || pair == 0xFFFF) break;
                        uint b0 = U.atoh(sp[1 + n * 2 + 0]);
                        uint b1 = U.atoh(sp[1 + n * 2 + 1]);
                        rom.write_u8(cursor + 0, b0);
                        rom.write_u8(cursor + 1, b1);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
