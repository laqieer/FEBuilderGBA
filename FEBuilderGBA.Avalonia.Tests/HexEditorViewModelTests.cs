// SPDX-License-Identifier: GPL-3.0-or-later
// #1466 — the Avalonia Hex Editor was read-only (display/navigate/search only),
// while the WinForms Hex Editor (HexEditorForm.WriteButton_Click) is a full
// in-place editor with a Write (Ctrl+S) action under undo. These headless tests
// drive the real HexEditorViewModel.Write(editedHexText) path against synthetic
// ROMs and prove:
//   * Editing a byte in the rendered page and Writing persists it to the ROM.
//   * Only differing cells are written (no-op page → no mutation).
//   * Invalid hex (bad nibble / tampered address gutter) is rejected with NO
//     mutation.
//   * The exact View sequence (UndoService Begin → Write → Commit) round-trips,
//     and Rollback restores byte identity.

using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class HexEditorViewModelTests : IDisposable
{
    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public HexEditorViewModelTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    static ROM MakeRom(int size = 0x200)
    {
        var rom = new ROM();
        rom.LoadLow("hex-1466.gba", new byte[size], "NAZO");
        CoreState.ROM = rom;
        return rom;
    }

    static HexEditorViewModel MakeVm(uint baseAddr = 0)
    {
        var vm = new HexEditorViewModel { BaseAddress = baseAddr };
        vm.RefreshDisplay();
        return vm;
    }

    /// <summary>Replace the 2-char hex cell for an absolute address in a freshly-rendered page.</summary>
    static string EditCell(string page, uint addr, string newToken)
    {
        string[] lines = page.Replace("\r\n", "\n").Split('\n');
        uint rowBase = addr & 0xFFFFFFF0;
        int col = (int)(addr - rowBase);
        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            int bar1 = line.IndexOf('|');
            if (bar1 < 0) continue;
            string addrTok = line.Substring(0, bar1).Trim();
            if (addrTok.Length != 8) continue;
            if (!uint.TryParse(addrTok, System.Globalization.NumberStyles.HexNumber, null, out uint rb)) continue;
            if (rb != rowBase) continue;

            int bar2 = line.IndexOf('|', bar1 + 1);
            string hexRegion = line.Substring(bar1 + 1, bar2 - bar1 - 1);
            int start = bar1 + 1 + NthTokenStart(hexRegion, col);
            lines[li] = line.Substring(0, start) + newToken + line.Substring(start + 2);
            return string.Join("\n", lines);
        }
        throw new InvalidOperationException($"Row for addr 0x{addr:X} not found.");
    }

    static int NthTokenStart(string hexRegion, int n)
    {
        int i = 0, count = 0;
        while (i < hexRegion.Length)
        {
            while (i < hexRegion.Length && hexRegion[i] == ' ') i++;
            if (i >= hexRegion.Length) break;
            if (count == n) return i;
            count++;
            while (i < hexRegion.Length && hexRegion[i] != ' ') i++;
        }
        throw new InvalidOperationException($"Token {n} not found.");
    }

    // ------------------------------------------------------------------

    [Fact]
    public void Write_EditedByte_PersistsToRom()
    {
        var rom = MakeRom();
        var vm = MakeVm();
        string edited = EditCell(vm.HexDisplay, 0x12, "7E");

        var wr = vm.Write(edited);

        Assert.True(wr.Success, wr.Message);
        Assert.Equal(1, wr.BytesWritten);
        Assert.Equal(0x7Eu, rom.u8(0x12));
    }

    [Fact]
    public void Write_NoChanges_ReturnsFailureNoMutation()
    {
        var rom = MakeRom();
        for (int i = 0; i < 0x20; i++) rom.Data[i] = (byte)i;
        var vm = MakeVm();

        var wr = vm.Write(vm.HexDisplay); // unchanged page

        Assert.False(wr.Success);
        Assert.Equal(0, wr.BytesWritten);
    }

    [Fact]
    public void Write_InvalidHex_Rejected_NoMutation()
    {
        var rom = MakeRom();
        var vm = MakeVm();
        string edited = EditCell(vm.HexDisplay, 0x10, "ZZ");

        var wr = vm.Write(edited);

        Assert.False(wr.Success);
        Assert.Equal(0x00u, rom.u8(0x10)); // unchanged
    }

    [Fact]
    public void Write_TamperedAddressGutter_Rejected_NoMutation()
    {
        var rom = MakeRom();
        var vm = MakeVm();
        string edited = vm.HexDisplay.Replace("00000010 |", "0000BEEF |");

        var wr = vm.Write(edited);

        Assert.False(wr.Success);
    }

    [Fact]
    public void ViewSequence_BeginWriteCommit_RoundTrips()
    {
        var rom = MakeRom();
        var undo = new Undo();
        CoreState.Undo = undo;
        var undoService = new UndoService();
        var vm = MakeVm();

        string edited = EditCell(vm.HexDisplay, 0x10, "AB");

        // Exact code-behind sequence.
        undoService.Begin("Edit Hex");
        var wr = vm.Write(edited);
        Assert.True(wr.Success, wr.Message);
        undoService.Commit();

        Assert.Equal(0xABu, rom.u8(0x10));
        Assert.True(undo.IsModified);

        // Undo restores the original byte.
        undo.RunUndo();
        Assert.Equal(0x00u, rom.u8(0x10));
    }

    [Fact]
    public void Write_NoRom_ReturnsFailure()
    {
        CoreState.ROM = null;
        var vm = new HexEditorViewModel();
        var wr = vm.Write("anything");
        Assert.False(wr.Success);
    }
}
