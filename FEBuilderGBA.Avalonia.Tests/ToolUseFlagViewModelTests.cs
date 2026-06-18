using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless tests for <see cref="ToolUseFlagViewModel"/> (issue #1192 — port of
/// WinForms <c>ToolUseFlagForm</c>). The ViewModel reuses the cross-platform
/// <see cref="UseFlagScanCore"/> aggregator and is strictly READ-ONLY: it never
/// mutates the ROM, so it deliberately does NOT implement IDataVerifiable.
///
/// Row-building and INDEX-keyed detail are covered DETERMINISTICALLY via the
/// <c>LoadFromUsages</c> test seam (injecting synthetic usages), independent of a
/// real ROM/chapter. The end-to-end scan is covered by the Core
/// <c>UseFlagScanCoreTests</c> synthetic-ROM suite.
/// </summary>
[Collection("SharedState")]
public class ToolUseFlagViewModelTests
{
    static UseFlagIDCore Usage(FELintCore.Type type, uint addr, string info, uint id, uint mapid = 0)
        => new(type, addr, info, id, mapid, addr);

    static List<UseFlagIDCore> SampleUsages() => new()
    {
        Usage(FELintCore.Type.EVENT_COND_TURN, 0x00008000, "Turn Conditions", 0x0011),
        Usage(FELintCore.Type.EVENTSCRIPT,     0x00009000, "",               0x0022),
        // Two rows sharing an address but different flag ids — duplicate-address case.
        Usage(FELintCore.Type.MAPCHANGE,       0x0000A000, "00",             0x0033),
        Usage(FELintCore.Type.MAPCHANGE,       0x0000A000, "01",             0x0044),
    };

    [Fact]
    public void LoadFromUsages_BuildsOneRowPerUsage()
    {
        var vm = new ToolUseFlagViewModel();
        List<AddrResult> list = vm.LoadFromUsages(SampleUsages());

        Assert.Equal(4, list.Count);
        Assert.Equal(4, vm.UsageListCount);
        Assert.Equal(4, vm.FlagUsageCount);
        // Summary renders the count.
        Assert.Contains("4", vm.SummaryText);
    }

    [Fact]
    public void RowLabel_ContainsFlagHexAndType()
    {
        var vm = new ToolUseFlagViewModel();
        List<AddrResult> list = vm.LoadFromUsages(SampleUsages());

        // First row: flag 0x11 (magnitude-padded hex, X02 for <= 0xff — WF parity),
        // type EVENT_COND_TURN, info "Turn Conditions".
        Assert.Contains("0x11", list[0].name);
        Assert.Contains("EVENT_COND_TURN", list[0].name);
        Assert.Contains("Turn Conditions", list[0].name);
    }

    [Fact]
    public void LoadEntryByIndex_PopulatesDetail()
    {
        var vm = new ToolUseFlagViewModel();
        vm.LoadFromUsages(SampleUsages());

        vm.LoadEntryByIndex(0);

        Assert.True(vm.IsLoaded);
        Assert.Equal("0x11", vm.SelectedFlagText);
        Assert.Equal("EVENT_COND_TURN", vm.SelectedTypeText);
        Assert.Equal("Turn Conditions", vm.SelectedInfoText);
        Assert.Equal("0x00008000", vm.SelectedAddrText);
    }

    [Fact]
    public void EventScriptRow_DetailAddress_UsesTagNotTreeRoot()
    {
        // For EVENTSCRIPT, Addr is the event-tree ROOT and Tag is the actual
        // referencing COMMAND. The detail "Address" must show the command (Tag),
        // not the tree root (Addr) — mirrors WF GotoEvent → EventScriptForm.JumpTo
        // positioning at tag.
        var vm = new ToolUseFlagViewModel();
        // Addr = tree root 0x00009000, Tag = referencing command 0x00009040.
        var usages = new List<UseFlagIDCore>
        {
            new(FELintCore.Type.EVENTSCRIPT, 0x00009000, "", 0x0022, 0, 0x00009040),
        };
        vm.LoadFromUsages(usages);

        vm.LoadEntryByIndex(0);
        Assert.Equal("0x00009040", vm.SelectedAddrText); // Tag (command), not Addr (root)
    }

    [Fact]
    public void EventCondRow_DetailAddress_UsesAddr()
    {
        // For EVENT_COND_*/MAPCHANGE, Addr IS the record address and Tag is the
        // slot index (not an address), so the detail must show Addr.
        var vm = new ToolUseFlagViewModel();
        var usages = new List<UseFlagIDCore>
        {
            // Tag = 2 (slot index) must NOT be shown as the address.
            new(FELintCore.Type.EVENT_COND_ALWAYS, 0x0000B000, "Always", 0x0055, 0, 2),
        };
        vm.LoadFromUsages(usages);

        vm.LoadEntryByIndex(0);
        Assert.Equal("0x0000B000", vm.SelectedAddrText); // Addr (record), not Tag (slot idx)
    }

    [Fact]
    public void DuplicateAddressRows_ResolveByIndex_NotFirstMatch()
    {
        // Index 2 and index 3 share addr 0x0000A000 but carry different flag ids.
        // Index-keyed LoadEntryByIndex must show EACH row's own flag; address-keyed
        // LoadEntry collapses to the first match.
        var vm = new ToolUseFlagViewModel();
        vm.LoadFromUsages(SampleUsages());

        vm.LoadEntryByIndex(3);
        Assert.Equal("0x44", vm.SelectedFlagText);

        vm.LoadEntryByIndex(2);
        Assert.Equal("0x33", vm.SelectedFlagText);

        vm.LoadEntry(0x0000A000);
        Assert.Equal("0x33", vm.SelectedFlagText); // first match
    }

    [Fact]
    public void LoadEntryByIndex_OutOfRange_ClearsDetail()
    {
        var vm = new ToolUseFlagViewModel();
        vm.LoadFromUsages(SampleUsages());
        vm.LoadEntryByIndex(0);
        Assert.True(vm.IsLoaded);

        vm.LoadEntryByIndex(99);
        Assert.False(vm.IsLoaded);
        Assert.Equal("", vm.SelectedFlagText);
        Assert.Equal("", vm.SelectedTypeText);
        Assert.Equal("", vm.SelectedAddrText);
    }

    [Fact]
    public void Rescan_ClearsStaleDetail()
    {
        var vm = new ToolUseFlagViewModel();
        vm.LoadFromUsages(SampleUsages());
        vm.LoadEntryByIndex(0);
        Assert.True(vm.IsLoaded);

        vm.LoadFromUsages(new List<UseFlagIDCore>()); // rescan → empty chapter
        Assert.False(vm.IsLoaded);
        Assert.Equal("", vm.SelectedFlagText);
        Assert.Equal(0, vm.FlagUsageCount);
    }

    [Fact]
    public void LoadForChapter_NoRom_ReturnsEmptyAndDoesNotThrow()
    {
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;

            var vm = new ToolUseFlagViewModel();
            List<AddrResult> list = vm.LoadForChapter(0u);

            Assert.NotNull(list);
            Assert.Empty(list);
            Assert.Equal(0, vm.FlagUsageCount);
            Assert.False(string.IsNullOrEmpty(vm.SummaryText)); // still renders "Flags used: 0"
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    [Fact]
    public void LoadMapList_NoRom_ReturnsEmpty()
    {
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ToolUseFlagViewModel();
            Assert.Empty(vm.LoadMapList());
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }
}
