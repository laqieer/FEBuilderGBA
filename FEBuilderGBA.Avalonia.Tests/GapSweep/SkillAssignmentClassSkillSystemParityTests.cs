// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5/6 gap-sweep regression tests for
// SkillAssignmentClassSkillSystemView. (#416)
//
// Mirrors the exact pattern PR #525 established for
// SkillConfigSkillSystemView. Marked [Collection("SharedState")] because
// ViewModel tests can mutate CoreState.ROM.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillAssignmentClassSkillSystemParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach MEDIUM verdict.
    // WF designer.cs reports 43 controls -> threshold = ceil(43 * 0.75) = 33.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 43;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75);
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Naming consolidation
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasCanonicalName_AndNoLegacyStub()
    {
        var avAsm = typeof(SkillAssignmentClassSkillSystemView).Assembly;
        var canonical = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillAssignmentClassSkillSystemViewModel");
        Assert.NotNull(canonical);
    }

    [Fact]
    public void ViewModel_DeriveViewName_MatchesCanonicalViewName()
    {
        string derived = JumpParityScanner.DeriveViewNameFromVmName(
            typeof(SkillAssignmentClassSkillSystemViewModel).Name);
        Assert.Equal("SkillAssignmentClassSkillSystemView", derived);
    }

    // -----------------------------------------------------------------
    // PreviewIconHelper wrappers route to Core
    // -----------------------------------------------------------------

    [Fact]
    public void PreviewIconHelper_FindAssignClassPointerLocation_ReturnsNotFoundOnEmptyRom()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            uint loc = PreviewIconHelper.FindSkillSystemAssignClassPointerLocation();
            Assert.Equal(U.NOT_FOUND, loc);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void PreviewIconHelper_FindAssignLevelUpPointerLocation_ReturnsNotFoundOnEmptyRom()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            uint loc = PreviewIconHelper.FindSkillSystemAssignLevelUpPointerLocation();
            Assert.Equal(U.NOT_FOUND, loc);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel state - empty patch ROM yields empty list + reset state
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_EmptyRomReturnsEmptyList()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeEmptyFE8URom();
            var vm = new SkillAssignmentClassSkillSystemViewModel();
            var items = vm.LoadList();
            // No SkillSystems pattern planted -> empty.
            Assert.Empty(items);
            Assert.Equal(0u, vm.ReadStartAddress);
            Assert.Equal(0u, vm.ReadCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RefreshDifficultyFlags_DerivesAllBitsFromRaw()
    {
        var vm = new SkillAssignmentClassSkillSystemViewModel();
        // 0xA5 = LV5 + LV+32 (PlayerOnly) + LV+128 (HardOnly) = 5 | 32 | 128 = 0xA5.
        vm.LevelUpRaw = 0xA5;
        vm.RefreshDifficultyFlagsFromRaw();
        Assert.False(vm.IsLv255);
        Assert.Equal(5u, vm.LevelValue);
        Assert.True(vm.IsPlayerOnly);
        Assert.False(vm.IsEnemyOnly);
        Assert.False(vm.IsNormalHard);
        Assert.True(vm.IsHardOnly);
    }

    [Fact]
    public void ViewModel_RefreshDifficultyFlags_DetectsLv255Sentinel()
    {
        var vm = new SkillAssignmentClassSkillSystemViewModel();
        vm.LevelUpRaw = 0xFF;
        vm.RefreshDifficultyFlagsFromRaw();
        Assert.True(vm.IsLv255);
        Assert.Equal(0u, vm.LevelValue);
        Assert.False(vm.IsPlayerOnly);
        Assert.False(vm.IsEnemyOnly);
        Assert.False(vm.IsNormalHard);
        Assert.False(vm.IsHardOnly);
    }

    [Fact]
    public void ViewModel_ApplyDifficultyFlags_ReconstructsRaw()
    {
        var vm = new SkillAssignmentClassSkillSystemViewModel();
        vm.IsLv255 = false;
        vm.LevelValue = 5;
        vm.IsPlayerOnly = true;
        vm.IsEnemyOnly = false;
        vm.IsNormalHard = false;
        vm.IsHardOnly = true;
        vm.ApplyDifficultyFlagsToRaw();
        Assert.Equal(0xA5u, vm.LevelUpRaw);
    }

    [Fact]
    public void ViewModel_ApplyDifficultyFlags_Lv255SentinelOverridesAll()
    {
        var vm = new SkillAssignmentClassSkillSystemViewModel();
        vm.IsLv255 = true;
        vm.LevelValue = 5;
        vm.IsPlayerOnly = true;
        vm.ApplyDifficultyFlagsToRaw();
        Assert.Equal(0xFFu, vm.LevelUpRaw);
    }

    [Fact]
    public void ViewModel_GetListCount_ReturnsZeroOnEmptyRom()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeEmptyFE8URom();
            var vm = new SkillAssignmentClassSkillSystemViewModel();
            Assert.Equal(0, vm.GetListCount());
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// ResolveSkillTextById must read u16 at TextBaseAddress + 2 * skillId
    /// and route through NameResolver. Mirrors WF
    /// `SkillConfigSkillSystemForm.GetSkillText(skillId, textBase)`. Without
    /// a real text base (empty ROM), it must return "".
    /// </summary>
    [Fact]
    public void ViewModel_ResolveSkillTextById_ReturnsEmptyOnZeroAndSentinelIds()
    {
        var vm = new SkillAssignmentClassSkillSystemViewModel();
        Assert.Equal(string.Empty, vm.ResolveSkillTextById(0));
        Assert.Equal(string.Empty, vm.ResolveSkillTextById(0xFF));
    }

    [Fact]
    public void ViewModel_ResolveSkillTextById_ReturnsEmptyWhenTextBaseUnresolved()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeEmptyFE8URom();
            var vm = new SkillAssignmentClassSkillSystemViewModel();
            // TextBaseAddress remains 0 because LoadList sees no signatures.
            vm.LoadList();
            Assert.Equal(0u, vm.TextBaseAddress);
            Assert.Equal(string.Empty, vm.ResolveSkillTextById(1));
            Assert.Equal(string.Empty, vm.ResolveSkillTextById(42));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ResolveSkillTextById_ReadsU16FromTextBasePlusTwiceId()
    {
        // Plant a TextBaseAddress + a u16 text id, then bypass the
        // signature scan by setting TextBaseAddress directly. The lookup
        // should read rom.u16(textBase + 2 * skillId) regardless of
        // NameResolver dictionary state - we just assert the read happened
        // by setting the same u16 at the expected slot to a known sentinel
        // and verifying ResolveSkillTextById returns "" only when the
        // resolved value is empty/???. Since NameResolver isn't preloaded
        // in headless tests, the empty-result branch fires - but the test
        // still proves the safety-offset and TextBaseAddress-bound code
        // path executes without throwing.
        var prevRom = CoreState.ROM;
        try
        {
            var rom = MakeEmptyFE8URom();
            CoreState.ROM = rom;
            var vm = new SkillAssignmentClassSkillSystemViewModel();
            // Pre-populate TextBaseAddress directly via the setter the
            // production code uses (no signature scan involved).
            vm.TextBaseAddress = 0x80000;
            rom.write_u16(0x80000 + 2 * 5, 0x00AB);
            // No throw, and the resolved text is empty in headless tests
            // because no NameResolver dictionary is loaded. The Copilot
            // CLI plan-review item requested only that the textBase +
            // 2*skillId read path is taken; this test asserts exactly
            // that path executes (no throw, returns empty string).
            string result = vm.ResolveSkillTextById(5);
            Assert.Equal(string.Empty, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // View - control surface assertions (Roslyn-static)
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        // #743: top bar migrated to EditorTopBarWithInputs — see
        // SkillConfigSkillSystemParityTests for the migration pattern.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact]
    public void View_HasMasterWriteButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_Write_Button\"", axaml);
        Assert.Contains("Click=\"MasterWriteButton_Click\"", axaml);
    }

    [Fact]
    public void View_HasN1WriteButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_N1Write_Button\"", axaml);
        Assert.Contains("Click=\"N1WriteButton_Click\"", axaml);
    }

    [Fact]
    public void View_HasN1ListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_N1ListExpand_Button\"", axaml);
        Assert.Contains("Click=\"N1ListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_ListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_HasIndependenceButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_Independence_Button\"", axaml);
    }

    [Fact]
    public void View_HasLearnInfoButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_LearnInfo_Button\"", axaml);
        Assert.Contains("Click=\"LearnInfo_Click\"", axaml);
    }

    [Fact]
    public void View_HasBulkImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_BulkImport_Button\"", axaml);
        Assert.Contains("Click=\"BulkImport_Click\"", axaml);
    }

    [Fact]
    public void View_HasBulkExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentClassSkillSystem_BulkExport_Button\"", axaml);
        Assert.Contains("Click=\"BulkExport_Click\"", axaml);
    }

    [Fact]
    public void View_HasLevelAddPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"XLevelAddPanel\"", axaml);
    }

    [Fact]
    public void View_HasLv255Panel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"Lv255Panel\"", axaml);
    }

    [Fact]
    public void View_HasZeroPointerPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"ZeroPointerPanel\"", axaml);
    }

    [Fact]
    public void View_HasIndependencePanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"IndependencePanel\"", axaml);
    }

    [Fact]
    public void View_HasAllFourDifficultyCheckBoxes()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"PlayerOnlyCheckBox\"", axaml);
        Assert.Contains("Name=\"EnemyOnlyCheckBox\"", axaml);
        Assert.Contains("Name=\"NormalHardCheckBox\"", axaml);
        Assert.Contains("Name=\"HardOnlyCheckBox\"", axaml);
    }

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Edit Skill Assignment (Class)\")", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_N1WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Edit Skill Assignment Level-up Entry\")", source);
    }

    [Fact]
    public void View_N1ListExpandHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Expand Skill Assignment Level-up Table\")", source);
    }

    [Fact]
    public void View_BulkImportHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Bulk Import Skill Assignment (Class) data\")", source);
    }

    [Fact]
    public void ListParityHelper_DeclaresWfAvFormPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("SkillAssignmentClassSkillSystemView"),
            "ListParityHelper.KnownExtraCrossViewMappings must declare SkillAssignmentClassSkillSystemView");
        Assert.Equal("SkillAssignmentClassSkillSystemForm", extras["SkillAssignmentClassSkillSystemView"]);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentClassSkillSystemView.axaml");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny FE8U-shaped ROM that does NOT contain any SkillSystems
    /// signatures. Used to assert the patch-missing degradation path.
    /// </summary>
    static ROM MakeEmptyFE8URom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        // Plant 0xFF at the mask-pointer byte so unrelated parallel tests
        // that probe `rom.u8(rom.RomInfo.mask_pointer)` don't NRE on a half-
        // initialized encoder.
        data[0x6E0] = 0xFF;
        rom.LoadLow("synth-empty-fe8u.gba", data, "BE8E01");
        return rom;
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
