// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Clones a shared per-class movement-cost table and repoints only the
    /// selected class's pointer slot.
    /// </summary>
    public static class MoveCostIndependenceCore
    {
        public const uint FE6TableSize = 54;
        public const uint FE7AndFE8TableSize = 68;
        const uint PointerSize = 4;

        public static uint GetTableSize(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            return rom.RomInfo.version switch
            {
                6 => FE6TableSize,
                7 or 8 => FE7AndFE8TableSize,
                _ => 0,
            };
        }

        internal static IReadOnlyList<(uint Offset, uint Length)> GetReferencedTableRanges(ROM rom)
        {
            uint size = GetTableSize(rom);
            if (size == 0 || rom.Data == null) return Array.Empty<(uint, uint)>();

            uint firstSlot = rom.RomInfo.version == 6 ? 52u : 56u;
            uint slotCount = rom.RomInfo.version == 6 ? 4u : 6u;
            uint stride = rom.RomInfo.class_datasize;
            uint classPointer = rom.RomInfo.class_pointer;
            if (stride < firstSlot + slotCount * PointerSize
                || !U.isSafetyOffset(classPointer, rom)
                || (ulong)classPointer + PointerSize > (uint)rom.Data.Length)
                return Array.Empty<(uint, uint)>();

            uint pointer = rom.u32(classPointer);
            if (!U.isSafetyPointer(pointer)) return Array.Empty<(uint, uint)>();
            uint classBase = U.toOffset(pointer);
            if (!U.isSafetyOffset(classBase, rom)) return Array.Empty<(uint, uint)>();

            var ranges = new List<(uint Offset, uint Length)>();
            for (uint id = 0; id <= 0xFF; id++)
            {
                ulong record = (ulong)classBase + id * (ulong)stride;
                if (record + stride > (uint)rom.Data.Length) break;
                uint address = (uint)record;
                if (id > 0 && rom.u8(address + 4) == 0) break;
                for (uint slot = 0; slot < slotCount; slot++)
                {
                    uint tablePointer = rom.u32(address + firstSlot + slot * PointerSize);
                    if (IsSafeTablePointer(rom, tablePointer, size))
                        ranges.Add((U.toOffset(tablePointer), size));
                }
            }
            return ranges;
        }

        internal static uint GetOverlapEnd(
            IReadOnlyList<(uint Offset, uint Length)> ranges, uint offset, uint length)
        {
            uint end = 0;
            foreach (var range in ranges)
            {
                uint rangeEnd = range.Offset + range.Length;
                if (offset < rangeEnd && (ulong)offset + length > range.Offset)
                    end = Math.Max(end, rangeEnd);
            }
            return end;
        }

        /// <summary>
        /// Returns true only when a safe class-specific table is shared by
        /// another in-range class record.
        /// </summary>
        public static bool IsTableShared(ROM rom, uint classAddr, uint pointerSlot, uint classCount)
        {
            if (!TryGetClassTable(rom, classAddr, pointerSlot, classCount, out uint classBase,
                    out uint classStride, out uint slotOffset, out uint selectedClass))
                return false;

            uint tableSize = GetTableSize(rom);
            uint currentPointer = rom.u32(pointerSlot);
            if (!IsSafeTablePointer(rom, currentPointer, tableSize)) return false;

            for (uint classId = 0; classId < classCount; classId++)
            {
                if (classId == selectedClass) continue;
                ulong otherSlot64 = (ulong)classBase + (ulong)classId * classStride + slotOffset;
                if (otherSlot64 + PointerSize > (uint)rom.Data.Length) break;
                if (rom.u32((uint)otherSlot64) == currentPointer) return true;
            }
            return false;
        }

        /// <summary>
        /// Appends an exact-size copy of the shared cost block and repoints
        /// only the selected class. The caller owns one ambient undo scope.
        /// </summary>
        public static uint MakeIndependent(ROM rom, uint classAddr, uint pointerSlot,
            uint classCount, Undo.UndoData undoData)
        {
            uint tableSize = GetTableSize(rom);
            if (tableSize == 0 || !IsTableShared(rom, classAddr, pointerSlot, classCount))
                return U.NOT_FOUND;

            uint source = U.toOffset(rom.u32(pointerSlot));
            if (!IsSafeTableOffset(rom, source, tableSize)) return U.NOT_FOUND;
            byte[] clone = rom.getBinaryData(source, tableSize);

            Undo.UndoData? ambientUndo = ROM.GetAmbientUndoData();
            if (ambientUndo != null && undoData != null && !ReferenceEquals(undoData, ambientUndo))
                return U.NOT_FOUND;

            uint newOffset;
            if (CoreState.AppendBinaryData != null && (undoData != null || ambientUndo != null))
            {
                Undo.UndoData allocatorUndo = ambientUndo != null ? null! : undoData!;
                newOffset = CoreState.AppendBinaryData(clone, allocatorUndo);
            }
            else
            {
                newOffset = rom.FindFreeSpace((uint)(rom.Data.Length / 2), tableSize);
                if (newOffset == U.NOT_FOUND)
                    newOffset = rom.FindFreeSpace(0x100u, tableSize);
                if (newOffset == U.NOT_FOUND) return U.NOT_FOUND;
                if (ambientUndo == null && undoData != null)
                    rom.write_range(newOffset, clone, undoData);
                else
                    rom.write_range(newOffset, clone);
            }

            // An allocator failure or invalid result must never alter the
            // class pointer. The allocation, if any, remains undoable.
            if (!IsSafeTableOffset(rom, newOffset, tableSize)) return U.NOT_FOUND;

            if (ReferenceEquals(undoData, ambientUndo))
                rom.write_p32(pointerSlot, newOffset);
            else if (undoData != null)
                rom.write_p32(pointerSlot, newOffset, undoData);
            else
                rom.write_p32(pointerSlot, newOffset);

            return newOffset;
        }

        static bool TryGetClassTable(ROM rom, uint classAddr, uint pointerSlot, uint classCount,
            out uint classBase, out uint classStride, out uint slotOffset, out uint selectedClass)
        {
            classBase = classStride = slotOffset = selectedClass = 0;
            if (rom?.RomInfo == null || rom.Data == null || classCount == 0) return false;

            classStride = rom.RomInfo.class_datasize;
            uint classPointerSlot = rom.RomInfo.class_pointer;
            if (classStride == 0 || !U.isSafetyOffset(classPointerSlot, rom)
                || (ulong)classPointerSlot + PointerSize > (uint)rom.Data.Length)
                return false;

            uint rawClassBase = rom.u32(classPointerSlot);
            if (!U.isSafetyPointer(rawClassBase)) return false;
            classBase = U.toOffset(rawClassBase);
            if (!U.isSafetyOffset(classBase, rom) || classAddr < classBase) return false;

            uint relativeClassAddr = classAddr - classBase;
            if (relativeClassAddr % classStride != 0) return false;
            selectedClass = relativeClassAddr / classStride;
            if (selectedClass >= classCount) return false;

            if ((ulong)classAddr + classStride > (uint)rom.Data.Length
                || pointerSlot < classAddr
                || (ulong)pointerSlot + PointerSize > (ulong)classAddr + classStride
                || (ulong)pointerSlot + PointerSize > (uint)rom.Data.Length)
                return false;

            slotOffset = pointerSlot - classAddr;
            return true;
        }

        static bool IsSafeTablePointer(ROM rom, uint pointer, uint tableSize)
        {
            return U.isSafetyPointer(pointer)
                && IsSafeTableOffset(rom, U.toOffset(pointer), tableSize);
        }

        static bool IsSafeTableOffset(ROM rom, uint offset, uint tableSize)
        {
            return rom?.Data != null
                && U.isSafetyOffset(offset, rom)
                && (ulong)offset + tableSize <= (uint)rom.Data.Length;
        }
    }
}
