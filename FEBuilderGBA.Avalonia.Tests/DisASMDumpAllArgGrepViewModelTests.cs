using System.Linq;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// #1463 — tests for <see cref="DisASMDumpAllArgGrepViewModel"/>.
///
/// The Avalonia Disassembly Argument Grep view previously surfaced none of the
/// 5 WinForms options. These tests pin the VM defaults that drive the newly
/// wired controls: the r0..r8 register list (verbatim WinForms Designer), the
/// allowed-rows default of 5 and bounds 1..20, and field round-trip.
/// </summary>
public class DisASMDumpAllArgGrepViewModelTests
{
    [Fact]
    public void RegisterItems_AreR0ThroughR8_VerbatimWinForms()
    {
        var vm = new DisASMDumpAllArgGrepViewModel();
        Assert.Equal(new[] { "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8" },
            vm.RegisterItems.ToArray());
    }

    [Fact]
    public void AllowedRows_DefaultsToFive_MatchesWinFormsDesigner()
    {
        var vm = new DisASMDumpAllArgGrepViewModel();
        Assert.Equal(5, vm.AllowedRows);
    }

    [Fact]
    public void AllowedRows_BoundsAreOneToTwenty_MatchesWinFormsNumericUpDown()
    {
        var vm = new DisASMDumpAllArgGrepViewModel();
        Assert.Equal(1, vm.AllowedRowsMinimum);
        Assert.Equal(20, vm.AllowedRowsMaximum);
    }

    [Fact]
    public void Fields_RoundTrip()
    {
        var vm = new DisASMDumpAllArgGrepViewModel
        {
            TargetFunctionAddress = "m4aSongNumStart",
            SearchRegisterIndex = 3,
            AllowedRows = 12,
            HideFunctionCalls = true,
            HideUnknownArgs = true,
        };

        Assert.Equal("m4aSongNumStart", vm.TargetFunctionAddress);
        Assert.Equal(3, vm.SearchRegisterIndex);
        Assert.Equal(12, vm.AllowedRows);
        Assert.True(vm.HideFunctionCalls);
        Assert.True(vm.HideUnknownArgs);
    }

    [Fact]
    public void Initialize_SetsIsLoaded()
    {
        var vm = new DisASMDumpAllArgGrepViewModel();
        vm.Initialize();
        Assert.True(vm.IsLoaded);
    }
}
