// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for the SoundFootSteps "List Expansion" parity raise
// (#1449): the Avalonia editor must expose the Switch2-table expansion +
// the FE8 PlaySoundStepByClass hardcode fix that previously lived only in
// WinForms SoundFootStepsForm.SwitchListExpandsButton_Click.
using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Proves the SoundFootSteps List Expansion parity raise (#1449) is permanent.
/// Marked [Collection("SharedState")] because list-loader tests mutate
/// CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class SoundFootStepsParityTests
{
    const string FE8U_CODE = "BE8E01";

    // -----------------------------------------------------------------
    // View structure — the List Expansion button + not-found label exist
    // and the click handler is wired to the VM.
    // -----------------------------------------------------------------

    [Fact]
    public void View_Axaml_HasListExpansionButtonAndNotFoundLabel()
    {
        string axaml = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SoundFootStepsViewerView.axaml"));

        Assert.Contains("SoundFootStepsViewer_SwitchListExpands_Button", axaml);
        Assert.Contains("Click=\"SwitchListExpands_Click\"", axaml);
        Assert.Contains("SoundFootStepsViewer_NotFound_Label", axaml);
    }

    [Fact]
    public void View_CodeBehind_ExpandHandler_CallsVmExpandList()
    {
        string code = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SoundFootStepsViewerView.axaml.cs"));

        Assert.Contains("void SwitchListExpands_Click", code);
        // The handler must delegate to the VM expansion method.
        Assert.Contains("_vm.ExpandList(", code);
        // And it must drive the expansion under an undo scope.
        Assert.Contains("_undoService.Begin(", code);
        Assert.Contains("GetActiveUndoData()", code);
        // The list-expansion newCount comes from the class count helper.
        Assert.Contains("_vm.GetNewCount()", code);
    }

    [Fact]
    public void View_CodeBehind_GatesButtonsOnSwitch2Enable()
    {
        string code = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SoundFootStepsViewerView.axaml.cs"));

        Assert.Contains("RefreshEnableState()", code);
        Assert.Contains("SwitchListExpandsButton.IsVisible", code);
        Assert.Contains("NotFoundLabel.IsVisible", code);
    }

    // -----------------------------------------------------------------
    // ViewModel surface — the new methods exist with the expected shapes.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExposesExpandListMethod()
    {
        MethodInfo? m = typeof(SoundFootStepsViewerViewModel).GetMethod(
            "ExpandList", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(m);
        Assert.Equal(typeof(uint), m!.ReturnType);
        var ps = m.GetParameters();
        Assert.Equal(3, ps.Length);
        Assert.Equal(typeof(uint), ps[0].ParameterType); // newCount
        Assert.Equal(typeof(uint), ps[1].ParameterType); // defaultJumpAddr
        Assert.Equal(typeof(Undo.UndoData), ps[2].ParameterType); // undo
    }

    [Fact]
    public void ViewModel_ExposesEnableAndCountHelpers()
    {
        var vmType = typeof(SoundFootStepsViewerViewModel);
        Assert.NotNull(vmType.GetProperty("IsSwitch2Enabled"));
        Assert.NotNull(vmType.GetMethod("RefreshEnableState"));
        Assert.NotNull(vmType.GetMethod("GetNewCount"));
    }

    // -----------------------------------------------------------------
    // List-loader parity — switch2 count + 1 sizing, NOT first-non-pointer.
    // A NULL pointer between two valid entries must stay in the list.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ListLoader_UsesSwitch2Count_NotNullScan()
    {
        var rom = MakeFe8uWithSwitch2NullGap();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SoundFootStepsViewerViewModel();
            var rows = vm.LoadSoundFootStepsList();

            // Switch2 encodes count-1 = 4 -> 5 rows, even though row index 2 is
            // a NULL pointer that the legacy first-non-pointer scan would have
            // truncated at.
            Assert.Equal(5, rows.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RefreshEnableState_TrueWhenSwitch2Present()
    {
        var rom = MakeFe8uWithSwitch2NullGap();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SoundFootStepsViewerViewModel();
            vm.RefreshEnableState();
            Assert.True(vm.IsSwitch2Enabled);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeFe8uWithSwitch2NullGap()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, FE8U_CODE);

        uint switchAddr = rom.RomInfo.sound_foot_steps_switch2_address;
        uint ptrSlot = rom.RomInfo.sound_foot_steps_pointer;

        // Switch2 signature: start=0, SUB op, count-1 = 4 (-> 5 entries), CMP op.
        bytes[switchAddr + 0] = 0x00;
        bytes[switchAddr + 1] = 0x38;
        bytes[switchAddr + 2] = 0x04;
        bytes[switchAddr + 3] = 0x28;

        // 5-entry table with a NULL gap at index 2.
        uint tableAddr = 0x00800000u;
        BitConverter.GetBytes(0x08000100u).CopyTo(bytes, tableAddr + 0 * 4);
        BitConverter.GetBytes(0x08000200u).CopyTo(bytes, tableAddr + 1 * 4);
        BitConverter.GetBytes(0x00000000u).CopyTo(bytes, tableAddr + 2 * 4); // NULL gap
        BitConverter.GetBytes(0x08000300u).CopyTo(bytes, tableAddr + 3 * 4);
        BitConverter.GetBytes(0x08000400u).CopyTo(bytes, tableAddr + 4 * 4);
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, ptrSlot);

        rom.LoadLow("synthetic-fe8u.gba", bytes, FE8U_CODE);
        return rom;
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
