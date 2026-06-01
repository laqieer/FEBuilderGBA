// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper for the SkillAssignment (Class) "Make Selected
    /// Class Independent" button — mirrors WinForms
    /// <c>SkillAssignmentClassSkillSystemForm.IndependenceButton_Click</c>
    /// (<c>SkillAssignmentClassSkillSystemForm.cs:551-595</c>) →
    /// <c>PatchUtil.WriteIndependence</c> (<c>PatchUtil.cs:1506-1524</c>).
    ///
    /// The per-class level-up skill table is referenced by a single pointer
    /// slot at <c>assignLevelUpBase + classId*4</c>. When several classes share
    /// the SAME pointer (i.e. the same physical table), editing one would edit
    /// them all. "Independence" clones the shared table into a fresh free-space
    /// block and repoints ONLY the selected class's slot — deliberately leaving
    /// every other sharing class on the intact original table.
    ///
    /// This is the DELIBERATE inverse of
    /// <see cref="DataExpansionCore.RepointAllReferences"/>: an all-reference
    /// rescan would drag every sharing class onto the clone and orphan the
    /// original, breaking independence. Hence a SINGLE
    /// <see cref="ROM.write_p32(uint, uint)"/> (which applies
    /// <see cref="U.toPointer(uint)"/> internally, mirroring WF
    /// <c>write_p32(writeSettingAddr, addr)</c>).
    ///
    /// Used by the Avalonia <c>SkillAssignmentClassSkillSystemViewModel</c>
    /// (#834). The block-clone (via the wired
    /// <see cref="CoreState.AppendBinaryData"/> seam — #796) and the
    /// pointer-write share the ambient undo scope (opened via
    /// <see cref="ROM.BeginUndoScope"/>), so a single rollback reverts both.
    /// </summary>
    public static class SkillAssignmentIndependenceCore
    {
        // Per-class level-up table entry size (lv|skill, 2 bytes) and pointer
        // slot stride (a 32-bit pointer per class). Mirror WF
        // N1_InputFormRef.BlockSize / the *4 slot stride.
        public const uint LEVELUP_BLOCK_SIZE = 2;
        const uint SLOT_STRIDE = 4;
        const uint LEVELUP_MAX_ROWS = 256;

        /// <summary>
        /// Count the 0x0000/0xFFFF-terminated level-up rows starting at
        /// <paramref name="baseOffset"/>. Mirrors WF N1_InputFormRef's read-max
        /// (<c>SkillAssignmentClassSkillSystemForm.cs:115-122</c>): stop at a
        /// 0x0000 or 0xFFFF pair, or at the end of ROM.
        /// </summary>
        public static uint CountLevelUpRows(ROM rom, uint baseOffset)
        {
            if (rom == null || rom.Data == null) return 0;
            uint count = 0;
            uint cursor = baseOffset;
            for (uint n = 0; n < LEVELUP_MAX_ROWS; n++, cursor += LEVELUP_BLOCK_SIZE)
            {
                if (cursor + LEVELUP_BLOCK_SIZE > (uint)rom.Data.Length) break;
                uint pair = rom.u16(cursor);
                if (pair == 0 || pair == 0xFFFF) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// True when the selected class's level-up pointer is shared with at
        /// least one OTHER class slot — i.e. Independence is meaningful. Mirrors
        /// WF <c>SkillAssignmentClassSkillSystemForm.IsExistsAssignLevelUpPointer</c>
        /// (<c>:531-549</c>): walk every other class slot and compare the raw
        /// GBA pointer to the current one. <paramref name="classCount"/> bounds
        /// the walk (the editor's visible class-list length).
        /// </summary>
        public static bool IsTableShared(ROM rom, uint assignLevelUpBase, uint classId, uint classCount)
        {
            if (rom == null || rom.Data == null) return false;
            uint slot = assignLevelUpBase + classId * SLOT_STRIDE;
            if (slot + SLOT_STRIDE > (uint)rom.Data.Length) return false;
            uint currentGbaPtr = rom.u32(slot);
            if (!U.isSafetyPointer(currentGbaPtr)) return false;
            for (uint i = 0; i < classCount; i++)
            {
                if (i == classId) continue;
                uint otherSlot = assignLevelUpBase + i * SLOT_STRIDE;
                if (otherSlot + SLOT_STRIDE > (uint)rom.Data.Length) continue;
                uint other = rom.u32(otherSlot);
                if (other == currentGbaPtr) return true;
            }
            return false;
        }

        /// <summary>
        /// Clone the selected class's level-up table into a fresh free-space
        /// block and repoint ONLY that class's pointer slot — mirrors WF
        /// <c>IndependenceButton_Click</c> + <c>PatchUtil.WriteIndependence</c>.
        ///
        /// WF guards mirrored exactly:
        /// <list type="bullet">
        /// <item><c>setting = assignLevelUpBase + classId*4</c> must be a safe
        /// offset (<see cref="U.isSafetyOffset(uint, ROM)"/>).</item>
        /// <item>the resolved pointer <c>p = toOffset(u32(setting))</c> must be a
        /// safe offset (WF <c>U.isSafetyOffset(p)</c>).</item>
        /// </list>
        /// The empty-list confirm + the <c>BaseAddress == p</c> equality guard
        /// are enforced by the caller/VM (they depend on the live N1 list state),
        /// matching WF where they sit in <c>IndependenceButton_Click</c> before
        /// <c>WriteIndependence</c>.
        ///
        /// The clone size is <c>(rowCount + 1) * 2</c> — the rows PLUS the
        /// 0x0000 terminator — exactly WF <c>(DataCount+1) * BlockSize</c>. The
        /// bytes are read verbatim via <see cref="ROM.getBinaryData(uint, uint)"/>
        /// then appended via <see cref="CoreState.AppendBinaryData"/> (production)
        /// or a direct FindFreeSpace fallback (headless tests). Both paths
        /// capture undo through the ambient scope.
        /// </summary>
        /// <returns>The new block's ROM offset, or <see cref="U.NOT_FOUND"/> on
        /// failure (out-of-bounds slot, unsafe pointer, or no free space). Never
        /// throws.</returns>
        public static uint MakeIndependent(ROM rom, uint assignLevelUpBase, uint classId, Undo.UndoData undodata)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return U.NOT_FOUND;

            // Guard: the pointer slot must be a real, in-bounds 4-byte slot.
            uint slot = assignLevelUpBase + classId * SLOT_STRIDE;
            if (!U.isSafetyOffset(slot, rom)) return U.NOT_FOUND;
            if (slot + SLOT_STRIDE > (uint)rom.Data.Length) return U.NOT_FOUND;

            // Resolve the current GBA pointer → offset; refuse a bad target so a
            // corrupt slot can never seed a header/low clone (WF:
            // U.isSafetyOffset(p)).
            uint gbaPtr = rom.u32(slot);
            uint p = U.toOffset(gbaPtr);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            // Clone (rowCount + 1) * 2 bytes (rows + terminator) verbatim. WF:
            // getBinaryData(p, (DataCount+1)*BlockSize).
            uint rowCount = CountLevelUpRows(rom, p);
            uint cloneSize = (rowCount + 1) * LEVELUP_BLOCK_SIZE;
            if (p + cloneSize > (uint)rom.Data.Length) return U.NOT_FOUND;
            byte[] clone = rom.getBinaryData(p, cloneSize);

            // Allocate via the AppendBinaryData seam if wired (production /
            // Avalonia + CLI via CoreState.WireHeadlessAppendBinaryData), else
            // fall back to a direct FindFreeSpace + write_range path (headless
            // tests). Both capture undo through the ambient scope. Mirrors
            // ItemAllocCore.Alloc.
            uint newaddr;
            if (CoreState.AppendBinaryData != null && undodata != null)
            {
                newaddr = CoreState.AppendBinaryData(clone, undodata);
            }
            else
            {
                uint searchStart = (uint)(rom.Data.Length / 2);
                newaddr = rom.FindFreeSpace(searchStart, cloneSize);
                if (newaddr == U.NOT_FOUND)
                {
                    newaddr = rom.FindFreeSpace(0x100u, cloneSize);
                }
                if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;
                rom.write_range(newaddr, clone);
            }
            if (newaddr == U.NOT_FOUND || newaddr == 0) return U.NOT_FOUND;

            // Repoint ONLY the selected class's slot. write_p32 applies
            // U.toPointer(addr) internally (raw OFFSET → GBA pointer), mirroring
            // WF write_p32(writeSettingAddr, addr). Dispatch on the ambient state
            // so we never record the same UndoPostion twice (the explicit
            // undodata IS the ambient scope's UndoData — same instance). Mirrors
            // ItemAllocCore.Alloc. CRITICALLY a SINGLE slot — NOT
            // RepointAllReferences — so other sharing classes stay on the
            // intact original table.
            Undo.UndoData ambient = ROM.GetAmbientUndoData();
            if (undodata == null || ReferenceEquals(undodata, ambient))
            {
                rom.write_p32(slot, newaddr);
            }
            else
            {
                rom.write_p32(slot, newaddr, undodata);
            }
            return newaddr;
        }
    }
}
