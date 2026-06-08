// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1+2+5+6 gap-sweep regression tests for SkillAssignmentClassCSkillSysView. (#415)
//
// Closes the 36 control gap + 24 WF-only labels surfaced by the gap-sweep
// methodology on SkillAssignmentClassCSkillSysForm (HIGH density 43/7).
// After this PR the view rebuilds to a three-pane master-detail editor
// (top read-config bar + class list + per-class detail + N1 sub-list).
// The form is gated to the CSkillSys300 patch (mirrors WinForms
// MainFE8Form.cs line 715).
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
public class SkillAssignmentClassCSkillSysParityTests
{
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassCSkillSysView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);
        const int WfControlCount = 43;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75);
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    [Fact] public void View_HasFilterAndReloadBar() {
        // #743: top bar migrated to EditorTopBarWithInputs — legacy
        // AutomationIds are preserved via the *AutomationId overrides on
        // the unified bar, and the Reload click is wired via the
        // ReloadRequested routed event (not a direct Click= on a Button).
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ReadStart_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact] public void View_HasMasterClassList() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_Class_List\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ClassList_NameFilter_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ClassList_Expand_Button\"", axaml);
    }

    [Fact] public void View_HasClassDetailGrid() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ClassSkillLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ClassSkill_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SkillIcon_Image\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SkillName_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SkillText_Label\"", axaml);
    }

    [Fact] public void View_HasAddressWriteBar() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_AddrLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SizeLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_SelectAddrLabel\"", axaml);
    }

    [Fact] public void View_HasN1Sublist() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_List\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_LabelFilter\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_Expand_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_LevelUpAddr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_LevelUpAddrLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_ReloadList_Button\"", axaml);
    }

    [Fact] public void View_HasN1WriteBar() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_LevelLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_SkillLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_B1_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_SkillIcon_Image\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_SkillName_Label\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_N1_SkillText_Label\"", axaml);
    }

    [Fact] public void View_HasLevelBreakdownPanel() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LevelAddPanel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_Value_Input\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_PlayerOnly_Check\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_EnemyOnly_Check\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_NormalHard_Check\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_HardOnly_Check\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_255_Panel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_BreakdownLabel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_X_LV_LevelLabel\"", axaml);
    }

    [Fact] public void View_HasIndependencePanel() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_IndependencePanel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_Independence_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_IndependenceNotice_Label\"", axaml);
    }

    [Fact] public void View_HasZeroPointerPanel() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ZeroPointerPanel\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ZeroPointer_Label\"", axaml);
    }

    [Fact] public void View_HasLearnInfoLink() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_LearnInfo_Link\"", axaml);
    }

    [Fact] public void View_HasExportImportButtons() {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ExportAll_Button\"", axaml);
        Assert.Contains("AutomationId=\"SkillAssignmentClassCSkillSys_ImportAll_Button\"", axaml);
    }

    // #1011: WF SkillAssignmentClassCSkillSysForm.ExportAllData/ImportAllData are
    // empty stubs, so the Avalonia bulk buttons must be DISABLED (not silent
    // no-ops) with the explanation on the enabled wrapping panel (a disabled
    // Avalonia control isn't hit-testable and wouldn't surface its own tooltip).
    [Fact] public void View_BulkButtons_AreDisabled_WithTooltip() {
        string axaml = ReadAxaml();
        var importBtn = Regex.Match(axaml, @"<Button[^>]*ImportAll_Button[^>]*?/>");
        var exportBtn = Regex.Match(axaml, @"<Button[^>]*ExportAll_Button[^>]*?/>");
        Assert.True(importBtn.Success, "ImportAll button element not found");
        Assert.True(exportBtn.Success, "ExportAll button element not found");
        // Both disabled.
        Assert.Contains("IsEnabled=\"False\"", importBtn.Value);
        Assert.Contains("IsEnabled=\"False\"", exportBtn.Value);
        // No-op Click handlers removed (the buttons no longer advertise an action).
        Assert.DoesNotContain("Click=", importBtn.Value);
        Assert.DoesNotContain("Click=", exportBtn.Value);
        // Explanatory tooltip present (on the enabled wrapping StackPanel).
        Assert.Contains("ToolTip.Tip=", axaml);
        Assert.Matches(new Regex("not available for the C-SkillSys", RegexOptions.IgnoreCase), axaml);
    }

    [Fact] public void View_StatusBanner_NamesCSkillSys300() {
        string axaml = ReadAxaml();
        Assert.Contains("CSkillSys", axaml);
        Assert.Matches(new Regex(@"3\.0|3\.00|300"), axaml);
    }

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

    [Fact] public void View_OnClassSelected_ResetsN1SelectedAddressOnEntryChange() {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+OnClassSelected[\s\S]*?_n1SelectedAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    [Fact] public void Consolidation_CanonicalViewModelFileExists() {
        string canonical = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillAssignmentClassCSkillSysViewModel.cs");
        Assert.True(File.Exists(canonical), $"Canonical ViewModel file expected at {canonical}");
    }

    [Fact] public void Consolidation_LegacyDoubledNameFileIsAbsent() {
        string legacy = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillAssignmentClassCSkillSysViewViewModel.cs");
        Assert.False(File.Exists(legacy), $"Legacy doubled-name ViewModel file must not exist: {legacy}");
    }

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
            var vm = new SkillAssignmentClassCSkillSysViewModel();
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
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            var rows = vm.LoadN1List(baseAddr);
            Assert.Equal(2, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_NullRom_ReturnsEmpty() {
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = null;
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            var rows = vm.LoadN1List(0x00820000u);
            Assert.Empty(rows);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadN1List_ZeroAddress_ReturnsEmpty() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            var rows = vm.LoadN1List(0);
            Assert.Empty(rows);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_IsClassSkillExtendsActive_NullRom_False() {
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = null;
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            vm.RefreshPatchState();
            Assert.False(vm.IsClassSkillExtendsActive);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadEntry_LoadsW0AndExposesClassSkill() {
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
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            vm.LoadEntry(entry);
            Assert.Equal(entry, vm.CurrentAddr);
            Assert.Equal(0x0042u, vm.ClassSkill);
        } finally { CoreState.ROM = prevRom; }
    }

    // Read-config wiring (Copilot CLI PR #552 review #2).
    [Fact] public void ViewModel_LoadN1List_HonorsN1ReadCountCap() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            int b = (int)baseAddr;
            // Plant 10 valid 2-byte entries (no terminator).
            for (int i = 0; i < 10; i++) {
                bytes[b + i * 2] = (byte)(1 + i);
                bytes[b + i * 2 + 1] = (byte)(0x10 + i);
            }
            var vm = new SkillAssignmentClassCSkillSysViewModel();
            vm.N1ReadCount = 4;
            var rows = vm.LoadN1List(baseAddr);
            Assert.Equal(4, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadClassList_HonorsReadStartAddressOverride() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            // Plant a u32 GBA pointer slot at gpConstSkillTable_Job (vanilla FE8U has 0xFFFFFFFF) pointing into our test region.
            // For this test we use ReadStartAddress override directly.
            byte[] bytes = rom.Data;
            uint customBase = 0x00800000u;
            int cb = (int)customBase;
            // Plant a class-data table at rom.RomInfo.class_pointer so ComputeClassCount returns >0.
            if (rom.RomInfo != null && rom.RomInfo.class_pointer != 0) {
                uint slot = rom.RomInfo.class_pointer;
                BitConverter.GetBytes(0x00100000u | 0x08000000u).CopyTo(bytes, (int)slot);
                // 5 valid class entries + 0 sentinel at +4 of entry 5 to terminate.
                uint classBase = 0x00100000u;
                uint cds = rom.RomInfo.class_datasize;
                int classBaseInt = (int)classBase;
                int cdsInt = (int)cds;
                for (int i = 0; i < 5; i++) {
                    bytes[classBaseInt + i * cdsInt + 4] = 0x01;
                }
                bytes[classBaseInt + 5 * cdsInt + 4] = 0x00;
            }

            // Plant 6 W0 entries at our custom base (4 bytes each).
            for (int i = 0; i < 6; i++) {
                bytes[cb + i * 4] = (byte)(0x10 + i);
            }

            var vm = new SkillAssignmentClassCSkillSysViewModel();
            vm.ReadStartAddress = customBase;
            var rows = vm.LoadClassList();
            // Should produce min(class_count, all_entries) — class_count = 5 from above.
            Assert.True(rows.Count > 0);
            Assert.Equal(customBase, rows[0].addr);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void ViewModel_LoadClassList_HonorsReadCountCap() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            // Plant a class-data table so ComputeClassCount returns ~10.
            if (rom.RomInfo != null && rom.RomInfo.class_pointer != 0) {
                uint slot = rom.RomInfo.class_pointer;
                BitConverter.GetBytes(0x00100000u | 0x08000000u).CopyTo(bytes, (int)slot);
                int classBase = (int)0x00100000u;
                int cds = (int)rom.RomInfo.class_datasize;
                for (int i = 0; i < 10; i++) bytes[classBase + i * cds + 4] = 0x01;
                bytes[classBase + 10 * cds + 4] = 0x00;
            }
            uint customBase = 0x00800000u;
            int cb = (int)customBase;
            for (int i = 0; i < 10; i++) bytes[cb + i * 4] = (byte)(0x10 + i);

            var vm = new SkillAssignmentClassCSkillSysViewModel();
            vm.ReadStartAddress = customBase;
            vm.ReadCount = 3;
            var rows = vm.LoadClassList();
            Assert.Equal(3, rows.Count);
        } finally { CoreState.ROM = prevRom; }
    }

    // Skill preview helpers (Copilot CLI PR #552 review #3).
    [Fact] public void ViewModel_ResolveSkillName_NullRom_ReturnsEmpty() {
        Assert.Equal(string.Empty, SkillAssignmentClassCSkillSysViewModel.ResolveSkillName(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillDescription_NullRom_ReturnsEmpty() {
        Assert.Equal(string.Empty, SkillAssignmentClassCSkillSysViewModel.ResolveSkillDescription(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillIconGbaPointer_NullRom_ReturnsZero() {
        Assert.Equal(0u, SkillAssignmentClassCSkillSysViewModel.ResolveSkillIconGbaPointer(null, 0));
    }

    [Fact] public void ViewModel_ResolveSkillIconGbaPointer_PlantedEntry_ReturnsRawU32() {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            // Plant a u32 GBA pointer at gpSkillInfos so p32 -> 0x00400000.
            uint slot = SkillAssignmentClassCSkillSysViewModel.gpSkillInfos;
            BitConverter.GetBytes(0x00400000u | 0x08000000u).CopyTo(bytes, (int)slot);
            // Plant a u32 GBA pointer at skill-info entry +0 for id=5
            uint baseAddr = 0x00400000u;
            uint id = 5;
            uint entryAddr = baseAddr + SkillAssignmentClassCSkillSysViewModel.SKILL_INFO_SIZE * id;
            BitConverter.GetBytes(0xCAFEBABEu).CopyTo(bytes, (int)entryAddr);
            uint p = SkillAssignmentClassCSkillSysViewModel.ResolveSkillIconGbaPointer(rom, id);
            Assert.Equal(0xCAFEBABEu, p);
        } finally { CoreState.ROM = prevRom; }
    }

    [Fact] public void UndoCoverage_ViewCoversCanonicalVmWriteMethods() {
        string repoRoot = FindRepoRoot();
        var allFiles = Directory.GetFiles(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia"),
            "*.cs", SearchOption.AllDirectories);
        var covered = UndoCoverageScanner.DiscoverViewCoveredVmMethods(allFiles);
        const string vm = "SkillAssignmentClassCSkillSysViewModel";
        Assert.Contains((vm, "WriteClassSkill"), covered);
        Assert.Contains((vm, "WriteN1Entry"), covered);
        Assert.Contains((vm, "MakeIndependent"), covered);
        Assert.Contains((vm, "ExpandN1List"), covered);
    }

    static string ReadAxaml() {
        string axamlPath = Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SkillAssignmentClassCSkillSysView.axaml");
        return File.ReadAllText(axamlPath);
    }

    static string ReadCodeBehind() {
        string path = Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "SkillAssignmentClassCSkillSysView.axaml.cs");
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
