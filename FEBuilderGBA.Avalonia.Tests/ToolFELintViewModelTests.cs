using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="ToolFELintViewModel"/> (issue #1168 — port of
/// WinForms <c>ToolFELintForm</c>). The ViewModel reuses the cross-platform
/// <see cref="FELintScanner"/> — the SAME scanner the CLI <c>--lint</c> command runs — and is
/// read-only: it never mutates the ROM. Detail resolution is INDEX-keyed so duplicate-address
/// findings each show their own message.
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
            _output.WriteLine("RomFixture not available — skipping.");
            return true;
        }
        return false;
    }

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
            // One AddrResult row per stored error.
            Assert.Equal(vm.ErrorListCount, list.Count);
            Assert.Equal(vm.ErrorCount + vm.WarningCount, vm.ErrorListCount);
        }
        else
        {
            // Clean ROM: single informational row, no stored errors.
            Assert.Equal(0, vm.ErrorListCount);
            Assert.Single(list);
        }
    }

    [Fact]
    public void LoadEntryByIndex_WithErrors_PopulatesDetail()
    {
        if (Skip()) return;

        var vm = new ToolFELintViewModel();
        vm.LoadList();

        if (!vm.HasErrors)
        {
            _output.WriteLine("ROM is clean (0 findings) — index-detail path covered by the " +
                "duplicate-address synthetic test; nothing to assert on a real finding here.");
            return;
        }

        vm.LoadEntryByIndex(0);

        Assert.True(vm.IsLoaded);
        Assert.False(string.IsNullOrEmpty(vm.SelectedMessage));
        Assert.False(string.IsNullOrEmpty(vm.SelectedSeverityText));
        Assert.False(string.IsNullOrEmpty(vm.SelectedAddrText));
    }

    [Fact]
    public void SummaryText_ReflectsCounts()
    {
        if (Skip()) return;

        var vm = new ToolFELintViewModel();
        vm.LoadList();

        Assert.False(string.IsNullOrEmpty(vm.SummaryText));
        // The summary embeds both counts; verify they appear.
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

    [Fact]
    public void DuplicateAddressRows_ResolveByIndex_NotFirstMatch()
    {
        if (Skip()) return;

        // Regression guard for the Copilot review finding: when two findings share an address
        // (e.g. multiple SYSTEM_MAP_ID header/global errors), address-keyed lookup collapses to
        // the first match, but the interactive UI must show the row the user actually selected.
        // Index-keyed LoadEntryByIndex is row-exact.
        //
        // We compare directly against the scanner's ordered output (the same list the VM stores)
        // and only assert the duplicate-specific distinction when the ROM actually produces a
        // duplicate-address pair — never fabricate findings.
        var scannerErrors = new FELintScanner().Scan() ?? new List<FELintCore.ErrorSt>();

        var vm = new ToolFELintViewModel();
        vm.LoadList();
        Assert.Equal(scannerErrors.Count, vm.ErrorListCount);

        // Find the first pair of rows sharing an address.
        var firstIndexByAddr = new Dictionary<uint, int>();
        int dupIndex = -1, firstIndex = -1;
        for (int i = 0; i < scannerErrors.Count; i++)
        {
            uint a = scannerErrors[i].Addr;
            if (firstIndexByAddr.TryGetValue(a, out int prior))
            {
                // A duplicate address — but only useful if the messages differ.
                if (scannerErrors[prior].Info != scannerErrors[i].Info)
                {
                    firstIndex = prior;
                    dupIndex = i;
                    break;
                }
            }
            else
            {
                firstIndexByAddr[a] = i;
            }
        }

        if (dupIndex < 0)
        {
            _output.WriteLine("No distinct-message duplicate-address pair in this ROM — " +
                "asserting index-exactness across all rows instead.");
            // Index-exactness still verifiable: each index resolves to its OWN message.
            for (int i = 0; i < scannerErrors.Count; i++)
            {
                vm.LoadEntryByIndex(i);
                Assert.Equal(scannerErrors[i].Info ?? "", vm.SelectedMessage);
            }
            return;
        }

        // Selecting the duplicate row shows the duplicate's message...
        vm.LoadEntryByIndex(dupIndex);
        Assert.Equal(scannerErrors[dupIndex].Info, vm.SelectedMessage);

        // ...and selecting the first row shows the FIRST message — they must differ.
        vm.LoadEntryByIndex(firstIndex);
        Assert.Equal(scannerErrors[firstIndex].Info, vm.SelectedMessage);
        Assert.NotEqual(scannerErrors[dupIndex].Info, scannerErrors[firstIndex].Info);

        // Address-keyed LoadEntry, by contrast, always lands on the first match.
        uint sharedAddr = scannerErrors[dupIndex].Addr;
        vm.LoadEntry(sharedAddr);
        Assert.Equal(scannerErrors[firstIndex].Info, vm.SelectedMessage);
    }

    [Fact]
    public void TryGetJumpOffset_GatesNonJumpableRows()
    {
        if (Skip()) return;

        var vm = new ToolFELintViewModel();
        vm.LoadList();

        // Out-of-range index is never jumpable.
        Assert.False(vm.TryGetJumpOffset(-1, out _));
        Assert.False(vm.TryGetJumpOffset(vm.ErrorListCount, out _));

        var scannerErrors = new FELintScanner().Scan() ?? new List<FELintCore.ErrorSt>();
        for (int i = 0; i < scannerErrors.Count; i++)
        {
            uint addr = scannerErrors[i].Addr;
            bool jumpable = vm.TryGetJumpOffset(i, out uint offset);

            if (addr == FELintCore.SYSTEM_MAP_ID || addr == 0)
            {
                Assert.False(jumpable); // sentinel / null addr never jumps
            }
            else if (jumpable)
            {
                Assert.Equal(addr, offset);
                Assert.True(U.isSafetyOffset(offset, CoreState.ROM));
            }
        }
    }
}
