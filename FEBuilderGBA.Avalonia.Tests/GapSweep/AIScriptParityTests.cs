// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for AIScriptView. (#410)
//
// Closes the 73 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `AIScriptForm` (HIGH density 37/3 == -91.9 %, 24 WF-only labels, 0
// common labels, 15 unmapped jump callsites). After this PR `AIScriptView`
// rebuilds to the master/detail editor the WinForms form exposes.
//
// Mirrors PR #412 (SongTrack) and PR #411 (EDForm) parity-test pattern:
// density floor + AutomationId surface coverage + undo wrap + ROM round-trip
// + manifest contract + Roslyn-static AXAML scans.
//
// Copilot CLI plan-review v1 issues addressed:
//   C1 - Manifest contract: only wired `WindowManager.Navigate<>` callsites
//        get manifest rows. Deferred AI sub-editors stay `MissingAvManifest`
//        per scanner — `NavigationManifest_NoIssueRefRows` pins this.
//   C2 - VM declaration changed to `public partial class AIScriptViewModel`
//        so `AIScriptViewModel.NavigationTargets.cs` partial compiles.
//   C3 - AI1/AI2 are separate pointer tables (not combined). Tests are
//        split: `ViewModel_LoadList_FilterZero_LoadsAI1`,
//        `ViewModel_LoadList_FilterOne_LoadsAI2`,
//        `ViewModel_LoadList_FilterChange_SwitchesPointerTable`.
//   C4 - Localization touches ja+zh only (ko.txt does not exist in repo).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the AIScriptForm parity raise (#410) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class AIScriptParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 37 control instantiations (per 2026-05-21
    /// density sweep). To leave the HIGH verdict we need
    /// AV >= ceil(37 * 0.75) = 28.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 37;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 28
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75 % of WF={WfControlCount}) to leave the HIGH verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // The AutomationId vocabulary mirrors the WF designer field names so
    // headless tests / external automation can drive both UIs identically.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReadConfigBar()
    {
        // WF panel3: FilterComboBox / ReadStartAddress / ReadCount / ReloadListButton.
        // #649: TopAddress / ReadCount / Reload migrated to
        // EditorTopBarWithInputs; legacy AutomationIds are preserved via
        // *AutomationId override styled-properties.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Filter_Combo\"", axaml);
        Assert.Contains("AIScript_TopAddress_Input", axaml);
        Assert.Contains("AIScript_ReadCount_Input", axaml);
        Assert.Contains("AIScript_Reload_Button", axaml);
    }

    [Fact]
    public void View_HasMasterListPanel()
    {
        // WF panel6: AddressList + AddressListExpandsButton + name header.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_ListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_HasWriteBar()
    {
        // WF panel5: Address / ReadCount (N_) / Reload / AllWrite.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_ReadByteCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_ReloadList_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasDetailPanel_AllParamRows()
    {
        // WF ControlPanel: 5 parameter rows (Label + Spinner + Value preview).
        // AutomationId suffix policy (build validator): the value preview
        // TextBox is read-only but uses `_Input` suffix because the
        // validator only allows specific suffixes (_Label is reserved for
        // TextBlocks; renaming to *Display_Input keeps the value uniquely
        // discoverable).
        string axaml = ReadAxaml();
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"AutomationId=\"AIScript_Param{i}_Label\"", axaml);
            Assert.Contains($"AutomationId=\"AIScript_Param{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"AIScript_Param{i}Display_Input\"", axaml);
        }
    }

    [Fact]
    public void View_HasDetailPanel_CommentAndAsmFields()
    {
        // WF ControlPanelCommand: Comment / ASM / ScriptCodeName / AddressTextBox.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Comment_Input\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_Asm_Input\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_ScriptCodeName_Label\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_DetailAddress_Label\"", axaml);
    }

    [Fact]
    public void View_HasActionButtons()
    {
        // WF ControlPanelCommand action buttons.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Update_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_New_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_Remove_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_Close_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_ScriptChange_Button\"", axaml);
    }

    [Fact]
    public void View_HasExportImportButtons()
    {
        // WF ListBoxPanel: EventToFile / FileToEvent buttons.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"AIScript_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"AIScript_Import_Button\"", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 4 - navigation manifest contract assertions.
    // -----------------------------------------------------------------

    /// <summary>
    /// The manifest contains rows for `WindowManager.Navigate<>` callsites that
    /// the view code-behind ACTUALLY wires. #1600 wired the 5 POINTER_AI*
    /// parameter jumps (ParamLabel_Click), so the manifest now carries the 5 AI
    /// sub-editor rows alongside the address-double-click PointerToolCopyTo row.
    /// The Unit / Class / DisASM param jumps remain deferred (different
    /// ArgTypes, not part of this slice) and stay out of the manifest.
    /// </summary>
    [Fact]
    public void NavigationManifest_HasExpectedRows()
    {
        var vm = new AIScriptViewModel();
        IReadOnlyList<NavigationTarget> targets = vm.GetNavigationTargets();

        var expected = new HashSet<string>
        {
            "JumpToPointerToolCopyTo",
            "JumpToAIUnits",
            "JumpToAITiles",
            "JumpToAIASMCoordinate",
            "JumpToAIASMRange",
            "JumpToAIASMCALLTALK",
        };
        var actual = new HashSet<string>(targets.Select(t => t.CommandName));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Per Copilot CLI plan-review v1 #1: every manifest row must correspond
    /// to a working `WindowManager.Navigate<>` callsite — IssueRef must be
    /// null for ALL rows. If a future PR has to defer a jump, drop the row
    /// (or add IssueRef ONLY when the jump callsite IS wired but routes to a
    /// broken target — the SupportTalk #360 precedent).
    /// </summary>
    [Fact]
    public void NavigationManifest_NoIssueRefRows()
    {
        var vm = new AIScriptViewModel();
        IReadOnlyList<NavigationTarget> targets = vm.GetNavigationTargets();
        Assert.All(targets, t => Assert.Null(t.IssueRef));
    }

    /// <summary>
    /// #1600 wired the 5 POINTER_AI* parameter jumps, so the AI sub-editors are
    /// NOW in the manifest. The remaining deferred targets (Unit / Class /
    /// DisASM via the param-label dispatch for OTHER ArgTypes, and the
    /// AIScriptCategorySelect picker opened via ScriptChangeButton) still must
    /// NOT appear in the manifest — they stay MissingAvManifest in the
    /// JumpParityScanner output.
    /// </summary>
    [Fact]
    public void NavigationManifest_WiredAISubEditors_InManifest()
    {
        var vm = new AIScriptViewModel();
        IReadOnlyList<NavigationTarget> targets = vm.GetNavigationTargets();

        // The 5 AI sub-editors wired by #1600 ARE present.
        Type[] wiredAiSubEditors = new[]
        {
            typeof(AIUnitsView),
            typeof(AITilesView),
            typeof(AIASMCoordinateView),
            typeof(AIASMRangeView),
            typeof(AIASMCALLTALKView),
        };
        foreach (var t in wiredAiSubEditors)
        {
            Assert.Contains(targets, n => n.TargetViewType == t);
        }

        // These remain deferred (different ArgTypes / opened via the opcode
        // picker), so they must NOT appear in the manifest.
        Type[] deferredTargets = new[]
        {
            typeof(UnitEditorView),
            typeof(UnitFE7View),
            typeof(UnitFE6View),
            typeof(ClassEditorView),
            typeof(ClassFE6View),
            typeof(DisASMView),
            typeof(AIScriptCategorySelectView),
        };
        foreach (var t in deferredTargets)
        {
            Assert.DoesNotContain(targets, n => n.TargetViewType == t);
        }
    }

    /// <summary>
    /// Every TargetViewType in the manifest must resolve to a real Avalonia
    /// view type — guards against rename / delete drift.
    /// </summary>
    [Fact]
    public void NavigationManifest_AllTargetViewTypesExist()
    {
        var vm = new AIScriptViewModel();
        IReadOnlyList<NavigationTarget> targets = vm.GetNavigationTargets();
        Assert.NotEmpty(targets);
        foreach (var t in targets)
        {
            Assert.NotNull(t.TargetViewType);
            // Sanity: TargetViewType must be a Window subtype.
            Assert.True(typeof(global::Avalonia.Controls.Window).IsAssignableFrom(t.TargetViewType),
                $"{t.CommandName}: TargetViewType {t.TargetViewType} must be a Window");
        }
    }

    // -----------------------------------------------------------------
    // Phase 5 - Code-behind / write-handler assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsUndoScopeAndWritesInPlace()
    {
        // #760/#763: AI script opcode write-back drives the VM's WriteScript,
        // which now handles BOTH a same-size in-place write AND a New/Remove
        // realloc + pointer-repoint. The Write_Click handler must wrap the
        // write in a UndoService.Begin / Commit block (rolling back on a
        // refused / failed write) and pass the active undoData so the realloc
        // path is undo-tracked — parity with SongTrack / EDView.
        string codeBehindPath = CodeBehindPath();
        Assert.True(File.Exists(codeBehindPath), $"Code-behind not found at {codeBehindPath}");
        string code = File.ReadAllText(codeBehindPath);

        // Write_Click must exist and drive the real write path.
        Assert.Contains("Write_Click", code);
        Assert.Contains("_undoService.Begin(", code);
        Assert.Contains("_undoService.Commit(", code);
        Assert.Contains("_undoService.Rollback(", code);
        // The realloc path requires the active undoData (#763).
        Assert.Contains("WriteScript(_undoService.GetActiveUndoData())", code);

        // The old "not yet implemented" Write message must be gone.
        Assert.DoesNotContain("AI script Write is not yet implemented in Avalonia", code);
    }

    [Fact]
    public void View_FilterCombo_ChangesPointerTable()
    {
        // Roslyn-static: the code-behind must subscribe to filter combo's
        // SelectionChanged event so AI1/AI2 toggle reloads the list.
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Contains("FilterCombo", code);
        // Should have a handler that calls LoadList again.
        Assert.True(code.Contains("Filter_Click")
                 || code.Contains("Filter_SelectionChanged")
                 || code.Contains("FilterCombo.SelectionChanged"),
            "Expected filter combo to wire SelectionChanged handler");
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic ROM).
    // -----------------------------------------------------------------

    /// <summary>
    /// Per Copilot CLI plan-review v1 #3: LoadList with FilterIndex = 0
    /// must read the ai1 pointer table (NOT a combined AI1+AI2 list).
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_FilterZero_LoadsAI1()
    {
        var rom = MakeMinimalFE8URom(out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new AIScriptViewModel();
            vm.FilterIndex = 0;
            List<AddrResult> list = vm.LoadList();
            // Synthetic plants 3 entries in the ai1 table.
            Assert.NotEmpty(list);
            Assert.True(list.Count >= 3,
                $"FilterIndex=0 (AI1) should load >=3 entries, got {list.Count}");
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Per Copilot CLI plan-review v1 #3: LoadList with FilterIndex = 1
    /// must read the ai2 pointer table (NOT a combined AI1+AI2 list).
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_FilterOne_LoadsAI2()
    {
        var rom = MakeMinimalFE8URom(out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new AIScriptViewModel();
            vm.FilterIndex = 1;
            List<AddrResult> list = vm.LoadList();
            // Synthetic plants 2 entries in the ai2 table.
            Assert.NotEmpty(list);
            Assert.True(list.Count >= 2,
                $"FilterIndex=1 (AI2) should load >=2 entries, got {list.Count}");
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Per Copilot CLI plan-review v1 #3: switching FilterIndex between 0
    /// and 1 must reload the address list against the *other* pointer
    /// table (parity with WF FilterComboBox_SelectedIndexChanged calling
    /// ReInitPointer).
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_FilterChange_SwitchesPointerTable()
    {
        var rom = MakeMinimalFE8URom(out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new AIScriptViewModel();

            vm.FilterIndex = 0;
            int ai1Count = vm.LoadList().Count;
            uint ai1FirstAddr = vm.LoadList().Count > 0 ? vm.LoadList()[0].addr : 0u;

            vm.FilterIndex = 1;
            int ai2Count = vm.LoadList().Count;
            uint ai2FirstAddr = vm.LoadList().Count > 0 ? vm.LoadList()[0].addr : 0u;

            // Switching the filter must change the listed addresses
            // (different pointer table).
            Assert.NotEqual(ai1FirstAddr, ai2FirstAddr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAddress()
    {
        var rom = MakeMinimalFE8URom(out uint ai1Addr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new AIScriptViewModel();
            vm.FilterIndex = 0;
            vm.LoadList();
            // Load entry at the first pointer slot (which points at
            // 0x08200000 in the synthetic plant).
            vm.LoadEntry(ai1Addr);
            Assert.True(vm.IsLoaded, "VM should report loaded after LoadEntry");
            Assert.NotEqual(0u, vm.CurrentAddr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_IsPartialClass_ForNavigationTargetsParity()
    {
        // Per Copilot CLI plan-review v1 #2: the class must be partial so
        // the NavigationTargets.cs partial compiles.
        Type t = typeof(AIScriptViewModel);
        // Roslyn-static check via reflection: the type's IsAbstract /
        // IsSealed don't tell us about `partial` directly, but the type
        // implementing INavigationTargetSource via a separate file is the
        // observable consequence — assert it implements the interface.
        Assert.True(typeof(INavigationTargetSource).IsAssignableFrom(t),
            "AIScriptViewModel must implement INavigationTargetSource via partial class");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "AIScriptView.axaml");
    }

    static string CodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "AIScriptView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with:
    /// - ai1_pointer  -> 0x08100000 (ai1 table at 0x100000): 3 entries.
    /// - ai2_pointer  -> 0x08100100 (ai2 table at 0x100100): 2 entries.
    /// Each AI script entry is a single 16-byte EXIT opcode at 0x08200000+.
    /// </summary>
    static ROM MakeMinimalFE8URom(out uint ai1PointerSlot, out uint ai2PointerSlot)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Free-space region for ExpandTable (~64KB of 0xFF).
        for (uint i = 0x500000; i < 0x510000; i++)
            rom.Data[i] = 0xFF;

        // Plant pointers.
        WriteU32(rom.Data, (int)rom.RomInfo.ai1_pointer, 0x08100000);
        WriteU32(rom.Data, (int)rom.RomInfo.ai2_pointer, 0x08100100);

        // AI1 list (4-byte pointer slots, 3 entries) at 0x100000.
        // Per PR #571 Copilot bot review #5: the out-param returns the
        // FIRST POINTER SLOT in the AI table (0x100000), NOT the pointer
        // location (rom.RomInfo.ai1_pointer). LoadEntry() expects to read
        // a script pointer at the slot address, follow it, and resolve
        // CalcLength — so the test must hand it a real slot address.
        ai1PointerSlot = 0x100000;
        WriteU32(rom.Data, 0x100000, 0x08200000);
        WriteU32(rom.Data, 0x100004, 0x08200010);
        WriteU32(rom.Data, 0x100008, 0x08200020);
        // Terminator at +12: u32 == 0xFFFFFFFF (matches AIScriptForm.Init
        // expects u32 == NOT_FOUND).
        WriteU32(rom.Data, 0x10000C, 0xFFFFFFFF);

        // AI2 list at 0x100100 - 2 entries.
        ai2PointerSlot = 0x100100;
        WriteU32(rom.Data, 0x100100, 0x08200030);
        WriteU32(rom.Data, 0x100104, 0x08200040);
        WriteU32(rom.Data, 0x100108, 0xFFFFFFFF);

        // Plant 5 minimal AI script bodies at 0x200000..0x200040 — each is
        // a single 16-byte EXIT instruction (opcode 0x03 followed by 15
        // zero bytes).
        for (uint i = 0; i < 5; i++)
        {
            uint offset = 0x200000 + i * 0x10;
            rom.Data[offset] = 0x03; // EXIT
            for (uint j = 1; j < 16; j++)
                rom.Data[offset + j] = 0x00;
        }

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
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
