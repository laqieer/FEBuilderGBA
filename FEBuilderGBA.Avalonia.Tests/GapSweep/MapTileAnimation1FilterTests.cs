// SPDX-License-Identifier: GPL-3.0-or-later
// Structural + VM tests for the MapTileAnimation1 anime1 PLIST filter (#955,
// #957 W1c). Mirrors the MapTileAnimation2ParityTests filter assertions: the
// View must carry the anime1 Filter combo + top bar + selection bar, and the VM
// must drive the entry list off the SELECTED PLIST's data table (instead of
// treating map_tileanime1_pointer — the PLIST table — as a flat entry table).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class MapTileAnimation1FilterTests
{
    // -----------------------------------------------------------------
    // Structural - View must carry the anime1 filter combo + bars.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasFilterCombo()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation1_Filter_Combo\"", axaml);
        Assert.Contains("SelectionChanged=\"FilterCombo_SelectionChanged\"", axaml);
    }

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation1_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact]
    public void View_HasSelectionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation1_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation1_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation1_SelectedAddress_Label\"", axaml);
    }

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    // -----------------------------------------------------------------
    // VM - filter builds + selected-PLIST entry scan (8-byte, +4 pointer).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadPlistList_BuildsFilterRows()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            var rows = vm.LoadPlistList();
            Assert.Single(rows);
            Assert.Equal(1u, rows[0].Plist);
            Assert.False(rows[0].IsBroken);
            Assert.StartsWith("ANIME1 ", rows[0].Display);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_BuildList_ScansSelectedPlistDataTable()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            // The PLIST table slot for plist=1 points at the entry block.
            var items = vm.BuildList(entryAddr);
            Assert.Single(items);
            Assert.Equal(entryAddr, items[0].addr);
            Assert.Equal(entryAddr, vm.ReadStartAddress);
            Assert.Equal(1u, vm.ReadCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadList_DerivesEntriesFromFirstNonBrokenPlist()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            var items = vm.LoadList();
            Assert.Single(items);
            Assert.Equal(entryAddr, items[0].addr);
            Assert.Equal(1u, vm.SelectedPlist);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesFields()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.IsLoaded);
            Assert.Equal(entryAddr, vm.CurrentAddr);
            // Entry: wait=0x13, length=0x1000, imgPtr raw GBA pointer 0x08800100
            // (the field is a raw DWord D4, so it carries the high bit — the
            // view renders it directly as 0x{...:X08}).
            Assert.Equal(0x13u, vm.AnimInterval);
            Assert.Equal(0x1000u, vm.DataCount);
            Assert.Equal(0x08800100u, vm.MapTileDataPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #960 fix 1 - empty non-broken PLIST must CLEAR stale detail.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ClearEntry_ResetsFieldsAndGatesWrite()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.IsLoaded);
            Assert.NotEqual(0u, vm.CurrentAddr);

            vm.ClearEntry();
            Assert.False(vm.IsLoaded); // Write_Click early-returns on !IsLoaded
            Assert.Equal(0u, vm.CurrentAddr);
            Assert.Equal(0u, vm.SelectedAddress);
            Assert.Equal(0u, vm.AnimInterval);
            Assert.Equal(0u, vm.DataCount);
            Assert.Equal(0u, vm.MapTileDataPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_BuildList_OnEmptyPlist_ReturnsEmpty()
    {
        // A non-broken PLIST whose data table is EMPTY (first record's +4 is
        // NOT a pointer) must yield zero entries — the view then clears the
        // stale detail panel (#960).
        var rom = MakeFE8URomWithEmptyFirstPlist(out uint emptyDataAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            var items = vm.BuildList(emptyDataAddr);
            Assert.Empty(items);
            Assert.Equal(emptyDataAddr, vm.ReadStartAddress);
            Assert.Equal(0u, vm.ReadCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #960 fix 2 - golden builder lockstep with VM on empty first PLIST.
    // -----------------------------------------------------------------

    [Fact]
    public void GoldenBuilder_And_VM_Lockstep_WhenFirstNonBrokenPlistIsEmpty()
    {
        // When the FIRST non-broken PLIST resolves to an EMPTY entry table the
        // VM's LoadList() returns that PLIST's (empty) scan rather than falling
        // through to a later PLIST. The golden builder must match exactly (it
        // previously did `if (entries.Count == 0) continue;`, diverging).
        var rom = MakeFE8URomWithEmptyFirstPlist(out uint _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation1ViewModel();
            var vmRows = vm.LoadList();
            var golden = FEBuilderGBA.Avalonia.Services.ListParityHelper
                .BuildReferenceList("MapTileAnimation1View");

            Assert.Empty(vmRows);  // empty first PLIST -> empty list
            Assert.Equal(golden.Count, vmRows.Count);
            for (int i = 0; i < vmRows.Count; i++)
            {
                Assert.Equal(golden[i].name, vmRows[i].name);
                Assert.Equal(golden[i].addr, vmRows[i].addr);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with:
    /// - map_setting_pointer -> 0x08800000 (map block at 0x00800000), one map
    ///   entry with anime1_plist=1 at +9, terminator at index 1.
    /// - map_tileanime1_pointer -> 0x08900000 (PLIST table at 0x00900000).
    /// - PLIST table entry for plist=1 -> 0x08800200 (entry block at
    ///   0x00800200), one 8-byte entry: wait=0x13, length=0x1000, imgPtr at +4
    ///   = 0x08800100.
    /// Returns the entry address (0x00800200).
    /// </summary>
    static ROM MakeMinimalFE8URomWithEntry(out uint entryAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Map block at 0x00800000: D0=pointer, anime1_plist=1 at +9.
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08800000u);
        WriteU32(rom.Data, 0x800000, 0x08800200u); // D0 = pointer (valid)
        rom.Data[0x800000 + 9] = 1; // anime1_plist = 1
        // Map[1]: terminator (D0 = 0)
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)(0x800000 + dataSize), 0u);

        // PLIST table at 0x00900000. Slot 1 -> 0x08800200.
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08900000u);
        WriteU32(rom.Data, 0x900000 + 1 * 4, 0x08800200u);

        // Entry block at 0x00800200: wait=0x13, length=0x1000, imgPtr@4=0x08800100.
        entryAddr = 0x800200;
        WriteU16(rom.Data, (int)entryAddr + 0, 0x13);
        WriteU16(rom.Data, (int)entryAddr + 2, 0x1000);
        WriteU32(rom.Data, (int)entryAddr + 4, 0x08800100u);
        // Entry[1] = zero P4 so ScanEntries stops cleanly.
        WriteU32(rom.Data, (int)entryAddr + 8 + 4, 0u);

        return rom;
    }

    /// <summary>
    /// Build a synthetic FE8U ROM whose FIRST (and only) non-broken anime1
    /// PLIST resolves to a SAFE, non-zero data offset (so BuildPlistList marks
    /// it non-broken) but whose entry table is EMPTY — the first 8-byte
    /// record's image pointer at +4 is NOT a pointer, so ScanEntries returns
    /// zero rows. Returns the resolved (empty) data offset.
    /// </summary>
    static ROM MakeFE8URomWithEmptyFirstPlist(out uint emptyDataAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Map block at 0x00800000: D0=pointer, anime1_plist=1 at +9.
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08800000u);
        WriteU32(rom.Data, 0x800000, 0x08800200u); // D0 = pointer (valid)
        rom.Data[0x800000 + 9] = 1; // anime1_plist = 1
        // Map[1]: terminator (D0 = 0)
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)(0x800000 + dataSize), 0u);

        // PLIST table at 0x00900000. Slot 1 -> 0x08800300 (SAFE, non-zero ->
        // non-broken), but the data block there has a non-pointer at +4 so the
        // entry table is empty.
        emptyDataAddr = 0x800300;
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08900000u);
        WriteU32(rom.Data, 0x900000 + 1 * 4, 0x08800300u);
        // Data block at 0x00800300: image pointer at +4 = 0 (NOT a pointer) so
        // ScanEntries stops immediately -> empty table.
        WriteU32(rom.Data, (int)emptyDataAddr + 0, 0u);
        WriteU32(rom.Data, (int)emptyDataAddr + 4, 0u);

        return rom;
    }

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapTileAnimation1View.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapTileAnimation1View.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
