using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY, never-throws cross-platform helper that resolves the unit-action
    /// function-pointer table for the Unit Action Pointer editor (Avalonia
    /// <c>UnitActionPointerViewModel</c> + <c>ListParityHelper.BuildUnitActionPointerList</c>).
    ///
    /// Single source of truth that mirrors WinForms <c>UnitActionPointerForm</c>
    /// (<c>SearchActionPointer</c> + <c>Init</c>) so the editor behaves identically on
    /// <b>UnitActionRework</b>-patched ROMs (#1415):
    /// <list type="bullet">
    ///   <item><b>Base resolution</b> (<see cref="ResolveBaseAddress"/>): non-rework =
    ///   <c>p32(RomInfo.unitaction_function_pointer)</c>; rework = <c>p32</c> of the relocated
    ///   slot grepped from <c>ApplyAction.bin</c> — delegated VERBATIM to the existing
    ///   <see cref="RebuildProducerCore.SearchActionPointer"/> port.</item>
    ///   <item><b>Entry validity</b> (<see cref="IsDataExists"/>): non-rework =
    ///   <c>isSafetyPointer(u32)</c>; rework = accept <c>a==0</c> (NULL routine), reject
    ///   <see cref="U.NOT_FOUND"/>, accept <c>isSafetyPointer(a &amp; 0x0FFFFFFF)</c> (the
    ///   high-nibble <c>abForcedYeild</c> flag is masked off before the safety check).</item>
    ///   <item><b>Action-id origin</b> (<see cref="ResolveActionId"/>): non-rework ids start
    ///   at 1 (id <c>0</c> is out of range); rework ids are 0-based.</item>
    /// </list>
    /// Every method guards a null ROM and returns the safe default — no exceptions.
    /// </summary>
    public static class UnitActionPointerCore
    {
        /// <summary>True when the UnitActionRework patch is installed in <paramref name="rom"/>.</summary>
        public static bool IsRework(ROM rom)
        {
            return PatchDetection.SearchUnitActionReworkPatch(rom);
        }

        /// <summary>
        /// The unit-action function-pointer table <b>slot</b> (a pointer, not the table base).
        /// Non-rework: <c>RomInfo.unitaction_function_pointer</c>. Rework: the relocated slot
        /// grepped from <c>config/patch2/&lt;ver&gt;/UnitActionRework/.../ApplyAction.bin</c>
        /// (BaseDirectory/missing-file safe — returns 0 when unresolved). Verbatim WF port
        /// reused from <see cref="RebuildProducerCore.SearchActionPointer"/>.
        /// </summary>
        public static uint ResolveBaseSlot(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            return RebuildProducerCore.SearchActionPointer(rom);
        }

        /// <summary>
        /// The dereferenced table base address (offset). Returns 0 when the slot is unresolved
        /// or the dereferenced pointer is not a safe offset.
        /// WF: <c>ReInitPointer(SearchActionPointer())</c> -> <c>BaseAddress = p32(slot)</c>.
        /// </summary>
        public static uint ResolveBaseAddress(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            uint slot = ResolveBaseSlot(rom);
            if (slot == 0) return 0;
            uint slotOffset = U.toOffset(slot);
            // Guard the 4-byte pointer read at the slot.
            if (!U.isSafetyOffset(slotOffset + 3, rom)) return 0;
            uint baseAddr = rom.p32(slotOffset);
            return U.isSafetyOffset(baseAddr, rom) ? baseAddr : 0;
        }

        /// <summary>
        /// Verbatim WF <c>UnitActionPointerForm.Init</c> IsDataExists predicate (block 4),
        /// gated on <paramref name="isRework"/>.
        /// Non-rework: <c>isSafetyPointer(u32(addr))</c>. Rework: reject <see cref="U.NOT_FOUND"/>,
        /// accept <c>0</c>, accept <c>isSafetyPointer(u32 &amp; 0x0FFFFFFF)</c>.
        /// </summary>
        public static bool IsDataExists(ROM rom, uint addr, bool isRework)
        {
            if (rom == null) return false;
            if (addr + 4 > (uint)rom.Data.Length) return false;
            uint a = rom.u32(addr);
            if (isRework == false)
            {//not reworked
                return U.isSafetyPointer(a, rom);
            }
            //reworked
            if (a == U.NOT_FOUND) return false;
            if (a == 0) return true;
            return U.isSafetyPointer(a & 0x0FFFFFFF, rom);
        }

        /// <summary>
        /// Action id for the zero-based list index <paramref name="i"/>, mirroring WF: non-rework
        /// ids start at 1 (<c>i + 1</c>; id 0 is out of range), rework ids are 0-based (<c>i</c>).
        /// </summary>
        public static uint ResolveActionId(int i, bool isRework)
        {
            uint id = (uint)i;
            if (isRework == false)
            {
                id += 1; // 0 is out of range
            }
            return id;
        }

        /// <summary>
        /// Recover the action id of an entry at <paramref name="addr"/> from its offset within the
        /// table (<paramref name="baseAddr"/>), applying the same origin rule as
        /// <see cref="ResolveActionId(int, bool)"/>. Returns 0 when <paramref name="addr"/> is below
        /// the base (non-rework: id 0 then signals "out of range").
        /// </summary>
        public static uint ResolveActionIdFromAddr(uint addr, uint baseAddr, bool isRework)
        {
            if (baseAddr == 0 || addr < baseAddr) return 0;
            int index = (int)((addr - baseAddr) / 4);
            return ResolveActionId(index, isRework);
        }
    }
}
