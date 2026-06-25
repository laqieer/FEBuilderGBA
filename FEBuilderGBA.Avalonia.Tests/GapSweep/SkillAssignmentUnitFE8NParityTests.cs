// SPDX-License-Identifier: GPL-3.0-or-later
// #1452: regression tests for SkillAssignmentUnitFE8NView.
//
// Before the fix the view was inert: it always showed a "no patch installed"
// warning and hid the editing panel even when an FE8N skill patch WAS present,
// and Write() no-opped on CurrentAddr==0. These tests prove that with an FE8N
// patch detected + a unit address navigated to, the view reveals the editor,
// loads/round-trips the unit's skill bytes, refreshes on a REUSED window
// (re-navigation), and that the parent Unit Editor re-syncs after the child
// closes so a later parent Write doesn't clobber the skill edit.
using System;
using System.IO;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillAssignmentUnitFE8NParityTests
{
    const uint UnitBase = 0x80000; // synthetic unit-table base for the test

    // ---- Synthetic FE8J ROM helpers ----

    static ROM MakeFE8JRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        byte[] version = System.Text.Encoding.ASCII.GetBytes("BE8J01");
        Array.Copy(version, 0, data, 0xAC, version.Length);
        rom.LoadLow("synth-fe8j.gba", data, "BE8J01");
        return rom;
    }

    static void PlantFE8NSignature(ROM rom)
    {
        byte[] sig = { 0x00, 0x4B, 0x9F, 0x46 };
        Array.Copy(sig, 0, rom.Data, 0x89268, sig.Length);
    }

    static void RefreshDetection() => PatchDetectionService.Instance.Refresh();

    // ---------------------------------------------------------------
    // ViewModel — LoadEntry / Write round-trip of the 3 skill bytes
    // (struct offsets 0x27/0x28/0x29 = B39/B40/B41).
    // ---------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntryWrite_RoundTrips_SkillBytes()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom();
            uint addr = UnitBase;
            rom.write_u8(addr + 39, 0x11); // PersonalSkill (B39)
            rom.write_u8(addr + 40, 0x22); // SkillSet1     (B40)
            rom.write_u8(addr + 41, 0x33); // SkillSet2     (B41)
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitFE8NViewViewModel();
            vm.LoadEntry(addr);
            Assert.Equal(addr, vm.CurrentAddr);
            Assert.Equal(0x11u, vm.PersonalSkill);
            Assert.Equal(0x22u, vm.SkillSet1);
            Assert.Equal(0x33u, vm.SkillSet2);

            vm.PersonalSkill = 0x44;
            vm.SkillSet1 = 0x55;
            vm.SkillSet2 = 0x66;
            vm.Write();

            Assert.Equal(0x44u, (uint)rom.u8(addr + 39));
            Assert.Equal(0x55u, (uint)rom.u8(addr + 40));
            Assert.Equal(0x66u, (uint)rom.u8(addr + 41));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_NoOps_WhenAddressZero()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom();
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitFE8NViewViewModel
            {
                CurrentAddr = 0,
                PersonalSkill = 0x77,
            };
            // Must not write to offset 39 of the ROM "0" base.
            vm.Write();
            Assert.Equal(0u, (uint)rom.u8(39));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ---------------------------------------------------------------
    // View — reveals editor when FE8N patch + address present (NavigateTo).
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void View_NavigateTo_WithFE8NPatch_RevealsEditor_HidesWarning()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom();
            PlantFE8NSignature(rom);
            rom.write_u8(UnitBase + 39, 0xAB);
            rom.write_u8(UnitBase + 40, 0xCD);
            rom.write_u8(UnitBase + 41, 0xEF);
            CoreState.ROM = rom;
            RefreshDetection();
            Assert.True(PatchDetectionService.Instance.HasSkillSystem);

            var view = new SkillAssignmentUnitFE8NView();
            view.NavigateTo(UnitBase);

            var fields = view.FindControl<Grid>("FieldsPanel");
            var writeBtn = view.FindControl<Button>("WriteButton");
            var warning = view.FindControl<Border>("WarningBorder");
            var personalBox = view.FindControl<NumericUpDown>("PersonalSkillBox");

            Assert.NotNull(fields);
            Assert.True(fields!.IsVisible, "FieldsPanel must be visible when FE8N patch present");
            Assert.True(writeBtn!.IsVisible, "WriteButton must be visible when FE8N patch present");
            Assert.False(warning!.IsVisible, "Warning must hide when FE8N patch present");
            // Box populated from the unit's bytes.
            Assert.Equal((decimal)0xAB, personalBox!.Value);
        }
        finally { CoreState.ROM = prevRom; PatchDetectionService.Instance.Refresh(); }
    }

    // ---------------------------------------------------------------
    // View — without a patch, warning stays and editor stays hidden.
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void View_NavigateTo_NoPatch_KeepsWarning_HidesEditor()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom(); // clean — no FE8N signature
            CoreState.ROM = rom;
            RefreshDetection();
            Assert.False(PatchDetectionService.Instance.HasSkillSystem);

            var view = new SkillAssignmentUnitFE8NView();
            view.NavigateTo(UnitBase);

            var fields = view.FindControl<Grid>("FieldsPanel");
            var warning = view.FindControl<Border>("WarningBorder");
            Assert.False(fields!.IsVisible, "FieldsPanel must stay hidden when no patch");
            Assert.True(warning!.IsVisible, "Warning must stay visible when no patch");
        }
        finally { CoreState.ROM = prevRom; PatchDetectionService.Instance.Refresh(); }
    }

    // ---------------------------------------------------------------
    // Copilot finding 1 — a REUSED window refreshes its fields on
    // re-navigation (NavigateTo runs even when Opened does not re-fire).
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void View_ReNavigate_RefreshesFields_NotStale()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom();
            PlantFE8NSignature(rom);
            uint addrA = UnitBase;
            uint addrB = UnitBase + 100;
            rom.write_u8(addrA + 39, 0x01);
            rom.write_u8(addrB + 39, 0x99);
            CoreState.ROM = rom;
            RefreshDetection();

            var view = new SkillAssignmentUnitFE8NView();
            view.NavigateTo(addrA);
            var personalBox = view.FindControl<NumericUpDown>("PersonalSkillBox");
            Assert.Equal((decimal)0x01, personalBox!.Value);

            // Re-navigate the SAME view to a different unit (window reuse path).
            view.NavigateTo(addrB);
            Assert.Equal((decimal)0x99, personalBox.Value);

            var addrLabel = view.FindControl<TextBlock>("AddrLabel");
            Assert.Equal($"0x{addrB:X08}", addrLabel!.Text);
        }
        finally { CoreState.ROM = prevRom; PatchDetectionService.Instance.Refresh(); }
    }

    // ---------------------------------------------------------------
    // Copilot finding 2 — after the child writes skill bytes, reloading
    // the parent Unit Editor VM picks up the change, so a later parent
    // Write preserves it (no stale-write clobber).
    // ---------------------------------------------------------------

    [Fact]
    public void ParentUnitEditor_ReloadsSkillBytes_AfterChildWrite()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeFE8JRom();
            uint addr = UnitBase;
            // Original ability/skill bytes.
            rom.write_u8(addr + 39, 0x10);
            rom.write_u8(addr + 40, 0x20);
            rom.write_u8(addr + 41, 0x30);
            CoreState.ROM = rom;

            // Parent loads the unit (caches Unk39 etc).
            var parent = new UnitEditorViewModel();
            parent.LoadUnit(addr);
            Assert.Equal(0x10u, parent.Unk39);

            // Child writes new PersonalSkill (B39).
            var child = new SkillAssignmentUnitFE8NViewViewModel();
            child.LoadEntry(addr);
            child.PersonalSkill = 0x7F;
            child.Write();
            Assert.Equal(0x7Fu, (uint)rom.u8(addr + 39));

            // Simulate the parent re-sync that EditSkills_Click does on child close.
            parent.LoadUnit(addr);
            Assert.Equal(0x7Fu, parent.Unk39);

            // A subsequent parent Write must now PRESERVE the skill byte.
            parent.WriteUnit();
            Assert.Equal(0x7Fu, (uint)rom.u8(addr + 39));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ---------------------------------------------------------------
    // View source — Navigate-with-address + parent reload wired.
    // ---------------------------------------------------------------

    [Fact]
    public void UnitEditorView_RoutesFE8N_WithNavigateAndReload()
    {
        string repoRoot = FindRepoRoot();
        string src = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "UnitEditorView.axaml.cs"));
        Assert.Contains("Navigate<SkillAssignmentUnitFE8NView>(_vm.CurrentAddr)", src);
        Assert.Contains("OnSkillEditorClosed", src);
    }

    [Fact]
    public void View_RefreshLogic_SharedBy_OpenedAndNavigateTo()
    {
        string repoRoot = FindRepoRoot();
        string src = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitFE8NView.axaml.cs"));
        // Single shared refresh helper used by both Opened and NavigateTo.
        Assert.Contains("void RefreshUiForCurrentAddress()", src);
        Assert.Contains("RefreshUiForCurrentAddress();", src);
    }

    [Fact]
    public void DeadDuplicateViewModel_Removed()
    {
        string repoRoot = FindRepoRoot();
        string dead = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillAssignmentUnitFE8NViewModel.cs");
        Assert.False(File.Exists(dead),
            "The dead duplicate SkillAssignmentUnitFE8NViewModel.cs must be removed (#1452).");
    }

    // ---- Helpers ----

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln");
        return dir;
    }
}
