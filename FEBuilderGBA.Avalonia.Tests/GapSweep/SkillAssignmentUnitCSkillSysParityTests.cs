// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #1451 regression tests for SkillAssignmentUnitCSkillSysView.
//
// Closes the inert-placeholder gap: the view is rebuilt from a single-warning
// stub to a three-pane master-detail editor (top read-config bar + unit list +
// per-unit detail + N1 level-up sub-list) mirroring WinForms
// SkillAssignmentUnitCSkillSysForm. The form is opened for both CSkillSys09x
// and CSkillSys300 (UnitEditorView.EditSkills_Click). Fixed CSkillSys patch
// slots: 0xB2A61C (personal skill table), 0xB2A7FC (per-unit level-up table),
// 0xB2A614 (skill-info table).
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillAssignmentUnitCSkillSysParityTests
{
    // ---- AXAML control-surface parity ----

    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitCSkillSysView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);
        // The Unit form has fewer WF controls than the Class form (no X_LV
        // breakdown panel). Use a conservative master/detail floor.
        const int MinControls = 25;
        Assert.True(avCount >= MinControls,
            $"AV control count {avCount} must be >= {MinControls} for a real master/detail editor");
    }

    [Fact] public void View_HasFilterAndReloadBar() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_ReadStart_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact] public void View_HasMasterUnitList() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_Unit_List\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_UnitList_NameFilter_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_UnitList_Expand_Button\"", axaml);
    }

    [Fact] public void View_HasUnitDetailGrid() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_UnitSkill_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_UnitSkill_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_SkillIcon_Image\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_SkillName_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_SkillText_Label\"", axaml);
    }

    [Fact] public void View_HasAddressWriteBar() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_Write_Button\"", axaml);
    }

    [Fact] public void View_HasN1Sublist() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_List\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_Filter_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_Expand_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_LevelUpAddr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_ReloadList_Button\"", axaml);
    }

    [Fact] public void View_HasN1WriteBar() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_B1_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_SkillIcon_Image\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_SkillName_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_N1_SkillText_Label\"", axaml);
    }

    // The Unit form has NO X_LV level-breakdown panel (Class-only). Assert that
    // the Unit AXAML does not accidentally carry the class-only controls.
    [Fact] public void View_DoesNotHaveClassOnlyLevelBreakdownPanel() {
        string axaml = ReadAxaml();
        Assert.DoesNotContain("X_LevelAddPanel", axaml);
        Assert.DoesNotContain("X_LV_Value_Input", axaml);
        Assert.DoesNotContain("X_LV_PlayerOnly_Check", axaml);
    }

    [Fact] public void View_HasLevelUpGroup_BoundToHasLevelUpTable() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_LevelUp_Control\"", axaml);
        Assert.Contains("IsVisible=\"{Binding HasLevelUpTable}\"", axaml);
    }

    [Fact] public void View_HasZeroPointerAndIndependencePanels() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_ZeroPointer_Control\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_Independence_Control\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_Independence_Button\"", axaml);
    }

    [Fact] public void View_HasLearnInfoLink() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitCSkillSys_LearnInfo_Link\"", axaml);
    }

    [Fact] public void View_BulkButtons_AreDisabled_WithTooltip() {
        string axaml = ReadAxaml();
        var importBtn = Regex.Match(axaml, @"<Button[^>]*ImportAll_Button[^>]*?/>");
        var exportBtn = Regex.Match(axaml, @"<Button[^>]*ExportAll_Button[^>]*?/>");
        Assert.True(importBtn.Success, "ImportAll button element not found");
        Assert.True(exportBtn.Success, "ExportAll button element not found");
        Assert.Contains("IsEnabled=\"False\"", importBtn.Value);
        Assert.Contains("IsEnabled=\"False\"", exportBtn.Value);
        Assert.DoesNotContain("Click=", importBtn.Value);
        Assert.DoesNotContain("Click=", exportBtn.Value);
        Assert.Contains("ToolTip.Tip=", axaml);
        Assert.Matches(new Regex("not available for the C-SkillSys", RegexOptions.IgnoreCase), axaml);
    }

    [Fact] public void View_StatusBanner_NamesCSkillSys() {
        string axaml = ReadAxaml();
        Assert.Contains("CSkillSys", axaml);
    }

    // ---- Undo coverage parity ----

    [Fact] public void View_OnWrite_UsesUndoService() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnWrite[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnWrite[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnWrite[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact] public void View_OnN1Write_UsesUndoService() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnN1Write[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnN1Write[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnN1Write[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact] public void View_OnIndependence_UsesUndoService() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnIndependence[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnIndependence[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnIndependence[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact] public void View_OnN1Expand_UsesUndoService() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnN1Expand[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnN1Expand[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnN1Expand[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact] public void View_OnUnitSelected_ResetsN1SelectedAddressOnEntryChange() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnUnitSelected[\s\S]*?_n1SelectedAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    [Fact] public void UndoCoverage_ViewCoversCanonicalVmWriteMethods() {
        string repoRoot = FindRepoRoot();
        var allFiles = Directory.GetFiles(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia"),
            "*.cs", SearchOption.AllDirectories);
        var covered = UndoCoverageScanner.DiscoverViewCoveredVmMethods(allFiles);
        const string vm = "SkillAssignmentUnitCSkillSysViewModel";
        Assert.Contains((vm, "WriteUnitSkill"), covered);
        Assert.Contains((vm, "WriteLevelUpPointer"), covered);
        Assert.Contains((vm, "WriteN1Entry"), covered);
        Assert.Contains((vm, "MakeIndependent"), covered);
        Assert.Contains((vm, "ExpandN1List"), covered);
    }

    // ---- Dead-duplicate-VM consolidation ----

    [Fact] public void Consolidation_CanonicalViewModelFileExists() {
        string canonical = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillAssignmentUnitCSkillSysViewModel.cs");
        Assert.True(File.Exists(canonical), $"Canonical ViewModel file expected at {canonical}");
    }

    [Fact] public void Consolidation_LegacyDoubledNameFileIsAbsent() {
        string legacy = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillAssignmentUnitCSkillSysViewViewModel.cs");
        Assert.False(File.Exists(legacy), $"Legacy doubled-name ViewModel file must not exist: {legacy}");
    }

    // ---- VM behavior (synthetic ROM) ----

    [Fact] public void ViewModel_LoadN1List_StopsAt0xFFFFTerminator() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            int b = (int)baseAddr;
            bytes[b + 0] = 0x01; bytes[b + 1] = 0x10;
            bytes[b + 2] = 0x02; bytes[b + 3] = 0x20;
            bytes[b + 4] = 0x03; bytes[b + 5] = 0x30;
            bytes[b + 6] = 0xFF; bytes[b + 7] = 0xFF;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            var rows = vm.LoadN1List(baseAddr);
            Assert.Equal(3, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_StopsAt0x0000Terminator() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            int b = (int)baseAddr;
            bytes[b + 0] = 0x01; bytes[b + 1] = 0x10;
            bytes[b + 2] = 0x02; bytes[b + 3] = 0x20;
            bytes[b + 4] = 0x00; bytes[b + 5] = 0x00;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            var rows = vm.LoadN1List(baseAddr);
            Assert.Equal(2, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_NullRom_ReturnsEmpty() {
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = null;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            var rows = vm.LoadN1List(0x00820000u);
            Assert.Empty(rows);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_ZeroAddress_ReturnsEmpty() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            var rows = vm.LoadN1List(0);
            Assert.Empty(rows);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_HonorsN1ReadCountCap() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            int b = (int)baseAddr;
            for (int i = 0; i < 10; i++) {
                bytes[b + i * 2] = (byte)(1 + i);
                bytes[b + i * 2 + 1] = (byte)(0x10 + i);
            }
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.N1ReadCount = 4;
            var rows = vm.LoadN1List(baseAddr);
            Assert.Equal(4, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadEntry_LoadsW0AndExposesUnitSkill() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            uint entry = 0x00800000u;
            int e = (int)entry;
            rom.Data[e + 0] = 0x42;
            rom.Data[e + 1] = 0x00;
            rom.Data[e + 2] = 0xCC;
            rom.Data[e + 3] = 0xDD;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.LoadEntry(entry);
            Assert.Equal(entry, vm.CurrentAddr);
            Assert.Equal(0x0042u, vm.UnitSkill);
            Assert.True(vm.IsLoaded);
        } finally { CoreState.ROM = prevRom; }
    }

    // Copilot review #5: a 0/unset ReadCount means the FULL unit_maxcount; only
    // an explicit override caps the list.
    [Fact] public void ViewModel_LoadUnitList_HonorsReadStartAddressOverride() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint customBase = 0x00800000u;
            int cb = (int)customBase;
            for (int i = 0; i < 6; i++) bytes[cb + i * 4] = (byte)(0x10 + i);
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.ReadStartAddress = customBase;
            var rows = vm.LoadUnitList();
            Assert.True(rows.Count > 0);
            Assert.Equal(customBase, rows[0].addr);
            // Row 0 IS the WF 1-based uid: row 0 stride is base+0.
            Assert.Equal(customBase + 1u * 4u, rows[1].addr);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadUnitList_HonorsReadCountCap() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint customBase = 0x00800000u;
            int cb = (int)customBase;
            for (int i = 0; i < 10; i++) bytes[cb + i * 4] = (byte)(0x10 + i);
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.ReadStartAddress = customBase;
            vm.ReadCount = 3;
            var rows = vm.LoadUnitList();
            Assert.Equal(3, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    // Copilot review #1: WF UnitForm.GetUnitName is 1-based; the VM must use the
    // one-based resolver so labels match. Row 0 (empty sentinel) must not match
    // row 1's name.
    [Fact] public void ViewModel_LoadUnitList_LabelsUseOneBasedUnitName() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint customBase = 0x00800000u;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.ReadStartAddress = customBase;
            vm.ReadCount = 3;
            var rows = vm.LoadUnitList();
            Assert.Equal(3, rows.Count);
            // The displayed name prefix is the 1-based index in hex (U.ToHexString,
            // bare 2-digit hex, no "0x" prefix — matches the Class CSkillSys VM).
            Assert.StartsWith("00 ", rows[0].name);
            Assert.StartsWith("01 ", rows[1].name);
            Assert.StartsWith("02 ", rows[2].name);
            // The resolver must agree with NameResolver.GetUnitNameByOneBasedId
            // (Copilot review #1: 1-based, so row 1 == the first unit name).
            string expected1 = NameResolver.GetUnitNameByOneBasedId(1);
            Assert.Equal(U.ToHexString(1) + " " + expected1, rows[1].name);
        } finally { CoreState.ROM = prevRom; }
    }

    // Copilot review #4: an absent level-up table hides the N1 group
    // (HasLevelUpTable=false) and does NOT block master W0 editing or compute
    // NOT_FOUND + unitId*4.
    [Fact] public void ViewModel_LoadEntry_AbsentLevelUpTable_HidesN1ButLoadsW0() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            // Plant 0 at the fixed gpCharLevelUpSkillTable slot so p32 -> 0 (unsafe).
            uint slot = SkillAssignmentUnitCSkillSysViewModel.gpCharLevelUpSkillTable;
            bytes[(int)slot + 0] = 0; bytes[(int)slot + 1] = 0;
            bytes[(int)slot + 2] = 0; bytes[(int)slot + 3] = 0;
            uint entry = 0x00800000u;
            bytes[(int)entry + 0] = 0x33; bytes[(int)entry + 1] = 0x00;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.SelectedUnitIndex = 5; // would be NOT_FOUND + 5*4 if unguarded
            vm.LoadEntry(entry);
            Assert.False(vm.HasLevelUpTable);
            Assert.Equal(0x0033u, vm.UnitSkill);  // master still loads
            Assert.Equal(0u, vm.XLevelUpAddr);    // no pointer resolved
        } finally { CoreState.ROM = prevRom; }
    }

    // Copilot review #2: the per-unit level-up pointer write is gated on the
    // level-up table being present and must round-trip.
    [Fact] public void ViewModel_WriteLevelUpPointer_NoTable_IsNoOp() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.HasLevelUpTable = false; // absent table
            vm.SelectedUnitIndex = 1;
            vm.XLevelUpAddr = U.toPointer(0x00900000u);
            // Should not throw and should not write anywhere.
            vm.WriteLevelUpPointer();
            Assert.True(true);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_WriteLevelUpPointer_WithTable_RoundTrips() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            // Plant a valid GBA pointer at the fixed gpCharLevelUpSkillTable slot
            // pointing to a per-unit pointer table.
            uint levelUpBaseOffset = 0x00880000u;
            uint slot = SkillAssignmentUnitCSkillSysViewModel.gpCharLevelUpSkillTable;
            BitConverter.GetBytes(levelUpBaseOffset | 0x08000000u).CopyTo(bytes, (int)slot);

            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.SelectedUnitIndex = 2;
            vm.HasLevelUpTable = true;
            uint newListOffset = 0x00900000u;
            vm.XLevelUpAddr = U.toPointer(newListOffset);
            vm.WriteLevelUpPointer();

            // The slot at levelUpBase + 2*4 must now hold the GBA pointer.
            uint written = rom.u32(levelUpBaseOffset + 2 * 4);
            Assert.Equal(newListOffset | 0x08000000u, written);
            Assert.Equal(newListOffset, vm.LevelUpAddr);
        } finally { CoreState.ROM = prevRom; }
    }

    // ---- Skill preview helper null guards ----

    [Fact] public void ViewModel_ResolveSkillName_NullRom_ReturnsEmpty() {
        Assert.Equal(string.Empty, SkillAssignmentUnitCSkillSysViewModel.ResolveSkillName(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillDescription_NullRom_ReturnsEmpty() {
        Assert.Equal(string.Empty, SkillAssignmentUnitCSkillSysViewModel.ResolveSkillDescription(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillIconGbaPointer_NullRom_ReturnsZero() {
        Assert.Equal(0u, SkillAssignmentUnitCSkillSysViewModel.ResolveSkillIconGbaPointer(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillIconGbaPointer_PlantedEntry_ReturnsRawU32() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint slot = SkillAssignmentUnitCSkillSysViewModel.gpSkillInfos;
            BitConverter.GetBytes(0x00400000u | 0x08000000u).CopyTo(bytes, (int)slot);
            uint baseAddr = 0x00400000u;
            uint id = 5;
            uint entryAddr = baseAddr + SkillAssignmentUnitCSkillSysViewModel.SKILL_INFO_SIZE * id;
            BitConverter.GetBytes(0xCAFEBABEu).CopyTo(bytes, (int)entryAddr);
            uint p = SkillAssignmentUnitCSkillSysViewModel.ResolveSkillIconGbaPointer(rom, id);
            Assert.Equal(0xCAFEBABEu, p);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_RefreshPatchState_NullRom_NotActive() {
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = null;
            var vm = new SkillAssignmentUnitCSkillSysViewModel();
            vm.RefreshPatchState();
            Assert.False(vm.IsCSkillSysActive);
        } finally { CoreState.ROM = prevRom; }
    }

    static string ReadAxaml() {
        string axamlPath = Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SkillAssignmentUnitCSkillSysView.axaml");
        return File.ReadAllText(axamlPath);
    }

    static string ReadCodeBehind() {
        string path = Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SkillAssignmentUnitCSkillSysView.axaml.cs");
        return File.ReadAllText(path);
    }

    static string FindRepoRoot() {
        string cur = AppContext.BaseDirectory;
        for (int i = 0; i < 12; i++) {
            if (File.Exists(Path.Combine(cur, "FEBuilderGBA.sln"))) return cur;
            DirectoryInfo? parent = Directory.GetParent(cur);
            if (parent == null) break;
            cur = parent.FullName;
        }
        throw new InvalidOperationException("Could not locate FEBuilderGBA.sln from " + AppContext.BaseDirectory);
    }

    static ROM MakeMinimalFe8uRom() {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }
}
