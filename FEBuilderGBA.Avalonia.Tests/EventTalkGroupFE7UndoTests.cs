// SPDX-License-Identifier: GPL-3.0-or-later
// #1428 — Talk Group (FE7) editor write must be undo-tracked. The Avalonia
// EventTalkGroupFE7View previously called _vm.Write() with NO UndoService scope,
// so the ROM write recorded into a null ambient undo and was never captured:
// the global Undo could not revert it and the dirty indicator never flipped.
// The fix wraps OnWrite in
//   _undoService.Begin(...); try { _vm.TextId=...; _vm.Write();
//   _undoService.Commit(); _vm.MarkClean(); }
//   catch { _undoService.Rollback(); Log.Error(...); }
// mirroring the canonical sibling EventFunctionPointerFE7View (#1426).
//
// These headless tests drive the EXACT View OnWrite sequence with a REAL
// UndoService against a synthetic FE7 ROM and prove:
//   * Commit() records the write into CoreState.Undo (IsModified == true) and
//     leaves the VM clean (IsDirty == false), and RunUndo() restores byte
//     identity.
//   * Rollback() restores byte identity.
//   * The .axaml.cs view actually contains the undo wiring (source guard).
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
public class EventTalkGroupFE7UndoTests : IDisposable
{
    const uint SlotAddr   = 0x00900000u; // talk-group entry (u32 TextId at +0)
    const uint OrigTextId = 0x00000123u; // initial text id
    const uint NewTextId  = 0x00000456u; // edited text id

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public EventTalkGroupFE7UndoTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    [Fact]
    public void Write_IsUndoable_FE7()
    {
        ROM rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventTalkGroupFE7ViewModel();
        vm.LoadEntry(SlotAddr);
        Assert.True(vm.IsLoaded);
        Assert.Equal(OrigTextId, vm.TextId);
        vm.MarkClean(); // mirror the View's load (selection settles dirty=false)

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        // EXACT View OnWrite sequence (Begin -> set -> Write -> Commit -> MarkClean).
        undoService.Begin("Edit Talk Group (FE7)");
        vm.TextId = NewTextId;
        vm.Write();
        undoService.Commit();
        vm.MarkClean();

        // The write hit the ROM, was captured by the undo buffer, and the VM is
        // clean again after the save.
        Assert.Equal(NewTextId, rom.u32(SlotAddr));
        Assert.True(CoreState.Undo.IsModified);
        Assert.False(vm.IsDirty);

        // Undo reverts byte-identity.
        CoreState.Undo.RunUndo();
        Assert.Equal(OrigTextId, rom.u32(SlotAddr));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Rollback_RestoresBytes_FE7()
    {
        ROM rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var vm = new EventTalkGroupFE7ViewModel();
        vm.LoadEntry(SlotAddr);
        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        undoService.Begin("Edit Talk Group (FE7)");
        vm.TextId = NewTextId;
        vm.Write();
        Assert.Equal(NewTextId, rom.u32(SlotAddr)); // applied inside the scope
        undoService.Rollback();

        // Rollback discards the in-scope write -> byte identity restored.
        Assert.Equal(OrigTextId, rom.u32(SlotAddr));
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void View_OnWrite_UsesUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventTalkGroupFE7View.axaml.cs");
        Assert.True(File.Exists(path), $"{path} must exist");
        string src = File.ReadAllText(path);

        Assert.Contains("readonly UndoService _undoService", src);
        Assert.Contains("_undoService.Begin(", src);
        Assert.Contains("_undoService.Commit()", src);
        Assert.Contains("_undoService.Rollback()", src);
        Assert.Contains("_vm.MarkClean()", src);
    }

    // #1442 — NewAlloc (new 14×4=56-byte block) must be undo-tracked through the
    // UndoService scope, exactly like OnNewBlock in the view. Commit records the
    // append (IsModified) and the editor repoints onto the new block; Undo restores
    // byte identity.
    [Fact]
    public void NewAlloc_IsUndoable_FE7()
    {
        var savedAppend = CoreState.AppendBinaryData;
        try
        {
            CoreState.AppendBinaryData = null; // force the headless free-space fallback

            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new EventTalkGroupFE7ViewModel();
            byte[] snap = (byte[])rom.Data.Clone();
            var undoService = new UndoService();

            // EXACT View OnNewBlock sequence (Begin -> NewAlloc -> Commit).
            undoService.Begin("New Block Talk Group (FE7)");
            uint newAddr = vm.NewAlloc();
            Assert.NotEqual(U.NOT_FOUND, newAddr);
            undoService.Commit();

            // The 56-byte block was appended, the editor repointed onto it, and the
            // append was captured by the undo buffer.
            Assert.Equal(newAddr, vm.BaseAddr);
            Assert.True(CoreState.Undo.IsModified);

            // The new block lists 14 stride-4 entries.
            var list = vm.LoadList();
            Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, list.Count);

            // Undo reverts byte-identity.
            CoreState.Undo.RunUndo();
            Assert.Equal(snap, rom.Data);
        }
        finally
        {
            CoreState.AppendBinaryData = savedAppend;
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Synthetic FE7 ROM with a talk-group entry planted at <see cref="SlotAddr"/>
    /// (u32 TextId = <see cref="OrigTextId"/> at offset 0). The VM's LoadEntry
    /// reads the u32 directly, so no event-script table is needed.
    /// </summary>
    static ROM MakeRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synth-AE7E01-1428.gba", bytes, "AE7E01");
        PlantU32(bytes, SlotAddr, OrigTextId);
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
