// SPDX-License-Identifier: GPL-3.0-or-later
// #1445 — the Avalonia C-String editor was a non-functional stub (addr-0 dummy
// list row, Address label always 0x00000000, no text box, no Write). These
// headless tests drive the real ported CStringViewModel against synthetic ROMs
// and prove:
//   * LoadEntry decodes the NUL-terminated C-string into Text.
//   * The exact View OnWrite sequence (UndoService Begin → Write → Commit)
//     persists an in-place edit and round-trips through the ROM.
//   * A grow Write moves the string + repoints the parent slot; Rollback
//     restores byte identity.
//   * A standalone grow with no slot and no reference refuses (no orphan).
//   * IDataVerifiable surfaces the loaded address/text.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM /.Undo /
// .SystemTextEncoder.

using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class CStringViewModelTests : IDisposable
{
    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;
    readonly ISystemTextEncoder? _savedEncoder;

    public CStringViewModelTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
        _savedEncoder = CoreState.SystemTextEncoder;
        CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
        CoreState.SystemTextEncoder = _savedEncoder;
        PatchDetection.ClearAllCaches();
    }

    static ROM MakeRom(int size = 0x200000)
    {
        var rom = new ROM();
        rom.LoadLow("cstring-1445.gba", new byte[size], "NAZO");
        return rom;
    }

    static void WriteAscii(ROM rom, uint addr, string s)
    {
        byte[] enc = new HeadlessSystemTextEncoder().Encode(s);
        for (int i = 0; i < enc.Length; i++) rom.Data[addr + i] = enc[i];
        rom.Data[addr + enc.Length] = 0x00;
    }

    static void WritePointer(ROM rom, uint slot, uint offset)
    {
        uint gba = offset + 0x08000000;
        rom.Data[slot + 0] = (byte)(gba & 0xFF);
        rom.Data[slot + 1] = (byte)((gba >> 8) & 0xFF);
        rom.Data[slot + 2] = (byte)((gba >> 16) & 0xFF);
        rom.Data[slot + 3] = (byte)((gba >> 24) & 0xFF);
    }

    // ----------------------------------------------------------------------
    // Load
    // ----------------------------------------------------------------------

    [Fact]
    public void LoadEntry_DecodesString()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Hello World");

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal("Hello World", vm.Text);
    }

    [Fact]
    public void LoadEntry_RawOffset_FromManualAddressBox_DecodesString()
    {
        // The manual address box supplies a raw OFFSET (e.g. 0x1000), not a GBA
        // pointer. Reloading must decode the actual string, NOT show empty
        // (Copilot finding #1 — otherwise Write could blank the existing string).
        var rom = MakeRom();
        CoreState.ROM = rom;
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Manual Path");

        var vm = new CStringViewModel();
        vm.LoadEntry(addr); // raw offset, as the manual box / Reload supplies.

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal("Manual Path", vm.Text);
    }

    [Fact]
    public void LoadEntry_Unsafe_ClearsAndReportsStatus()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;

        var vm = new CStringViewModel();
        vm.LoadEntry(0x10); // < 0x200 danger zone, not a safety offset.

        Assert.False(vm.IsLoaded);
        Assert.Equal(string.Empty, vm.Text);
        Assert.Contains("No valid", vm.Status);
    }

    // ----------------------------------------------------------------------
    // Write (the exact View OnWrite sequence)
    // ----------------------------------------------------------------------

    [Fact]
    public void Write_InPlace_PersistsAndRoundTrips()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint addr = 0x1000;
        WriteAscii(rom, addr, "AAAAAAAA"); // 8 + NUL = 9 => Padding2 => 10 bytes.

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);
        vm.MarkClean();

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        // EXACT View OnWrite sequence.
        undoService.Begin("Edit C-String");
        vm.Text = "Hello"; // 5 + NUL = 6 <= 10 => in place.
        bool changed = vm.Write();
        undoService.Commit();
        vm.MarkClean();

        Assert.True(changed);
        Assert.Contains("in place", vm.Status);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal("Hello", CStringCore.ReadCString(rom, addr + 0x08000000));

        Assert.True(CoreState.Undo.IsModified);
        CoreState.Undo.RunUndo();
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Write_Grow_MovesAndRepointsParentSlot()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint slot = 0x400;
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Hi"); // small region.
        WritePointer(rom, slot, addr);

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);
        vm.ParentPointerSlot = slot;
        vm.MarkClean();

        var undoService = new UndoService();
        undoService.Begin("Edit C-String");
        vm.Text = "A much longer replacement string that will not fit";
        bool changed = vm.Write();
        undoService.Commit();
        vm.MarkClean();

        Assert.True(changed);
        Assert.NotEqual(addr, vm.CurrentAddr);
        Assert.Equal(vm.CurrentAddr, rom.p32(slot)); // parent slot repointed (offset).
        Assert.Equal("A much longer replacement string that will not fit",
            CStringCore.ReadCString(rom, vm.CurrentAddr + 0x08000000));
    }

    [Fact]
    public void Write_Grow_Rollback_RestoresBytes()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint slot = 0x400;
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Hi");
        WritePointer(rom, slot, addr);

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);
        vm.ParentPointerSlot = slot;

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();
        undoService.Begin("Edit C-String");
        vm.Text = "A much longer replacement string that will not fit";
        vm.Write();
        undoService.Rollback();

        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Write_Grow_NoParentNoReference_RefusesNoMutation()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Hi"); // no references anywhere.
        byte[] snap = (byte[])rom.Data.Clone();

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);

        var undoService = new UndoService();
        undoService.Begin("Edit C-String");
        vm.Text = "A much longer replacement string that will not fit";
        bool changed = vm.Write();
        if (changed) undoService.Commit(); else undoService.Rollback();

        Assert.False(changed);
        Assert.Contains("orphan", vm.Status);
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Write_NoRecordLoaded_NoParentSlot_RefusesNoMutation()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        byte[] snap = (byte[])rom.Data.Clone();

        var vm = new CStringViewModel(); // nothing loaded.
        var undoService = new UndoService();
        undoService.Begin("Edit C-String");
        bool changed = vm.Write();
        if (changed) undoService.Commit(); else undoService.Rollback();

        Assert.False(changed);
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void IDataVerifiable_SurfacesAddressAndText()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        uint addr = 0x1000;
        WriteAscii(rom, addr, "Eirika");

        var vm = new CStringViewModel();
        vm.LoadEntry(addr + 0x08000000);

        Assert.Equal(1, vm.GetListCount());
        var report = vm.GetDataReport();
        Assert.Equal("0x00001000", report["addr"]);
        Assert.Equal("Eirika", report["Text"]);
    }

    [Fact]
    public void ListParity_CString_IsNoListEditor()
    {
        Assert.True(ListParityHelper.IsNoListEditor("CStringView"));
    }
}
