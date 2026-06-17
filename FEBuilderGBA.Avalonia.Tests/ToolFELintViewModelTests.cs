using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless tests for <see cref="ToolFELintViewModel"/> (issue #1168 — port of WinForms
/// <c>ToolFELintForm</c>). The ViewModel reuses the cross-platform <see cref="FELintScanner"/>
/// — the SAME scanner the CLI <c>--lint</c> command runs — and is read-only: it never mutates
/// the ROM. Detail resolution is INDEX-keyed so duplicate-address findings each show their own
/// message.
///
/// The selected-detail and duplicate-address paths are covered DETERMINISTICALLY via the
/// <c>LoadFromErrors</c> test seam (injecting synthetic findings), because vanilla retail ROMs
/// are structurally valid and legitimately produce 0 findings — so a ROM-only test would skip
/// those assertions in the common/CI state.
/// </summary>
[Collection("SharedState")]
public class ToolFELintViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ToolFELintViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed test.");
            return true;
        }
        return false;
    }

    // ---- Synthetic finding sets (ROM-independent) ----

    static FELintCore.ErrorSt Err(uint addr, string msg, FELintCore.ErrorType sev = FELintCore.ErrorType.ERROR)
        => new(FELintCore.Type.ROM_HEADER, addr, msg, sev);

    static List<FELintCore.ErrorSt> SampleErrors() => new()
    {
        Err(0x000000B2, "header fixed byte wrong", FELintCore.ErrorType.ERROR),
        Err(0x00008000, "unit name bad", FELintCore.ErrorType.WARNING),
        // Two rows sharing an address but with DIFFERENT messages — the duplicate-address case.
        Err(FELintCore.SYSTEM_MAP_ID, "global problem A", FELintCore.ErrorType.ERROR),
        Err(FELintCore.SYSTEM_MAP_ID, "global problem B", FELintCore.ErrorType.ERROR),
    };

    // ---- ROM-backed tests (real scanner) ----

    [Fact]
    public void LoadList_WithRom_ReturnsListAndDoesNotThrow()
    {
        if (Skip()) return;

        var vm = new ToolFELintViewModel();
        List<AddrResult> list = vm.LoadList();

        // Always non-null; never throws. A clean ROM yields a single "no problems" row,
        // a dirty ROM yields one row per finding.
        Assert.NotNull(list);
        Assert.NotEmpty(list);

        if (vm.HasErrors)
        {
            Assert.Equal(vm.ErrorListCount, list.Count);
            Assert.Equal(vm.ErrorCount + vm.WarningCount, vm.ErrorListCount);
        }
        else
        {
            Assert.Equal(0, vm.ErrorListCount);
            Assert.Single(list);
        }
    }

    [Fact]
    public void SummaryText_WithRom_ReflectsCounts()
    {
        if (Skip()) return;

        var vm = new ToolFELintViewModel();
        vm.LoadList();

        Assert.False(string.IsNullOrEmpty(vm.SummaryText));
        Assert.Contains(vm.ErrorCount.ToString(), vm.SummaryText);
        Assert.Contains(vm.WarningCount.ToString(), vm.SummaryText);
    }

    [Fact]
    public void LoadList_NoRom_ReturnsEmptyAndDoesNotThrow()
    {
        // Independent of fixture availability: explicitly null out the ROM, then restore.
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;

            var vm = new ToolFELintViewModel();
            List<AddrResult> list = vm.LoadList();

            Assert.NotNull(list);
            Assert.Empty(list);
            Assert.False(vm.HasErrors);
            Assert.Equal(0, vm.ErrorCount);
            Assert.Equal(0, vm.WarningCount);
            Assert.False(string.IsNullOrEmpty(vm.SummaryText)); // summary still renders "0 / 0"
        }
        finally
        {
            // Restore so we don't poison later tests in the SharedState collection.
            CoreState.ROM = prev;
        }
    }

    // ---- Deterministic tests (synthetic findings via the LoadFromErrors seam) ----

    [Fact]
    public void LoadFromErrors_BuildsRowsAndCounts()
    {
        var vm = new ToolFELintViewModel();
        List<AddrResult> list = vm.LoadFromErrors(SampleErrors());

        Assert.Equal(4, list.Count);
        Assert.Equal(4, vm.ErrorListCount);
        Assert.True(vm.HasErrors);
        Assert.Equal(3, vm.ErrorCount);   // three ERROR rows
        Assert.Equal(1, vm.WarningCount); // one WARNING row
        Assert.Contains("3", vm.SummaryText);
        Assert.Contains("1", vm.SummaryText);
    }

    [Fact]
    public void LoadEntryByIndex_PopulatesDetail()
    {
        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());

        vm.LoadEntryByIndex(0);

        Assert.True(vm.IsLoaded);
        Assert.Equal("header fixed byte wrong", vm.SelectedMessage);
        Assert.False(string.IsNullOrEmpty(vm.SelectedSeverityText));
        Assert.False(string.IsNullOrEmpty(vm.SelectedCategoryText));
        // Non-sentinel address renders as a hex offset.
        Assert.Equal("0x000000B2", vm.SelectedAddrText);
    }

    [Fact]
    public void LoadEntryByIndex_SystemSentinel_RendersAsSystem()
    {
        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());

        vm.LoadEntryByIndex(2); // first SYSTEM_MAP_ID row
        Assert.True(vm.IsLoaded);
        Assert.Equal("(system)", vm.SelectedAddrText);
    }

    [Fact]
    public void DuplicateAddressRows_ResolveByIndex_NotFirstMatch()
    {
        // Regression guard for the Copilot review finding: index 2 and index 3 share
        // SYSTEM_MAP_ID but carry different messages. Index-keyed LoadEntryByIndex must show
        // EACH row's own message, while address-keyed LoadEntry collapses to the first match.
        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());

        vm.LoadEntryByIndex(3);
        Assert.Equal("global problem B", vm.SelectedMessage);

        vm.LoadEntryByIndex(2);
        Assert.Equal("global problem A", vm.SelectedMessage);

        // Address-keyed lookup always lands on the FIRST row with that address.
        vm.LoadEntry(FELintCore.SYSTEM_MAP_ID);
        Assert.Equal("global problem A", vm.SelectedMessage);
    }

    [Fact]
    public void LoadEntryByIndex_OutOfRange_ClearsDetail()
    {
        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());
        vm.LoadEntryByIndex(0);
        Assert.True(vm.IsLoaded);

        vm.LoadEntryByIndex(99); // e.g. the synthetic "no problems" row index on a clean ROM
        Assert.False(vm.IsLoaded);
        Assert.Equal("", vm.SelectedMessage);
        Assert.Equal("", vm.SelectedSeverityText);
        Assert.Equal("", vm.SelectedAddrText);
    }

    [Fact]
    public void Rescan_ClearsStaleDetail()
    {
        // A previous scan's selected detail must NOT survive a rescan that yields no findings —
        // the address list raises no selection-changed event for an empty list.
        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());
        vm.LoadEntryByIndex(0);
        Assert.True(vm.IsLoaded);

        vm.LoadFromErrors(new List<FELintCore.ErrorSt>()); // rescan → clean
        Assert.False(vm.IsLoaded);
        Assert.Equal("", vm.SelectedMessage);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void TryGetJumpOffset_GatesNonJumpableRows()
    {
        if (Skip()) return; // needs a real ROM for the data-length bound

        var vm = new ToolFELintViewModel();
        vm.LoadFromErrors(SampleErrors());

        // Out-of-range index is never jumpable.
        Assert.False(vm.TryGetJumpOffset(-1, out _));
        Assert.False(vm.TryGetJumpOffset(vm.ErrorListCount, out _));

        // Header-region finding (0xB2) IS jumpable — the Hex Editor displays any in-ROM byte
        // (regression for the Copilot finding that isSafetyOffset wrongly excluded 0x0–0x1FF).
        Assert.True(vm.TryGetJumpOffset(0, out uint headerOffset));
        Assert.Equal(0x000000B2u, headerOffset);

        // A normal in-ROM finding is jumpable.
        Assert.True(vm.TryGetJumpOffset(1, out uint normalOffset));
        Assert.Equal(0x00008000u, normalOffset);

        // The SYSTEM_MAP_ID sentinel never jumps.
        Assert.False(vm.TryGetJumpOffset(2, out _));
        Assert.False(vm.TryGetJumpOffset(3, out _));
    }
}
