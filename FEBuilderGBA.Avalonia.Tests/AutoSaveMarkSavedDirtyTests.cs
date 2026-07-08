// SPDX-License-Identifier: GPL-3.0-or-later
// #1914 — After a manual Save, the window-close dirty check (Undo.IsModified) must
// report clean. AutoSaveService.MarkSaved() is the hook every primary-ROM save
// calls; it now also advances the undo saved-position (Undo.MarkFileSaved), so an
// edited-then-saved ROM stops looking dirty and the close prompt is suppressed.
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class AutoSaveMarkSavedDirtyTests : System.IDisposable
{
    readonly ROM? _prevRom = CoreState.ROM;
    readonly Undo? _prevUndo = CoreState.Undo;

    public void Dispose()
    {
        CoreState.ROM = _prevRom;
        CoreState.Undo = _prevUndo;
    }

    static void SetupRomAndUndo()
    {
        var rom = new ROM();
        rom.SwapNewROMDataDirect(new byte[0x1000]);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
    }

    static void PushEdit(uint addr, byte val)
    {
        var undo = CoreState.Undo!;
        var ud = undo.NewUndoData("test");
        ud.list.Add(new Undo.UndoPostion(addr, new byte[] { val }));
        undo.Push(ud);
    }

    [Fact]
    public void MarkSaved_ClearsUndoIsModified_SoClosePromptIsSuppressed()
    {
        SetupRomAndUndo();

        PushEdit(0, 0x11);
        Assert.True(CoreState.Undo!.IsModified);

        // Simulate a manual Save: MarkSaved() runs after every primary-ROM write.
        AutoSaveService.Instance.MarkSaved();
        Assert.False(CoreState.Undo!.IsModified);

        // A subsequent edit re-dirties the ROM.
        PushEdit(1, 0x22);
        Assert.True(CoreState.Undo!.IsModified);
    }

    [Fact]
    public void UpdateRomFilename_ThenMarkSaved_TracksNewName_AndClearsDirty()
    {
        // Save As changes ROM.Filename; RomFileService.SaveRomAsync must point
        // autosave at the new name (so the sidecar isn't written next to the old
        // path) AND clear the dirty flag. Verify the UpdateRomFilename+MarkSaved
        // combo does both (#1914 review).
        SetupRomAndUndo();
        PushEdit(0, 0x11);
        Assert.True(CoreState.Undo!.IsModified);

        AutoSaveService.Instance.UpdateRomFilename("renamed-rom.gba");
        AutoSaveService.Instance.MarkSaved();

        Assert.Equal("renamed-rom.gba", AutoSaveService.Instance.CurrentRomFilename);
        Assert.False(CoreState.Undo!.IsModified);
    }
}
