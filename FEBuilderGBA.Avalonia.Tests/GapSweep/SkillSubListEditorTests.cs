// SPDX-License-Identifier: GPL-3.0-or-later
// #930 — SkillConfig FE8N Ver2/Ver3 editable sub-list tabs.
//
// Proves the reusable SkillSubListEditorViewModel (WU1) edits a per-skill
// null-terminated 1-byte-ID array referenced through a 32-bit pointer SLOT,
// consuming the merged NullTerminatedByteListCore (#926/#928) via the single
// WriteByteList(slot, ids, undo) path, AND that the Ver2 (4 tabs) / Ver3
// (5 tabs) host views instantiate + Load their embedded editors.
//
// ROM source: the repo does NOT commit real .gba ROMs (the roms/ folder is
// empty locally and in CI). The Ver2 GUI screenshot uses roms/FE8J_skill.gba
// manually; here every test builds a deterministic SYNTHETIC FE8U ROM (header
// signature BE8E01) with an explicit 0xFF free-space region for the relocate,
// exactly like the existing MapExitPoint / SkillConfigFE8NVer2 suites. This
// exercises the full WriteByteList path without depending on an external ROM.
//
// Marked [Collection("SharedState")] because the tests mutate CoreState.ROM /
// CoreState.Undo to plant the synthetic ROM.
using System;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillSubListEditorTests
{
    // The 4-byte pointer slot under test.
    const uint SlotAddr = 0x00800000u;
    // The null-terminated id array the slot initially points at.
    const uint ListBase = 0x00810000u;
    // Free-space region so WriteByteList's FindFreeSpace relocates here.
    const uint FreeSpaceBase = 0x00C00000u;

    /// <summary>
    /// Build a synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item>A pointer slot at <see cref="SlotAddr"/> → <see cref="ListBase"/>
    ///     (when <paramref name="seedList"/> is non-null) or 0 (null slot).</item>
    ///   <item>A null-terminated id list at <see cref="ListBase"/> = the seed
    ///     ids + a 0x00 terminator.</item>
    ///   <item>A 0x40000-byte 0xFF free-space region at <see cref="FreeSpaceBase"/>.</item>
    /// </list>
    /// </summary>
    static ROM MakeRom(byte[]? seedList, uint slotRaw = 0)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-sublist.gba", bytes, "BE8E01");

        // Fill the low region (0x100..0x700000) with a non-free sentinel so
        // FindFreeSpace cannot allocate below 0x08000200 (the isSafetyPointer
        // header floor) — an unsafe sub-0x200 base would fail the deref guard.
        // (Start at 0x100, past the ROM header signature at 0xAC.) The slot
        // (0x800000), list (0x810000) and 0xFF free region (0xC00000) all sit
        // above this fill.
        for (int i = 0x100; i < 0x700000; i++)
        {
            bytes[i] = 0x11;
        }

        if (seedList != null)
        {
            BitConverter.GetBytes(ListBase | 0x08000000u).CopyTo(bytes, (int)SlotAddr);
            Array.Copy(seedList, 0, bytes, (int)ListBase, seedList.Length);
            bytes[(int)ListBase + seedList.Length] = 0x00; // terminator
        }
        else
        {
            // Caller-supplied raw slot value (0 = null, or a garbage non-pointer).
            BitConverter.GetBytes(slotRaw).CopyTo(bytes, (int)SlotAddr);
        }

        for (uint i = 0; i < 0x40000u; i++)
        {
            bytes[FreeSpaceBase + i] = 0xFF;
        }

        rom.LoadLow("synthetic-sublist.gba", bytes, "BE8E01");
        return rom;
    }

    static (ROM rom, ROM prevRom, Undo prevUndo) Install(byte[]? seedList, uint slotRaw = 0)
    {
        ROM rom = MakeRom(seedList, slotRaw);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return (rom, prevRom, prevUndo);
    }

    static void Restore(ROM prevRom, Undo prevUndo)
    {
        CoreState.ROM = prevRom;
        CoreState.Undo = prevUndo;
    }

    // -----------------------------------------------------------------
    // VM — Load populates entries + decorated names + count.
    // -----------------------------------------------------------------

    [Fact]
    public void Load_PopulatesEntriesNamesAndCount()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x01, 0x02, 0x03 });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => $"NAME{id}", canEdit: true);

            Assert.True(vm.CanEdit);
            Assert.Equal(3, vm.Entries.Count);
            Assert.Equal(1u, vm.Entries[0].Id);
            Assert.Equal(2u, vm.Entries[1].Id);
            Assert.Equal(3u, vm.Entries[2].Id);
            // Decoration: "0x{id:X2} {name}".
            Assert.Equal("0x01 NAME1", vm.Entries[0].Display);
            Assert.Equal("0x03 NAME3", vm.Entries[2].Display);
            Assert.Contains("Entry count: 3", vm.CountDisplay);
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — AddEntry grows the list by one + repoints the slot.
    // -----------------------------------------------------------------

    [Fact]
    public void AddEntry_GrowsByOne_RepointsSlot()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x05, 0x06 });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Equal(2, vm.Entries.Count);

            uint origSlot = rom.u32(SlotAddr);

            var undo = CoreState.Undo.NewUndoData("add");
            using (ROM.BeginUndoScope(undo)) { vm.AddEntry(undo); }
            CoreState.Undo.Push(undo);

            // Slot repointed away from the original base.
            Assert.NotEqual(origSlot, rom.u32(SlotAddr));

            // Re-Load reflects the +1 entry; the appended id is the placeholder.
            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Equal(3, vm.Entries.Count);
            Assert.Equal(0x05u, vm.Entries[0].Id);
            Assert.Equal(0x06u, vm.Entries[1].Id);
            Assert.Equal(NullTerminatedByteListCore.NewSlotPlaceholder, vm.Entries[2].Id);
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — RemoveSelected shrinks the list by one.
    // -----------------------------------------------------------------

    [Fact]
    public void RemoveSelected_ShrinksByOne()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x0A, 0x0B, 0x0C });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);
            vm.SelectedIndex = 1; // remove 0x0B

            var undo = CoreState.Undo.NewUndoData("remove");
            using (ROM.BeginUndoScope(undo)) { vm.RemoveSelected(undo); }
            CoreState.Undo.Push(undo);

            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal(0x0Au, vm.Entries[0].Id);
            Assert.Equal(0x0Cu, vm.Entries[1].Id);
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — SetSelectedId rewrites one entry's id via WriteByteList.
    // -----------------------------------------------------------------

    [Fact]
    public void SetSelectedId_ChangesEntryId_ViaWriteByteList()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x10, 0x20, 0x30 });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);
            vm.SelectedIndex = 2;       // 0x30
            vm.EditId = 0x99;

            var undo = CoreState.Undo.NewUndoData("setid");
            using (ROM.BeginUndoScope(undo)) { vm.SetSelectedId(undo); }
            CoreState.Undo.Push(undo);

            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Equal(3, vm.Entries.Count);
            Assert.Equal(0x10u, vm.Entries[0].Id);
            Assert.Equal(0x20u, vm.Entries[1].Id);
            Assert.Equal(0x99u, vm.Entries[2].Id); // changed
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — a 0 slot is a legit EMPTY list; AddEntry allocates a fresh one.
    // -----------------------------------------------------------------

    [Fact]
    public void NullSlot_IsEmpty_AddAllocatesFreshOneEntryList()
    {
        var (rom, prevRom, prevUndo) = Install(seedList: null, slotRaw: 0u);
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.True(vm.CanEdit);
            Assert.Empty(vm.Entries);

            Assert.Equal(0u, rom.u32(SlotAddr)); // null before

            var undo = CoreState.Undo.NewUndoData("add-fresh");
            using (ROM.BeginUndoScope(undo)) { vm.AddEntry(undo); }
            CoreState.Undo.Push(undo);

            // Slot now points at a fresh 1-entry list.
            Assert.NotEqual(0u, rom.u32(SlotAddr));
            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Single(vm.Entries);
            Assert.Equal(NullTerminatedByteListCore.NewSlotPlaceholder, vm.Entries[0].Id);
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — a garbage non-zero slot renders an EMPTY list (deref guard).
    // -----------------------------------------------------------------

    [Fact]
    public void GarbageSlot_RendersEmptyList()
    {
        // 0x00000123 is non-zero but NOT a safe GBA pointer (< 0x08000200).
        var (rom, prevRom, prevUndo) = Install(seedList: null, slotRaw: 0x00000123u);
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);
            Assert.Empty(vm.Entries);
            Assert.Contains("invalid", vm.BaseDisplay, StringComparison.OrdinalIgnoreCase);
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — canEdit:false disables mutation gating (Item2 when stride < 20).
    // -----------------------------------------------------------------

    [Fact]
    public void CanEditFalse_StillLoadsButGatesMutation()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x01 });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: false);
            Assert.False(vm.CanEdit);
            Assert.Single(vm.Entries); // still readable
        }
        finally { Restore(prevRom, prevUndo); }
    }

    // -----------------------------------------------------------------
    // VM — fork-on-write: a co-owner's original list is preserved (C2).
    // -----------------------------------------------------------------

    [Fact]
    public void AddEntry_ForksSharedArray_PreservesOriginalBytes()
    {
        var (rom, prevRom, prevUndo) = Install(new byte[] { 0x07, 0x08 });
        try
        {
            var vm = new SkillSubListEditorViewModel();
            vm.Load(SlotAddr, id => "", canEdit: true);

            byte[] origListBytes = rom.getBinaryData(ListBase, 4); // 07 08 00 ..

            var undo = CoreState.Undo.NewUndoData("fork");
            using (ROM.BeginUndoScope(undo)) { vm.AddEntry(undo); }
            CoreState.Undo.Push(undo);

            // The ORIGINAL array bytes at ListBase are intact (fork-on-write).
            Assert.Equal(0x07u, rom.u8(ListBase + 0));
            Assert.Equal(0x08u, rom.u8(ListBase + 1));
            Assert.Equal(0x00u, rom.u8(ListBase + 2));
            // And the slot moved away from ListBase.
            Assert.NotEqual(ListBase, rom.p32(SlotAddr));
        }
        finally { Restore(prevRom, prevUndo); }
    }
}
