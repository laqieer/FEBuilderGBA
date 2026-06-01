using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for the item editors' "new-alloc" buttons
    /// (StatBooster P12 / Effectiveness P16) — mirrors WinForms
    /// <c>InputFormRef.AllocEvent</c> (<c>InputFormRef.cs:3303-3330</c> for the
    /// templates + <c>:3389-3395</c> for
    /// <c>addr = AppendBinaryData(...); U.ForceUpdate(src, U.toPointer(addr))</c>).
    ///
    /// Used by the Avalonia <c>ItemEditorViewModel</c> / <c>ItemFE6ViewModel</c>
    /// new-alloc handlers (#831). The original WinForms forms already wire these
    /// via the convention-based <c>L_12_NEWALLOC_ITEMSTATBOOSTER</c> /
    /// <c>L_16_NEWALLOC_EFFECTIVENESS</c> links; this Core helper lets the
    /// headless/Avalonia head reuse the EXACT same template + alloc + repoint
    /// path through the wired <see cref="CoreState.AppendBinaryData"/> seam
    /// (#796) without depending on WinForms / <c>InputFormRef</c>.
    /// </summary>
    public static class ItemAllocCore
    {
        // Item record sub-data pointer offsets (shared by all ROM versions).
        const uint STATBOOSTER_PTR_OFFSET = 12;   // P12
        const uint EFFECTIVENESS_PTR_OFFSET = 16;  // P16

        /// <summary>
        /// WF <c>ITEMSTATBOOSTER</c> default template
        /// (<c>InputFormRef.cs:3309-3310</c>): a 20-byte block (a comfortably
        /// safe size) with <c>[1] = 5</c> (HP+5) and every other byte 0.
        /// </summary>
        public static byte[] BuildStatBoosterTemplate()
        {
            // 面倒なので、安全圏の20確保します — 20 bytes, [1]=5 (HP+5), rest 0.
            byte[] alloc = new byte[20];
            alloc[1] = 5;
            return alloc;
        }

        /// <summary>
        /// WF <c>EFFECTIVENESS</c> default template
        /// (<c>InputFormRef.cs:3317-3329</c>). The shape is patch-conditional:
        /// <list type="bullet">
        /// <item><paramref name="skillSystemsRework"/> == true (FE8U
        /// SkillSystems 特効リワーク): <c>FillArray(12, 0)</c> then
        /// <c>[1]=6, [2]=1</c> (armor), <c>[5]=6, [6]=2</c> (cavalry).</item>
        /// <item>otherwise (classic effectiveness list): <c>FillArray(12, 1)</c>
        /// then <c>[11]=0</c> — 12 bytes all <c>0x01</c> except the last byte,
        /// which terminates the class-ID list.</item>
        /// </list>
        /// The Rework variant selection mirrors WF
        /// <c>PatchUtil.SearchClassType() == SkillSystems_Rework</c> (the
        /// Avalonia head supplies this via
        /// <c>PatchDetectionService.SkillSystemsClassTypeRework</c>).
        /// </summary>
        public static byte[] BuildEffectivenessTemplate(bool skillSystemsRework)
        {
            byte[] alloc;
            if (skillSystemsRework)
            {
                // SkillSystemsによる 特効リワーク — zero-filled, then seeded.
                alloc = U.FillArray(12, 0);
                alloc[1] = 6;
                alloc[2] = 1; // アーマー (armor)
                alloc[5] = 6;
                alloc[6] = 2; // 騎兵 (cavalry)
            }
            else
            {
                // Classic zero-terminated class-ID list: all 0x01 except [11]=0.
                alloc = U.FillArray(12, 1);
                alloc[11] = 0;
            }
            return alloc;
        }

        /// <summary>
        /// Allocate a fresh StatBooster (P12) block for the item at
        /// <paramref name="itemAddr"/> and write its pointer into the item's
        /// <c>addr+12</c> slot — mirrors WF <c>AllocEvent("ITEMSTATBOOSTER")</c>.
        /// No-clobber: if the slot already holds a non-zero pointer this is a
        /// no-op (returns <see cref="U.NOT_FOUND"/>) so an existing block is
        /// never orphaned. The block-write and the pointer-write share the
        /// ambient undo scope (open via <see cref="ROM.BeginUndoScope"/>), so a
        /// single rollback reverts both (the slot returns to 0).
        /// </summary>
        /// <returns>The new block's ROM offset, or <see cref="U.NOT_FOUND"/> on
        /// failure / already-allocated.</returns>
        public static uint AllocStatBonuses(ROM rom, uint itemAddr, Undo.UndoData? undodata)
            => Alloc(rom, itemAddr, STATBOOSTER_PTR_OFFSET, BuildStatBoosterTemplate(), undodata);

        /// <summary>
        /// Allocate a fresh Effectiveness (P16) block for the item at
        /// <paramref name="itemAddr"/> and write its pointer into the item's
        /// <c>addr+16</c> slot — mirrors WF <c>AllocEvent("EFFECTIVENESS")</c>.
        /// <paramref name="skillSystemsRework"/> selects the template variant
        /// (see <see cref="BuildEffectivenessTemplate"/>). No-clobber + shared
        /// ambient undo scope, exactly as <see cref="AllocStatBonuses"/>.
        /// </summary>
        public static uint AllocEffectiveness(ROM rom, uint itemAddr, bool skillSystemsRework, Undo.UndoData? undodata)
            => Alloc(rom, itemAddr, EFFECTIVENESS_PTR_OFFSET, BuildEffectivenessTemplate(skillSystemsRework), undodata);

        /// <summary>
        /// Shared alloc + repoint core. Validates the item record + pointer
        /// slot, no-clobbers a non-zero slot, allocates the template via the
        /// wired <see cref="CoreState.AppendBinaryData"/> seam (production) or a
        /// direct FindFreeSpace fallback (headless tests), then repoints the
        /// slot via <see cref="ROM.write_p32(uint, uint)"/> — which applies
        /// <see cref="U.toPointer(uint)"/> internally, mirroring WF
        /// <c>U.ForceUpdate(src, U.toPointer(addr))</c>.
        /// </summary>
        static uint Alloc(ROM rom, uint itemAddr, uint slotOffset, byte[] template, Undo.UndoData? undodata)
        {
            if (rom == null || rom.RomInfo == null || template == null) return U.NOT_FOUND;

            // The pointer slot (itemAddr + slotOffset) must be a real, in-bounds
            // 4-byte slot. Refuse header / low / out-of-range targets so a bad
            // CurrentAddr can never corrupt the ROM header under undo.
            uint slotAddr = itemAddr + slotOffset;
            if (!U.isSafetyOffset(itemAddr, rom)) return U.NOT_FOUND;
            if (slotAddr + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;

            // No-clobber: an existing non-zero pointer means a block is already
            // allocated — do nothing rather than orphan it (mirrors the WF
            // visibility gate, which hides the button once the field is set).
            if (rom.u32(slotAddr) != 0) return U.NOT_FOUND;

            // Allocate via the AppendBinaryData seam if wired (production /
            // Avalonia + CLI via CoreState.WireHeadlessAppendBinaryData), else
            // fall back to a direct FindFreeSpace + write_range path (headless
            // tests). Both paths capture undo through the ambient scope opened
            // by BeginUndoScope. Mirrors MapExitPointCore.NewAlloc.
            uint needsize = (uint)template.Length;
            uint newaddr;
            if (CoreState.AppendBinaryData != null && undodata != null)
            {
                newaddr = CoreState.AppendBinaryData(template, undodata);
            }
            else
            {
                uint searchStart = (uint)(rom.Data.Length / 2);
                newaddr = rom.FindFreeSpace(searchStart, needsize);
                if (newaddr == U.NOT_FOUND)
                {
                    newaddr = rom.FindFreeSpace(0x100u, needsize);
                }
                if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;
                rom.write_range(newaddr, template);
            }
            if (newaddr == U.NOT_FOUND || newaddr == 0) return U.NOT_FOUND;

            // Repoint the item-field slot. write_p32 applies U.toPointer(addr)
            // internally (raw OFFSET → GBA pointer), mirroring WF
            // U.ForceUpdate(src, U.toPointer(addr)). Dispatch on the ambient
            // state so we never record the same UndoPostion twice (the explicit
            // undodata IS the ambient scope's UndoData — same instance). Mirrors
            // MapExitPointCore.NewAlloc.
            Undo.UndoData? ambient = ROM.GetAmbientUndoData();
            if (undodata == null || ReferenceEquals(undodata, ambient))
            {
                rom.write_p32(slotAddr, newaddr);
            }
            else
            {
                rom.write_p32(slotAddr, newaddr, undodata);
            }
            return newaddr;
        }
    }
}
