// SPDX-License-Identifier: GPL-3.0-or-later
// #837 — VM-level tests for the Magic FEditor + CSA Creator "List Expansion"
// wiring. Proves both byte-identical ViewModels
// (ImageMagicFEditorViewModel.ExpandMagicLists /
//  ImageMagicCSACreatorViewModel.ExpandMagicLists) delegate to the shared
// Core helper (MagicListExpandCore) and that:
//   * the magic-effect (table-1) + CSA (table-2) tables both relocate to the
//     all-reference-repointed new bases (canonical + raw + LDR),
//   * the CSA-pointer NOT_FOUND case aborts cleanly with ZERO mutation,
//   * IsListExpanded flips true after a successful expand (button hidden).
//
// Synthetic FE8U ROM recipe mirrors MagicCSACoreTests.MakeMinimalFE8URomWithCsa.
// [Collection("SharedState")] + save/restore CoreState.ROM/Undo.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class MagicListExpandTests : IDisposable
{
    const uint TableSigAddr = 0x100000u;
    const uint CsaPointerSlot = 0x100010u;
    const uint CsaTableBase = 0x200000u;     // table-2 base before expand
    const uint MagicEffectBase = 0x300000u;  // table-1 base before expand
    const uint FreeRegionA = 0x400000u;      // table-1 lands here
    const int FreeRegionASize = 0x500;
    const uint FreeRegionB = 0x500000u;      // table-2 lands here
    const int FreeRegionBSize = 0x4000;

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public MagicListExpandTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    // ==================================================================
    // FEditor VM — delegates to the Core helper, relocates both tables.
    // ==================================================================

    [Fact]
    public void FEditorVm_ExpandMagicLists_RelocatesBothTables_AndFlipsIsListExpanded()
    {
        ROM rom = MakeRomWithCsa();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint meBaseBefore = rom.p32(rom.RomInfo.magic_effect_pointer);
        uint csaBaseBefore = rom.p32(CsaPointerSlot);
        Assert.Equal(MagicEffectBase, meBaseBefore);
        Assert.Equal(CsaTableBase, csaBaseBefore);

        var vm = new ImageMagicFEditorViewModel();
        // LoadList resolves the patch state; IsListExpanded false initially
        // (spell-data count <= original count on this fresh table).
        vm.LoadList();

        string err = ExpandFEditor(vm);
        Assert.Equal("", err);

        // Both canonical pointers moved (all-reference repoint via the helper).
        Assert.NotEqual(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
        Assert.NotEqual(CsaTableBase, rom.p32(CsaPointerSlot));

        // 0xFFFFFFFF terminators present at each new base + 254*entrySize.
        uint meNew = rom.p32(rom.RomInfo.magic_effect_pointer);
        uint csaNew = rom.p32(CsaPointerSlot);
        Assert.Equal(0xFFFFFFFFu,
            rom.u32(meNew + MagicListExpandCore.NewCount * MagicListExpandCore.MagicEffectEntrySize));
        Assert.Equal(0xFFFFFFFFu,
            rom.u32(csaNew + MagicListExpandCore.NewCount * MagicListExpandCore.CsaEntrySize));

        // After expand the spell-data table is grown past the original count, so
        // the "Expand List" button is hidden (mirrors WF
        // MagicListExpandsButton.Enabled = false).
        Assert.True(vm.IsListExpanded);
    }

    [Fact]
    public void FEditorVm_ExpandMagicLists_CsaNotFound_AbortsWithZeroMutation()
    {
        // ROM WITHOUT the CSA spell-table signature -> GetCSASpellTablePointer
        // returns NOT_FOUND.
        ROM rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        WriteU32(rom, rom.RomInfo.magic_effect_pointer, U.toPointer(MagicEffectBase));
        PlantMagicEffectRows(rom, 4);
        PlantFreeRegion(rom, FreeRegionA, FreeRegionASize);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        Assert.Equal(U.NOT_FOUND, MagicCSACore.GetCSASpellTablePointer(rom));

        byte[] before = (byte[])rom.Data.Clone();

        var vm = new ImageMagicFEditorViewModel();
        string err = ExpandFEditor(vm);

        // Non-empty error, ZERO ROM mutation (incl. the magic-effect table-1).
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal(before, rom.Data);
        Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
    }

    // ==================================================================
    // CSA Creator VM — same delegation + abort behavior.
    // ==================================================================

    [Fact]
    public void CsaVm_ExpandMagicLists_RelocatesBothTables_AndFlipsIsListExpanded()
    {
        ROM rom = MakeRomWithCsa();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
        Assert.Equal(CsaTableBase, rom.p32(CsaPointerSlot));

        var vm = new ImageMagicCSACreatorViewModel();
        vm.LoadList(); // resolves the CSA system + spell-data count

        string err = ExpandCsa(vm);
        Assert.Equal("", err);

        Assert.NotEqual(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
        Assert.NotEqual(CsaTableBase, rom.p32(CsaPointerSlot));
        Assert.True(vm.IsListExpanded);
    }

    [Fact]
    public void CsaVm_ExpandMagicLists_CsaNotFound_AbortsWithZeroMutation()
    {
        ROM rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        WriteU32(rom, rom.RomInfo.magic_effect_pointer, U.toPointer(MagicEffectBase));
        PlantMagicEffectRows(rom, 4);
        PlantFreeRegion(rom, FreeRegionA, FreeRegionASize);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        Assert.Equal(U.NOT_FOUND, MagicCSACore.GetCSASpellTablePointer(rom));
        byte[] before = (byte[])rom.Data.Clone();

        var vm = new ImageMagicCSACreatorViewModel();
        string err = ExpandCsa(vm);

        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal(before, rom.Data);
        Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    static string ExpandFEditor(ImageMagicFEditorViewModel vm)
    {
        var ud = CoreState.Undo.NewUndoData("FEditor ExpandMagicLists test");
        string err;
        using (ROM.BeginUndoScope(ud))
        {
            err = vm.ExpandMagicLists(ud);
        }
        if (string.IsNullOrEmpty(err)) CoreState.Undo.Push(ud);
        return err;
    }

    static string ExpandCsa(ImageMagicCSACreatorViewModel vm)
    {
        var ud = CoreState.Undo.NewUndoData("CSA ExpandMagicLists test");
        string err;
        using (ROM.BeginUndoScope(ud))
        {
            err = vm.ExpandMagicLists(ud);
        }
        if (string.IsNullOrEmpty(err)) CoreState.Undo.Push(ud);
        return err;
    }

    static ROM MakeRomWithCsa()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        byte[] engineSig = { 0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08 };
        Buffer.BlockCopy(engineSig, 0, rom.Data, 0x95d780, engineSig.Length);

        byte[] tableSig = { 0x1C,0x58,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08 };
        Buffer.BlockCopy(tableSig, 0, rom.Data, (int)TableSigAddr, tableSig.Length);
        WriteU32(rom, CsaPointerSlot, U.toPointer(CsaTableBase));

        WriteU32(rom, rom.RomInfo.magic_effect_pointer, U.toPointer(MagicEffectBase));

        PlantFreeRegion(rom, FreeRegionA, FreeRegionASize);
        PlantFreeRegion(rom, FreeRegionB, FreeRegionBSize);
        return rom;
    }

    static void PlantMagicEffectRows(ROM rom, uint count)
    {
        for (uint i = 0; i < count; i++)
            WriteU32(rom, MagicEffectBase + i * MagicListExpandCore.MagicEffectEntrySize,
                U.toPointer(0x95d7edu + i));
        WriteU32(rom, MagicEffectBase + count * MagicListExpandCore.MagicEffectEntrySize, 0xFFFFFFFFu);
    }

    static void PlantFreeRegion(ROM rom, uint start, int length)
    {
        int baseIdx = (int)start;
        for (int i = 0; i < length; i++)
            rom.Data[baseIdx + i] = 0xFF;
    }

    static void WriteU32(ROM rom, uint addr, uint value)
    {
        int idx = (int)addr;
        rom.Data[idx + 0] = (byte)(value & 0xFF);
        rom.Data[idx + 1] = (byte)((value >> 8) & 0xFF);
        rom.Data[idx + 2] = (byte)((value >> 16) & 0xFF);
        rom.Data[idx + 3] = (byte)((value >> 24) & 0xFF);
    }
}
