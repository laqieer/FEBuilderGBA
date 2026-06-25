// SPDX-License-Identifier: GPL-3.0-or-later
// #1426 — Event Function Pointer editor (FE8 + FE7 variants) write must be
// undo-tracked. Both Avalonia views (EventFunctionPointerView /
// EventFunctionPointerFE7View) previously called _vm.Write() with NO
// UndoService scope, so the ROM write recorded into a null ambient undo and was
// never captured: the global Undo could not revert it and the dirty indicator
// never flipped. The fix wraps OnWrite in
//   _undoService.Begin(...); try { ...; _vm.Write(); _undoService.Commit();
//   _vm.MarkClean(); } catch { _undoService.Rollback(); Log.Error(...); }
// mirroring the canonical sibling Command85PointerView.
//
// These headless tests drive the EXACT View OnWrite sequence with a REAL
// UndoService against synthetic ROMs and prove:
//   * Commit() records the write into CoreState.Undo (IsModified == true) and
//     leaves the VM clean (IsDirty == false), and RunUndo() restores byte
//     identity.
//   * Rollback() restores byte identity.
//   * Both .axaml.cs views actually contain the undo wiring (source guards).
// Covers BOTH variants: FE8/FE6 (stride 4, one pointer field) and FE7
// (stride 8, pointer + Unknown4).
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM / .Undo.

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EventFunctionPointerUndoTests : IDisposable
{
    const uint TableBase = 0x00900000u; // function-pointer table
    // Function-pointer entries must be Thumb (odd) code pointers: the editor's
    // list scan requires U.isPointer && U.IsValueOdd (WinForms parity, #1441).
    const uint Entry0Ptr = 0x08123457u; // initial pointer at slot 0 (odd/Thumb)
    const uint NewPtr    = 0x08ABCDEFu; // edited pointer (odd/Thumb)
    const uint NewUnk4   = 0x08FEDCBAu; // edited Unknown4 (FE7 only)

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public EventFunctionPointerUndoTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    // ================================================================
    // FE8/FE6 variant (stride 4) — EventFunctionPointerViewModel
    // ================================================================

    [Fact]
    public void Write_IsUndoable_FE8()
    {
        ROM rom = MakeRom("BE8E01", strideEight: false);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventFunctionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        Assert.True(vm.IsLoaded);
        Assert.Equal(Entry0Ptr, vm.EventCommandFunctionPointer);
        vm.MarkClean(); // mirror the View's load (selection settles dirty=false)

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        // EXACT View OnWrite sequence (Begin → set → Write → Commit → MarkClean).
        undoService.Begin("Edit Event Function Pointer");
        vm.EventCommandFunctionPointer = NewPtr;
        vm.Write();
        undoService.Commit();
        vm.MarkClean();

        // The write hit the ROM, was captured by the undo buffer, and the VM is
        // clean again after the save.
        Assert.Equal(NewPtr, rom.u32(addr));
        Assert.True(CoreState.Undo.IsModified);
        Assert.False(vm.IsDirty);

        // Undo reverts byte-identity.
        CoreState.Undo.RunUndo();
        Assert.Equal(Entry0Ptr, rom.u32(addr));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Rollback_RestoresBytes_FE8()
    {
        ROM rom = MakeRom("BE8E01", strideEight: false);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventFunctionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        undoService.Begin("Edit Event Function Pointer");
        vm.EventCommandFunctionPointer = NewPtr;
        vm.Write();
        Assert.Equal(NewPtr, rom.u32(addr)); // applied inside the scope
        undoService.Rollback();

        // Rollback discards the in-scope write → byte identity restored.
        Assert.Equal(Entry0Ptr, rom.u32(addr));
        Assert.Equal(snap, rom.Data);
    }

    // ================================================================
    // FE7 variant (stride 8) — EventFunctionPointerFE7ViewModel
    // ================================================================

    [Fact]
    public void Write_IsUndoable_FE7()
    {
        ROM rom = MakeRom("AE7E01", strideEight: true);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventFunctionPointerFE7ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        Assert.True(vm.IsLoaded);
        Assert.Equal(Entry0Ptr, vm.EventCommandFunctionPointer);
        vm.MarkClean();

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        undoService.Begin("Edit Event Function Pointer");
        vm.EventCommandFunctionPointer = NewPtr;
        vm.Unknown4 = NewUnk4;
        vm.Write();
        undoService.Commit();
        vm.MarkClean();

        // Both fields written, captured, and the VM is clean.
        Assert.Equal(NewPtr, rom.u32(addr + 0));
        Assert.Equal(NewUnk4, rom.u32(addr + 4));
        Assert.True(CoreState.Undo.IsModified);
        Assert.False(vm.IsDirty);

        // Undo reverts both fields → byte identity.
        CoreState.Undo.RunUndo();
        Assert.Equal(Entry0Ptr, rom.u32(addr + 0));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Rollback_RestoresBytes_FE7()
    {
        ROM rom = MakeRom("AE7E01", strideEight: true);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventFunctionPointerFE7ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        undoService.Begin("Edit Event Function Pointer");
        vm.EventCommandFunctionPointer = NewPtr;
        vm.Unknown4 = NewUnk4;
        vm.Write();
        Assert.Equal(NewPtr, rom.u32(addr + 0));
        Assert.Equal(NewUnk4, rom.u32(addr + 4));
        undoService.Rollback();

        Assert.Equal(Entry0Ptr, rom.u32(addr + 0));
        Assert.Equal(snap, rom.Data);
    }

    // ================================================================
    // Source-wiring guards — the views actually carry the undo scope.
    // ================================================================

    [Fact]
    public void View_FE8_OnWrite_UsesUndoScope()
        => AssertViewHasUndoWiring("EventFunctionPointerView.axaml.cs");

    [Fact]
    public void View_FE7_OnWrite_UsesUndoScope()
        => AssertViewHasUndoWiring("EventFunctionPointerFE7View.axaml.cs");

    static void AssertViewHasUndoWiring(string fileName)
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", fileName);
        Assert.True(File.Exists(path), $"{path} must exist");
        string src = File.ReadAllText(path);

        Assert.Contains("readonly UndoService _undoService", src);
        Assert.Contains("_undoService.Begin(", src);
        Assert.Contains("_undoService.Commit()", src);
        Assert.Contains("_undoService.Rollback()", src);
        Assert.Contains("_vm.MarkClean()", src);
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Synthetic ROM whose <c>event_function_pointer_table_pointer</c> (a fixed
    /// per-version offset) points at <see cref="TableBase"/>. The table holds N
    /// function pointers (stride 4 for FE6/8, stride 8 for FE7), terminated by a
    /// non-pointer (0) so the VM's LoadList stops scanning.
    /// </summary>
    static ROM MakeRom(string header, bool strideEight)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow($"synth-{header}-1426.gba", bytes, header);

        uint stride = strideEight ? 8u : 4u;
        uint tablePtrLoc = rom.RomInfo.event_function_pointer_table_pointer;
        Assert.NotEqual(0u, tablePtrLoc);

        // p32(tablePtrLoc) → TableBase (as a GBA pointer).
        PlantU32(bytes, tablePtrLoc, TableBase | 0x08000000u);

        // Three valid Thumb (odd) pointer entries, then a 0 terminator.
        for (uint i = 0; i < 3; i++)
        {
            uint slot = TableBase + i * stride;
            // OR 1 to keep every entry odd (Thumb) so the editor's
            // isPointer && IsValueOdd scan accepts the full table.
            PlantU32(bytes, slot, i == 0 ? Entry0Ptr : ((0x08000100u + i * 4u) | 1u));
            if (strideEight)
                PlantU32(bytes, slot + 4, 0x08000000u + i); // Unknown4 (any value)
        }
        // Terminator: a non-pointer u32 at the next slot.
        PlantU32(bytes, TableBase + 3 * stride, 0x00000000u);

        return rom;
    }

    static void PlantU32(byte[] bytes, uint addr, uint value)
    {
        int idx = (int)addr;
        bytes[idx + 0] = (byte)(value & 0xFF);
        bytes[idx + 1] = (byte)((value >> 8) & 0xFF);
        bytes[idx + 2] = (byte)((value >> 16) & 0xFF);
        bytes[idx + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return AppContext.BaseDirectory;
    }
}
