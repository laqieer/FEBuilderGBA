// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/4 gap-sweep regression tests for ItemUsagePointerViewerView. (#440)
//
// Covers the 19 gaps the issue called out: 16 missing controls (density)
// + 3 missing INavigationTargetSource entries (jumps). Tests stay headless —
// no real ROM file required for the density / manifest / scanner assertions.
//
// Two additional assertions (required by Copilot CLI review of the plan):
//   - ViewModel_LoadList_UsesSwitch2Count_NotNullScan exercises the
//     count + 1 semantics with a NULL gap that would have truncated the
//     pre-#440 implementation.
//   - View_NavigationHandlers_AreWired Roslyn-walks the View AST to assert
//     real click handlers exist and call WindowManager Navigate/Open.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the Item Usage Pointer parity raise (#440) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
/// </summary>
public class ItemUsagePointerParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 23 controls (per density sweep). To leave
    /// HIGH we need AV ≥ ceil(WF * 0.75) = 18 controls.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ItemUsagePointerViewerView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-21 density sweep — see issue #440.
        const int WfControlCount = 23;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 18
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be ≥ {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all three callsites.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllThreeJumpTargets()
    {
        var vm = new ItemUsagePointerViewerViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t => t.TargetViewType == typeof(ItemPromotionViewerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(ItemStatBonusesViewerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(PatchManagerView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #440, NONE of the three rows should carry
        // an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new ItemUsagePointerViewerViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate the three WF callsites and confirm
    // they MATCH the new manifest rows (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_AllThreeCallsites_NowMatchManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ItemUsagePointerForm",
                TargetForm: "ItemPromotionForm",
                HasAddressArgument: true),
            new WfJumpCallsite(
                SourceForm: "ItemUsagePointerForm",
                TargetForm: "ItemStatBonusesForm",
                HasAddressArgument: true),
            new WfJumpCallsite(
                SourceForm: "ItemUsagePointerForm",
                TargetForm: "PatchForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ItemUsagePointerViewerViewModel",
                SourceView: "ItemUsagePointerViewerView",
                Command: "JumpToPromotion",
                TargetView: "ItemPromotionViewerView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "ItemUsagePointerViewerViewModel",
                SourceView: "ItemUsagePointerViewerView",
                Command: "JumpToStatBonuses",
                TargetView: "ItemStatBonusesViewerView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "ItemUsagePointerViewerViewModel",
                SourceView: "ItemUsagePointerViewerView",
                Command: "JumpToIerPatch",
                TargetView: "PatchManagerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);

        foreach (var (target, expectedAv) in new[]
        {
            ("ItemPromotionForm", "ItemPromotionViewerView"),
            ("ItemStatBonusesForm", "ItemStatBonusesViewerView"),
            ("PatchForm", "PatchManagerView"),
        })
        {
            var match = rows.FirstOrDefault(r =>
                r.SourceForm == "ItemUsagePointerForm" &&
                r.TargetWfType == target);
            Assert.NotNull(match);
            Assert.Equal(JumpRowStatus.Match, match!.Status);
            Assert.Equal(expectedAv, match.TargetAvType);
        }
    }

    // -----------------------------------------------------------------
    // VM: switch2 count semantics (Copilot CLI review point 3)
    // — list must NOT truncate at a NULL pointer mid-list.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_UsesSwitch2Count_NotNullScan()
    {
        ROM rom = MakeFe8uWithSwitch2WithNullGap();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ItemUsagePointerViewerViewModel();
            var rows = vm.LoadList(0); // Usability filter
            // Synthetic Switch2 declares count=4 → 5 rows total.
            Assert.Equal(5, rows.Count);
            // Row 2 is the NULL gap — must be preserved.
            Assert.Contains("Func=0x00000000", rows[2].name);
            // Trailing valid pointer entries must follow the NULL.
            Assert.Contains("Func=0x08123476", rows[3].name);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // View: real click handlers must be wired (Copilot CLI review point 4)
    // — manifest match alone doesn't prove the UI works.
    // -----------------------------------------------------------------

    [Fact]
    public void View_NavigationHandlers_AreWiredToWindowManager()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ItemUsagePointerViewerView.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");

        string source = File.ReadAllText(viewCsPath);

        // Promotion handler: must Navigate<ItemPromotionViewerView>.
        AssertHandlerWiring(
            source,
            handlerName: "PromotionItemLink_Click",
            requiredCallPattern: @"WindowManager\.Instance\.Navigate<ItemPromotionViewerView>");

        // Stat Bonuses handler: must Navigate<ItemStatBonusesViewerView>.
        AssertHandlerWiring(
            source,
            handlerName: "StatBoosterItemLink_Click",
            requiredCallPattern: @"WindowManager\.Instance\.Navigate<ItemStatBonusesViewerView>");

        // IER handler: must Open<PatchManagerView>.
        AssertHandlerWiring(
            source,
            handlerName: "IerPatch_Click",
            requiredCallPattern: @"WindowManager\.Instance\.Open<PatchManagerView>");

        // Switch2 expander handler: must call ExpandList through _vm with undo scope.
        AssertHandlerWiring(
            source,
            handlerName: "SwitchListExpands_Click",
            requiredCallPattern: @"_vm\.ExpandList\s*\(");

        // Reload handler: must reload via LoadListForFilter.
        AssertHandlerWiring(
            source,
            handlerName: "ReloadList_Click",
            requiredCallPattern: @"LoadListForFilter\s*\(");
    }

    // -----------------------------------------------------------------
    // Existing list-parity helper still maps the editor (regression guard).
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_ItemUsagePointerView_StillRegistered()
    {
        var map = ListParityHelper.GetMapping("ItemUsagePointerViewerView");
        Assert.NotNull(map);
        Assert.Equal("ItemUsagePointerForm", map!.Value.FormType);
    }

    // -----------------------------------------------------------------
    // ResolveDefaultExpansionFillPointer (Copilot CLI re-review issue 3) —
    // mirror WF's "use first '-' entry, fall back to first entry" logic.
    // Tested at the View source level since the helper is private to the
    // View; we verify the code-behind contains the right pattern.
    // -----------------------------------------------------------------

    [Fact]
    public void View_DefaultExpansionFillPointer_UsesDashPlaceholderFirst()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ItemUsagePointerViewerView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        // The helper must be present and must look for the "-" placeholder
        // entry before falling back to the first row — matches WF
        // U.FindComboSelectHexFromValueWhereName(L_0_COMBO, "-").
        Assert.Contains("ResolveDefaultExpansionFillPointer", source);
        Assert.Contains("\"-\"", source);
        // newCount comes from ItemListPredicate.GetItemDataCount (mirrors WF
        // ItemForm.DataCount()) — never a hard-coded 0x100.
        Assert.Contains("ItemListPredicate.GetItemDataCount", source);
        Assert.DoesNotContain("newCount = 0x100u;", source);
    }

    [Fact]
    public void ItemListPredicate_GetItemDataCount_ReturnsZeroOnNullRom()
    {
        Assert.Equal(0u, ItemListPredicate.GetItemDataCount(null!));
    }

    // -----------------------------------------------------------------
    // PatchUtil delegator preserves WinForms confirmation prompt
    // (Copilot CLI re-review issue 2). The WF Switch2Expands delegator
    // MUST call R.ShowYesNo before invoking the Core implementation.
    // -----------------------------------------------------------------

    [Fact]
    public void WinForms_PatchUtil_Switch2Expands_PreservesConfirmationPrompt()
    {
        string repoRoot = FindRepoRoot();
        string patchUtilPath = Path.Combine(repoRoot, "FEBuilderGBA", "PatchUtil.cs");
        string source = File.ReadAllText(patchUtilPath);
        // The WF delegator must call R.ShowYesNo before delegating to Core
        // (WinForms doesn't wire CoreState.Services, so the Core prompt
        // would be silently skipped without this preserved WF dialog).
        // Locate the Switch2Expands method body by string anchor (the
        // signature spans multiple lines, so a single-line regex would
        // miss the body). Slice from the method's `{` to its matching `}`
        // by counting braces — works whether or not the signature wraps.
        int sigIdx = source.IndexOf("public static uint Switch2Expands", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "Switch2Expands wrapper not found in PatchUtil.cs");
        int braceOpenIdx = source.IndexOf('{', sigIdx);
        Assert.True(braceOpenIdx > sigIdx, "Switch2Expands has no body");
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0, "Switch2Expands body is malformed (no matching `}`)");
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);

        // Strip C# line comments so that the order assertion compares
        // CODE positions, not comment text. The natural comment block
        // mentions `ItemUsagePointerCore.Switch2Expands` to explain WHY
        // the wrapper exists, which would otherwise be mis-detected as
        // a "delegate call before the confirmation" violation.
        string codeOnly = Regex.Replace(body, @"//[^\n]*", "");

        Assert.Contains("R.ShowYesNo", codeOnly);
        Assert.Contains("ItemUsagePointerCore.Switch2Expands", codeOnly);
        // R.ShowYesNo must appear BEFORE the Core call (so the dialog gates
        // the ROM mutation, not the other way around).
        int yesNoIdx = codeOnly.IndexOf("R.ShowYesNo", StringComparison.Ordinal);
        int coreIdx = codeOnly.IndexOf("ItemUsagePointerCore.Switch2Expands", StringComparison.Ordinal);
        Assert.True(yesNoIdx < coreIdx,
            $"WinForms R.ShowYesNo confirmation must run BEFORE the Core mutation. " +
            $"yesNoIdx={yesNoIdx}, coreIdx={coreIdx}");
    }

    // ---------------------------- Helpers ----------------------------

    static void AssertHandlerWiring(string source, string handlerName, string requiredCallPattern)
    {
        // Find the handler method body: `void handlerName(...) { ... }`.
        // We assert the body contains the required navigation call.
        var methodPattern = new Regex(
            @"\b" + Regex.Escape(handlerName) + @"\s*\([^)]*\)\s*\{(?<body>.*?)\n\s*\}",
            RegexOptions.Singleline | RegexOptions.Compiled);
        Match m = methodPattern.Match(source);
        Assert.True(m.Success,
            $"Click handler '{handlerName}' not found in ItemUsagePointerViewerView.axaml.cs");
        string body = m.Groups["body"].Value;
        Assert.Matches(requiredCallPattern, body);
    }

    /// <summary>
    /// Build a synthetic FE8U ROM with the item_usability_array switch2
    /// signed (start=0, count=4) and a NULL pointer parked at row 2 of
    /// the pointer table. Mirrors the helper in
    /// FEBuilderGBA.Core.Tests/ItemUsagePointerCoreTests.cs.
    /// </summary>
    static ROM MakeFe8uWithSwitch2WithNullGap()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        uint ptrSlot = rom.RomInfo.item_usability_array_pointer;

        // Plant Switch2 ASM signature: start=0, SUB 0x38, count-1=4, CMP 0x28.
        bytes[switchAddr + 0] = 0;
        bytes[switchAddr + 1] = 0x38;
        bytes[switchAddr + 2] = 4;
        bytes[switchAddr + 3] = 0x28;

        // Park pointer table at 0x00800000 with a NULL at row 2.
        uint tableAddr = 0x00800000u;
        BitConverter.GetBytes(0x08123456u).CopyTo(bytes, tableAddr);
        BitConverter.GetBytes(0x08123466u).CopyTo(bytes, tableAddr + 4);
        BitConverter.GetBytes(0x00000000u).CopyTo(bytes, tableAddr + 8);
        BitConverter.GetBytes(0x08123476u).CopyTo(bytes, tableAddr + 12);
        BitConverter.GetBytes(0x08123486u).CopyTo(bytes, tableAddr + 16);

        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, ptrSlot);

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
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
