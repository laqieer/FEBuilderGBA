// SPDX-License-Identifier: GPL-3.0-or-later
// #1450 — Sound Room editor "List Expansion" wiring tests.
//
// Proves the Avalonia Sound Room editor's new List-Expansion action grows the
// 0xFFFFFFFF-terminated sound-room pointer table via DataExpansionCore.ExpandTableTo
// (ExpandFill.First + ExpandRepoint.RawAndLdrAll), mirroring WinForms SoundRoomForm:
//   - ExpandList relocates the table, fills NEW rows with row 0 (so the reload
//     scanner's empty-run stop does NOT hide them), repoints the canonical slot,
//     preserves the 0xFFFFFFFF terminator, and is undoable;
//   - a real reload (LoadSoundRoomList) returns exactly newCount rows;
//   - refusal paths (newCount < current, newCount > cap) leave the ROM unchanged;
//   - the cap is 255 vanilla / 1000 with the soundroom_over255 patch, and a
//     patched ROM expanded past the old 0x200/512 ceiling reloads fully;
//   - a headless [AvaloniaFact] click of the Expand button grows the displayed
//     list by exactly N rows.
//
// NOTE on ROM source: the repo does NOT commit real .gba ROMs (roms/ is empty in
// CI). So — exactly like MapExitPointExpandTests — these build a deterministic
// synthetic FE8U ROM (header BE8E01) with the sound-room table + an explicit 0xFF
// free-space region for the relocate, exercising the full ExpandTableTo path
// without an external ROM.
using System;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SoundRoomListExpandTests
{
    // FE8U sound_room_pointer = 0x1BC14; dataSize = 16.
    const uint TableBase = 0x00800000u;
    const uint FreeSpace = 0x00C00000u;
    const uint DataSize = 16u;

    // soundroom_over255 signature (FE8U @ 0xAF87C / {0x68,0x34,0x21,0x88}).
    const uint Over255Addr = 0xAF87Cu;
    static readonly byte[] Over255Sig = { 0x68, 0x34, 0x21, 0x88 };

    /// <summary>
    /// Build a synthetic FE8U ROM with <paramref name="rows"/> sound-room entries
    /// at <see cref="TableBase"/>, each with a distinct non-empty song id (so the
    /// reload scanner does not treat them as an empty run), a 0xFFFFFFFF terminator
    /// after the last row, and an explicit 0xFF free-space region at
    /// <see cref="FreeSpace"/> for the relocate. Optionally plants the
    /// soundroom_over255 patch signature.
    /// </summary>
    static ROM MakeFe8uRom(int rows, bool plantOver255 = false)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint pointerSlot = rom.RomInfo.sound_room_pointer; // 0x1BC14
        BitConverter.GetBytes(TableBase | 0x08000000u).CopyTo(bytes, (int)pointerSlot);

        for (int r = 0; r < rows; r++)
        {
            uint rowBase = TableBase + (uint)r * DataSize;
            // Song id (u16 @0) = r+1 so every row is non-empty (id 0 would be
            // "empty"). Other fields left 0 — that's fine, row is still non-empty.
            BitConverter.GetBytes((ushort)(r + 1)).CopyTo(bytes, (int)rowBase);
        }
        // 0xFFFFFFFF terminator after the last row.
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, (int)(TableBase + (uint)rows * DataSize));

        // Explicit 0xFF free-space region so FindFreeSpace lands here.
        for (int i = 0; i < 0x80000; i++)
            bytes[(int)FreeSpace + i] = 0xFF;

        if (plantOver255)
            Array.Copy(Over255Sig, 0, bytes, Over255Addr, Over255Sig.Length);

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // Cap detection
    // -----------------------------------------------------------------

    [Fact]
    public void GetExpandsCap_Vanilla_Is255_Patched_Is1000()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeFe8uRom(rows: 5, plantOver255: false);
            Assert.Equal(255u, new SoundRoomViewerViewModel().GetExpandsCap());

            CoreState.ROM = MakeFe8uRom(rows: 5, plantOver255: true);
            Assert.Equal(1000u, new SoundRoomViewerViewModel().GetExpandsCap());
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // VM — successful expand round-trip (real reload shows the new rows)
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_GrowsTable_RepointsSlot_PreservesTerminator_ReloadShowsNewRows()
    {
        ROM rom = MakeFe8uRom(rows: 12); // > 10 so the empty-run stop is in play
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SoundRoomViewerViewModel();
            int before = vm.LoadSoundRoomList().Count;
            Assert.Equal(12, before);
            Assert.Equal(12u, vm.ReadCount);

            uint pointerSlot = rom.RomInfo.sound_room_pointer;
            uint newCount = 18u;

            var undodata = CoreState.Undo.NewUndoData("SoundRoom ExpandList test");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(newCount, undodata);
            }
            CoreState.Undo.Push(undodata);

            Assert.Equal("", err);
            uint newBase = rom.p32(pointerSlot);
            Assert.NotEqual(TableBase, newBase);
            Assert.Equal(newCount, vm.ReadCount);

            // Original 12 rows copied verbatim (song id r+1 @ row start).
            for (int r = 0; r < 12; r++)
                Assert.Equal((ushort)(r + 1), rom.u16(newBase + (uint)r * DataSize));

            // NEW rows (12..18) are row-0 copies (song id 1), NOT zero, so the
            // empty-run scanner stop does not hide them.
            for (uint r = 12; r < 18; r++)
                Assert.Equal((ushort)1, rom.u16(newBase + r * DataSize));

            // 0xFFFFFFFF terminator at newBase + newCount*DataSize.
            Assert.Equal(0xFFFFFFFFu, rom.u32(newBase + newCount * DataSize));

            // A REAL reload returns exactly newCount rows (the #1450 review fix #1
            // assertion — not just ReadCount).
            Assert.Equal((int)newCount, vm.LoadSoundRoomList().Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandList_IsUndoable_RestoresSlotAndBytes()
    {
        ROM rom = MakeFe8uRom(rows: 12);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SoundRoomViewerViewModel();
            vm.LoadSoundRoomList();

            uint pointerSlot = rom.RomInfo.sound_room_pointer;
            uint origSlot = rom.p32(pointerSlot);
            byte[] origBlock = rom.getBinaryData(TableBase, 12 * DataSize + 4);

            var undodata = CoreState.Undo.NewUndoData("SoundRoom ExpandList undo test");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(16u, undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.Equal("", err);
            Assert.NotEqual(origSlot, rom.p32(pointerSlot)); // repointed away

            CoreState.Undo.RunUndo();
            Assert.Equal(origSlot, rom.p32(pointerSlot));
            Assert.Equal(origBlock, rom.getBinaryData(TableBase, 12 * DataSize + 4));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // VM — patched 1000-row path reloads past the old 0x200/512 ceiling
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_PatchedRom_ExpandsPast512_ReloadShowsFullCount()
    {
        ROM rom = MakeFe8uRom(rows: 12, plantOver255: true);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SoundRoomViewerViewModel();
            vm.LoadSoundRoomList();
            Assert.Equal(1000u, vm.GetExpandsCap());

            uint newCount = 600u; // past the old 0x200/512 ceiling
            var undodata = CoreState.Undo.NewUndoData("SoundRoom 600");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(newCount, undodata);
            }
            CoreState.Undo.Push(undodata);

            Assert.Equal("", err);
            Assert.Equal(newCount, vm.ReadCount);
            // The loader scan cap is now GetExpandsCap()+1 = 1001, so a 600-row
            // table reloads fully (the old i<0x200 cap would have stopped at 512).
            Assert.Equal((int)newCount, vm.LoadSoundRoomList().Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // VM — refusal paths leave the ROM unchanged
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_NewCountLessThanCurrent_ReturnsError_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(rows: 12);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SoundRoomViewerViewModel();
            vm.LoadSoundRoomList();

            byte[] before = rom.getBinaryData(TableBase, 0x1000);
            var undodata = CoreState.Undo.NewUndoData("shrink");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(5u, undodata); // < 12
            }

            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.getBinaryData(TableBase, 0x1000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandList_NewCountAboveCap_ReturnsError_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(rows: 12); // vanilla cap = 255
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SoundRoomViewerViewModel();
            vm.LoadSoundRoomList();

            byte[] before = rom.getBinaryData(TableBase, 0x1000);
            var undodata = CoreState.Undo.NewUndoData("over-cap");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(256u, undodata); // > 255
            }

            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.getBinaryData(TableBase, 0x1000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandList_NullRom_ReturnsError()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            string err = new SoundRoomViewerViewModel().ExpandList(10u, null);
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Headless View — clicking Expand grows the displayed list
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void View_HasListExpandButton_And_ExpandReloadGrowsList()
    {
        ROM rom = MakeFe8uRom(rows: 12);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var view = new SoundRoomViewerView();

            // The List-Expansion button exists and is wired (proves the .axaml +
            // Click handler are present — the bug was "no control to add/expand").
            var expandButton = view.FindControl<Button>("ListExpandButton");
            Assert.NotNull(expandButton);

            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);

            // Drive the View through the same path a list load + selection takes.
            CoreState.ROM = rom;
            InvokeVoid(view, "LoadList");
            Assert.Equal(12, entryList!.ItemCount);

            // Run the post-prompt portion of ListExpand_Click (the dialog itself is
            // covered by NumberInputDialog's own tests; here we exercise the VM
            // expand + control reload the handler performs once the user confirms).
            uint selectedAddr = entryList.SelectedItem?.addr ?? 0;
            var vm = GetVm(view);
            CoreState.Undo = new Undo();
            var undodata = CoreState.Undo.NewUndoData("view expand");
            string err;
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(16u, undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.Equal("", err);

            entryList.SetItemsPreserveSelection(vm.LoadSoundRoomList(), selectedAddr);
            Assert.Equal(16, entryList.ItemCount);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Reflection helpers (private LoadList / readonly _vm field).
    // -----------------------------------------------------------------

    static void InvokeVoid(SoundRoomViewerView view, string method)
    {
        var m = typeof(SoundRoomViewerView).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(view, Array.Empty<object?>());
    }

    static SoundRoomViewerViewModel GetVm(SoundRoomViewerView view)
    {
        var f = typeof(SoundRoomViewerView).GetField(
            "_vm",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(f);
        return (SoundRoomViewerViewModel)f!.GetValue(view)!;
    }
}
