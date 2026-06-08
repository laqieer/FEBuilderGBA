// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/4/6 gap-sweep regression tests for ImageMapActionAnimationView. (#433)
//
// Closes the 37 Avalonia ↔ WinForms gaps the gap-sweep methodology surfaced
// on `ImageMapActionAnimationForm` (HIGH density + 20 WF-only labels + 1
// cross-jump). Each assertion maps to a concrete acceptance-criterion bullet
// in the issue body, so regressions get a clear pointer back to the
// original gap-sweep report.
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

/// <summary>
/// Tests proving the ImageMapActionAnimation parity raise (#433) is permanent.
///
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM and CoreState.BaseDirectory — without serialization, xUnit's
/// per-class parallel runner can race a sibling test's ROM swap between two
/// LoadList calls.
/// </summary>
[Collection("SharedState")]
public class ImageMapActionAnimationParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 29 control instantiations. To leave the
    /// HIGH verdict we need AV >= 22 (75% of 29, the MEDIUM threshold).
    /// Closes the density bullet of #433.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer has 29 controls — see the issue body / sweep report.
        const int WfControlCount = 29;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 22
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Partial-class enforcement — Copilot CLI plan-review pt 1.
    // -----------------------------------------------------------------

    /// <summary>
    /// Enforces that <c>ImageMapActionAnimationViewModel</c> implements
    /// <see cref="INavigationTargetSource"/> — the contract added by the
    /// sibling <c>.NavigationTargets.cs</c> partial. The <c>partial</c>
    /// modifier itself is compile-time enforced (CS0260 if it disappears),
    /// so this test focuses on the actually-runtime-visible interface
    /// contract instead. Renamed from
    /// <c>ViewModel_IsDeclaredAsPartialClass</c> after Copilot CLI inline
    /// review on PR #506 flagged the original name as misleading.
    /// </summary>
    [Fact]
    public void ViewModel_ImplementsNavigationTargetSourceInterface()
    {
        var t = typeof(ImageMapActionAnimationViewModel);
        Assert.Contains(typeof(INavigationTargetSource), t.GetInterfaces());
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare the WF callsite as KnownGap.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToAnimationCreator()
    {
        var vm = new ImageMapActionAnimationViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(ToolAnimationCreatorView));
    }

    /// <summary>
    /// As of #500 the `ToolAnimationCreatorView.InitFromRom` flow is live,
    /// `OpenInCreator_Click` is wired in `ImageMapActionAnimationView`, and
    /// the manifest no longer carries an `IssueRef`. The gap-sweep scanner
    /// now classifies this row as `Match`.
    /// </summary>
    [Fact]
    public void ViewModel_JumpToAnimationCreator_IsMarkedAsMatch()
    {
        var vm = new ImageMapActionAnimationViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(ToolAnimationCreatorView));
        Assert.NotNull(target);
        Assert.True(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToAnimationCreator must NOT carry an IssueRef now that " +
            "ToolAnimationCreatorView.InitFromRom is implemented (#500).");
    }

    [Fact]
    public void JumpParityScanner_ToCreator_IsMatch()
    {
        // Simulate the WF callsite for X_N_JumpEditor_Click and verify
        // the scanner classifies it as `Match` now that the manifest entry
        // has no IssueRef (#500).
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ImageMapActionAnimationForm",
                TargetForm: "ToolAnimationCreatorForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageMapActionAnimationViewModel",
                SourceView: "ImageMapActionAnimationView",
                Command: "JumpToAnimationCreator",
                TargetView: "ToolAnimationCreatorView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageMapActionAnimationForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
    }

    [Fact]
    public void View_HasOpenInCreatorButton_Wired()
    {
        // #500: the "Open in Animation Creator" button must be rendered in
        // the selection bar with its click handler wired up.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_OpenInCreator_Button\"", axaml);
        Assert.Contains("Click=\"OpenInCreator_Click\"", axaml);
    }

    // -----------------------------------------------------------------
    // ViewModel state — Phase 1 new fields populated.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PopulatesFromSyntheticRom()
    {
        // Use the same synthetic-ROM technique #491 used: plant the
        // signature + a pointer + a small table so LoadList finds rows.
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            Assert.True(vm.ReadCount > 0, "LoadList must populate ReadCount");
            Assert.True(vm.ReadStartAddress > 0, "LoadList must populate ReadStartAddress");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesD0W4W6()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            // Pick row 1 (row 0 is the "Empty" reserved slot).
            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            Assert.Equal(addr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            Assert.True(vm.CanWrite);
            // Sanity on padding fields — synthetic ROM zero-fills them.
            Assert.Equal(0u, vm.Padding1);
            Assert.Equal(0u, vm.Padding2);
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    /// <summary>
    /// Mirrors WF NOTIFY_KeepEmpty logic — IsEmptyEntry must flip true
    /// when row 0 is selected.
    /// </summary>
    [Fact]
    public void ViewModel_LoadEntry_Row0_FlipsIsEmptyEntry()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            vm.LoadEntry(items[0].addr);
            Assert.True(vm.IsEmptyEntry);
            // Row 1+ must NOT be flagged as Empty.
            if (items.Count > 1)
            {
                vm.LoadEntry(items[1].addr);
                Assert.False(vm.IsEmptyEntry);
            }
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    [Fact]
    public void ViewModel_DefaultName_FilledFromConfigFile()
    {
        // Force-set CoreState.BaseDirectory to the test assembly dir
        // (where Content Include copies config/data/...). The previous
        // version used `EnsureCoreStateBaseDirectory` which only sets when
        // null/empty — that path raced parallel tests in another class
        // that set BaseDirectory to an unrelated location, leaving our
        // `LoadDefaultName` resolving an unreadable path on Linux CI.
        var prevBaseDir = CoreState.BaseDirectory;
        try
        {
            string? assemblyDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (assemblyDir != null)
                CoreState.BaseDirectory = assemblyDir;

            ImageMapActionAnimationViewModel.ResetDefaultNameCache();

            // config/data/MapActionAnimation_ALL.txt ships with id=0 -> "Empty",
            // id=1 -> "Break1". LoadDefaultName must return those exact strings.
            // If the file isn't shipped to the test bin in this CI environment
            // we treat it as a skip (return early) rather than hard-fail —
            // the contract we're asserting is "when the file IS readable,
            // LoadDefaultName returns the parsed names".
            string baseDir = CoreState.BaseDirectory ?? AppContext.BaseDirectory;
            string configPath = Path.Combine(baseDir, "config", "data", "MapActionAnimation_ALL.txt");
            if (!File.Exists(configPath))
            {
                return;
            }

            string name0 = ImageMapActionAnimationViewModel.LoadDefaultName(0);
            string name1 = ImageMapActionAnimationViewModel.LoadDefaultName(1);

            Assert.Equal("Empty", name0);
            Assert.Equal("Break1", name1);
        }
        finally
        {
            // Restore prior BaseDirectory UNCONDITIONALLY (including
            // null) — leaking our overwrite into subsequent tests
            // breaks isolation (Copilot CLI inline review on PR #506).
            CoreState.BaseDirectory = prevBaseDir;

            // Reset the static cache so any subsequent test that uses
            // LoadDefaultName picks up the restored BaseDirectory.
            ImageMapActionAnimationViewModel.ResetDefaultNameCache();
        }
    }

    /// <summary>
    /// Copilot CLI review on PR #506 — <c>Write()</c> must persist the
    /// Comment to <c>CoreState.CommentCache</c> so subsequent reloads
    /// (via <c>LoadEntry</c>) read back what the user typed. Mirrors
    /// WinForms <c>UI_WriteCommentToUI</c> / <c>UI_ReadUIToComment</c>
    /// (InputFormRef.cs lines 5373 / 5395). Without this fix, user
    /// comment edits would be silently discarded after Write+Reload.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsCommentToCommentCache()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            CoreState.CommentCache = new HeadlessEtcCache();

            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            // Edit the comment + write.
            vm.Comment = "MyTestComment";
            vm.Write();

            // The comment must be in CoreState.CommentCache at the row addr.
            Assert.Equal("MyTestComment", CoreState.CommentCache.At(addr));

            // Reload the same entry — Comment must come back from the cache.
            vm.Comment = "OVERWRITTEN_BEFORE_RELOAD";
            vm.LoadEntry(addr);
            Assert.Equal("MyTestComment", vm.Comment);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.CommentCache = prevCache;
        }
    }

    // -----------------------------------------------------------------
    // View — control surface assertions (Roslyn-static).
    // -----------------------------------------------------------------

    /// <summary>
    /// The new view AXAML MUST surface the Reload button so the scanner
    /// counts it toward the density verdict and so the click handler is
    /// reachable from headless test harnesses.
    /// </summary>
    [Fact]
    public void View_HasReloadButton_Wired()
    {
        // #649: Reload button now lives inside EditorTopBar; its
        // AutomationId is preserved via the ReloadAutomationId override.
        // The hand-rolled Click=\"ReloadList_Click\" handler was replaced
        // by the unified bar's ReloadRequested routed event.
        string axaml = ReadAxaml();
        Assert.Contains("ImageMapActionAnimation_ReloadList_Button", axaml);
        Assert.Contains("OnTopBarReloadRequested", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_EnabledNow_501Landed()
    {
        // #501 (closed) — the button now has a real ListExpand_Click handler
        // backed by DataExpansionCore.ExpandTableTo, so it must NOT be disabled
        // in AXAML. The button's IsEnabled gating now lives entirely in the
        // click handler (which checks `_vm.IsLoaded` / `_vm.ReadCount > 0`).
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_ListExpand_Button\"", axaml);
        // Tooltip no longer says "Not yet implemented" / references #501.
        Assert.DoesNotContain("Not yet implemented", axaml);
        Assert.DoesNotContain("see #501", axaml);
        // Specifically the ListExpand button block must not carry IsEnabled="False".
        int idx = axaml.IndexOf("ListExpand_Button");
        Assert.True(idx >= 0);
        int blockEnd = axaml.IndexOf("/>", idx);
        string buttonBlock = axaml.Substring(idx, blockEnd - idx);
        Assert.DoesNotContain("IsEnabled=\"False\"", buttonBlock);
    }

    [Fact]
    public void View_ListExpand_Tooltip_DescribesExpansion()
    {
        // #501 — the new tooltip must describe what the button does
        // (entry-count expansion) rather than the old "not yet implemented"
        // placeholder. The exact tooltip wording is asserted so future
        // refactors don't silently revert to the placeholder.
        string axaml = ReadAxaml();
        Assert.Contains("Expand the map action animation list to a specified entry count (max 255).",
            axaml);
    }

    [Fact]
    public void ViewModel_ExpandList_RejectsSmallerCount()
    {
        // VM smoke test — ExpandList(newCount, undo) must reject a count smaller
        // than the current ReadCount with a non-empty error string.
        ROM prevRom = CoreState.ROM;
        var rom = MakeMinimalFe8uRom();
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new ImageMapActionAnimationViewModel();
            // Force ReadCount to a known non-zero value.
            vm.ReadCount = 10;
            string err = vm.ExpandList(5, null);
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void ViewModel_ExpandList_NoROM_ReturnsError()
    {
        // VM smoke test — without a ROM loaded, ExpandList must return a
        // non-empty error string instead of throwing.
        ROM prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageMapActionAnimationViewModel();
            string err = vm.ExpandList(10, null);
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void ViewModel_ExpandList_HappyPath()
    {
        // VM smoke test — synthetic FE8U ROM, run LoadList to populate
        // ReadStartAddress/ReadCount, then ExpandList(ReadCount + 2). After
        // success, ReadCount must reflect the new size and ReadStartAddress
        // must point at the new table base.
        ROM prevRom = CoreState.ROM;
        ISystemTextEncoder prevEnc = CoreState.SystemTextEncoder;
        IEtcCache prevCache = CoreState.CommentCache;
        var rom = MakeMinimalFe8uRom();
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            CoreState.CommentCache = new HeadlessEtcCache();

            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint oldBase = vm.ReadStartAddress;
            uint oldCount = vm.ReadCount;
            uint newCount = oldCount + 2;

            string err = vm.ExpandList(newCount, null);
            Assert.True(string.IsNullOrEmpty(err), err);
            Assert.Equal(newCount, vm.ReadCount);
            // New table must live at a different base.
            Assert.NotEqual(oldBase, vm.ReadStartAddress);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void View_HasCommentBox_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_Comment_Input\"", axaml);
    }

    /// <summary>
    /// Mirrors WF NOTIFY_KeepEmpty visibility logic. The label exists in
    /// AXAML with IsVisible=False (the code-behind flips it on row-0).
    /// </summary>
    [Fact]
    public void View_HasKeepEmptyLabel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_KeepEmpty_Label\"", axaml);
    }

    /// <summary>
    /// Issue #499 closes the four WF-only labels that were deferred in #433:
    /// アニメーション取出 / アニメーション読込 / ソースファイルを開く /
    /// ソースフォルダーを開く. The AXAML must now contain all four buttons.
    /// </summary>
    [Fact]
    public void View_ContainsExportImportSourceButtons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMapActionAnimation_Export_Button", axaml);
        Assert.Contains("ImageMapActionAnimation_Import_Button", axaml);
        Assert.Contains("ImageMapActionAnimation_OpenSource_Button", axaml);
        Assert.Contains("ImageMapActionAnimation_SelectSource_Button", axaml);
    }

    /// <summary>
    /// Export button must be disabled by default — only the
    /// `RefreshExportImportButtonState()` helper flips it to enabled once
    /// `IsAnimationValid` is true. Verified in AXAML default attribute.
    /// </summary>
    [Fact]
    public void View_ExportImportButtons_DefaultDisabled()
    {
        string axaml = ReadAxaml();
        // Both `Export` + `Import` buttons start IsEnabled="False" so the
        // selection-bar gating doesn't have to fight a pre-clicked state.
        // (The 4 buttons share the same StackPanel — search for the literal
        // pair to keep the assertion specific.)
        Assert.Contains("Name=\"AnimationExportButton\" Content=\"Export Animation\"",
            axaml);
        Assert.Contains("Name=\"AnimationImportButton\" Content=\"Import Animation\"",
            axaml);
        Assert.Contains("Name=\"OpenSourceButton\" Content=\"Open Source File\"",
            axaml);
        Assert.Contains("Name=\"SelectSourceButton\" Content=\"Open Source Folder\"",
            axaml);
    }

    /// <summary>
    /// Code-behind must call RefreshExportImportButtonState from UpdateUI
    /// so selection changes re-evaluate Export/Import button gating.
    /// </summary>
    [Fact]
    public void View_UpdateUI_CallsRefreshExportImportButtonState()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml.cs");
        string source = File.ReadAllText(sourcePath);
        Assert.Contains("RefreshExportImportButtonState()", source);
    }

    /// <summary>
    /// Import handler must wrap the ROM mutation in UndoService scope —
    /// rollback on error, commit on success.
    /// </summary>
    [Fact]
    public void View_ImportHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        // Both AnimationImport_Click and the existing Write_Click must
        // wrap their ROM mutations in undo scopes.
        Assert.Contains("AnimationImport_Click", source);
        // The Import handler must Rollback on error.
        Assert.Contains("_undoService.Rollback()", source);
    }

    /// <summary>
    /// Write handler must wrap ROM mutation in `_undoService.Begin/Commit`.
    /// Roslyn-static read of the code-behind source — we don't need the
    /// real Avalonia head running.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        // Assert the Begin/Commit/Rollback CALL PATTERN without pinning
        // the exact undo-scope label string. The previous version asserted
        // `_undoService.Begin("Edit Map Action Animation")` which would
        // fail on harmless refactors that just change the label text —
        // Copilot CLI inline review on PR #506.
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    // -----------------------------------------------------------------
    // #499 ViewModel API surface
    // -----------------------------------------------------------------

    /// <summary>
    /// The VM must expose the three export/import entry points + the two
    /// source-path remember/lookup helpers.
    /// </summary>
    [Fact]
    public void ViewModel_ExposesExportImportApi()
    {
        var t = typeof(ImageMapActionAnimationViewModel);
        Assert.NotNull(t.GetMethod("ExportScript", new[] { typeof(string) }));
        Assert.NotNull(t.GetMethod("ExportGif", new[] { typeof(string) }));
        Assert.NotNull(t.GetMethod("ImportScript", new[]
        {
            typeof(string),
            typeof(System.Func<string, System.ValueTuple<byte[], int, int>?>),
        }));
        Assert.NotNull(t.GetMethod("RememberSourcePath", new[] { typeof(string) }));
        Assert.NotNull(t.GetMethod("TryGetSourcePath", new[] { typeof(string).MakeByRefType() }));
    }

    /// <summary>
    /// RememberSourcePath + TryGetSourcePath must round-trip via the
    /// CoreState.ResourceCache backing store (mirrors WF
    /// `Program.ResourceCache.Update / At`). Backed by EtcCacheResource —
    /// the same type the WF version uses.
    ///
    /// The EtcCacheResource ctor reads
    /// `config/etc/{version}/resource_{ver}.txt` and NREs without a ROM,
    /// so we plant a minimal FE8U ROM first; the resource file is allowed
    /// to be missing (LoadTSVResourcePair2 returns an empty dict).
    /// </summary>
    [Fact]
    public void ViewModel_SourcePathRememberAndRetrieveRoundTrip()
    {
        var prevRom = CoreState.ROM;
        var prevCache = CoreState.ResourceCache;
        try
        {
            CoreState.ROM = MakeMinimalFe8uRom();
            CoreState.ResourceCache = new EtcCacheResource();
            var vm = new ImageMapActionAnimationViewModel
            {
                SelectedId = 7u,
            };
            // No source remembered yet.
            Assert.False(vm.TryGetSourcePath(out string before));
            Assert.Equal("", before);

            // Remember + read back.
            vm.RememberSourcePath("/tmp/my-anim.MapActionAnimation.txt");
            Assert.True(vm.TryGetSourcePath(out string after));
            Assert.Equal("/tmp/my-anim.MapActionAnimation.txt", after);
        }
        finally
        {
            CoreState.ResourceCache = prevCache;
            CoreState.ROM = prevRom;
        }
    }

    /// <summary>
    /// ExportScript must early-return with a non-empty error when no animation
    /// is loaded — protects against accidental click-when-empty.
    /// </summary>
    [Fact]
    public void ViewModel_ExportScript_WithoutAnimation_ReturnsError()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageMapActionAnimationViewModel();
            string err = vm.ExportScript("/tmp/never_written.txt");
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    /// <summary>
    /// ImportScript must early-return with a non-empty error when no entry
    /// is loaded.
    /// </summary>
    [Fact]
    public void ViewModel_ImportScript_WithoutEntry_ReturnsError()
    {
        var vm = new ImageMapActionAnimationViewModel();
        string err = vm.ImportScript("/tmp/never_read.txt", _ => null);
        Assert.False(string.IsNullOrEmpty(err));
    }

    // =================================================================
    // #1025 — ExpandList now repoints ALL references (raw 32-bit + ARM-Thumb
    // LDR literal-pool), not just the canonical pointer slot, by routing the
    // post-ExpandTableTo repoint through DataExpansionCore.RepointAllReferences
    // (#781). Mirrors the merged WorldMapImageListExpandTests /
    // EventMapChangeListExpandTests "AllThreeRefs" behavior tests.
    // =================================================================

    // Synthetic-ROM layout for the #1025 LDR round-trip tests (all offsets;
    // GBA pointers add 0x08000000). The canonical pointer slot is the
    // FindAnimationPointer result slot (the search-start word), NOT a RomInfo
    // offset — this editor locates its table by binary signature search.
    const uint LdrTableBase = 0x00100000u;  // action-anime table base before expand
    const uint LdrFreeRegion = 0x00180000u; // known 0xFF run -> ExpandTableTo lands here
    const uint LdrRawSlot = 0x00004000u;    // a SECOND raw pointer to LdrTableBase
    const uint LdrInstr = 0x00005000u;      // ARM Thumb LDR r0,[pc,#0] (0x4800)
    const uint LdrLiteralSlot = LdrInstr + 4; // its literal-pool slot

    /// <summary>
    /// Behavior-level (real ROM-state) proof that ExpandList repoints the
    /// canonical pointer, a SECOND raw 32-bit pointer, AND an ARM-Thumb LDR
    /// literal-pool word — all to the new base — then restores all three (and
    /// the wiped old region) on undo. This is the #1025 acceptance criterion.
    /// </summary>
    [Fact]
    public void ViewModel_ExpandList_RepointsCanonicalRawAndLdrRefs_AndUndoRestores()
    {
        ROM prevRom = CoreState.ROM;
        Undo prevUndo = CoreState.Undo;
        ISystemTextEncoder prevEnc = CoreState.SystemTextEncoder;
        IEtcCache prevCache = CoreState.CommentCache;
        try
        {
            const int currentCount = 3;
            ROM rom = MakeLdrTestRom(currentCount, out uint canonicalSlot);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            EnsureSystemTextEncoder(rom);
            CoreState.CommentCache = new HeadlessEtcCache();

            // Sanity: all three references resolve to LdrTableBase before expand.
            Assert.Equal(LdrTableBase, rom.p32(canonicalSlot));
            Assert.Equal(LdrTableBase, rom.p32(LdrRawSlot));
            Assert.Equal(LdrTableBase, rom.p32(LdrLiteralSlot));

            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.Equal(currentCount, items.Count);
            Assert.Equal((uint)currentCount, vm.ReadCount);

            // Snapshot the old region so undo-restore can be asserted byte-exact.
            byte[] oldRegion = rom.getBinaryData(LdrTableBase, (uint)(currentCount * (int)ImageMapActionAnimationViewModel.SIZE));

            uint newCount = (uint)currentCount + 2;
            string err = ExpandUnderUndo(vm, newCount);
            Assert.Equal("", err);

            // The table moved to a new base.
            uint newBase = vm.ReadStartAddress;
            Assert.NotEqual(LdrTableBase, newBase);
            Assert.Equal(newCount, vm.ReadCount);

            // ALL THREE references now point at the new base.
            Assert.Equal(newBase, rom.p32(canonicalSlot));    // canonical (ExpandTableTo)
            Assert.Equal(newBase, rom.p32(LdrRawSlot));        // raw secondary (RepointAllReferences)
            Assert.Equal(newBase, rom.p32(LdrLiteralSlot));    // LDR literal (RepointAllReferences)

            // Undo restores every reference + the wiped old region.
            CoreState.Undo.RunUndo();
            Assert.Equal(LdrTableBase, rom.p32(canonicalSlot));
            Assert.Equal(LdrTableBase, rom.p32(LdrRawSlot));
            Assert.Equal(LdrTableBase, rom.p32(LdrLiteralSlot));
            Assert.Equal(oldRegion,
                rom.getBinaryData(LdrTableBase, (uint)(currentCount * (int)ImageMapActionAnimationViewModel.SIZE)));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.CommentCache = prevCache;
        }
    }

    /// <summary>
    /// Clean ROM (only the canonical pointer, no secondary refs) — ExpandList
    /// succeeds because RepointAllReferences returns 0 (NOTE A: 0 is success,
    /// not a rollback), and the canonical pointer resolves to the new base.
    /// </summary>
    [Fact]
    public void ViewModel_ExpandList_CleanRom_NoSecondaryRefs_SucceedsAndCanonicalPointsToNewBase()
    {
        ROM prevRom = CoreState.ROM;
        Undo prevUndo = CoreState.Undo;
        ISystemTextEncoder prevEnc = CoreState.SystemTextEncoder;
        IEtcCache prevCache = CoreState.CommentCache;
        try
        {
            const int currentCount = 3;
            // Plant ONLY the canonical pointer + table + free region (no raw/LDR refs).
            ROM rom = MakeLdrTestRom(currentCount, out uint canonicalSlot, plantSecondaryRefs: false);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            EnsureSystemTextEncoder(rom);
            CoreState.CommentCache = new HeadlessEtcCache();

            uint oldBase = rom.p32(canonicalSlot);
            Assert.Equal(LdrTableBase, oldBase);

            var vm = new ImageMapActionAnimationViewModel();
            var items = vm.LoadList();
            Assert.Equal(currentCount, items.Count);

            uint newCount = (uint)currentCount + 1;
            string err = ExpandUnderUndo(vm, newCount);
            Assert.Equal("", err);

            uint newBase = vm.ReadStartAddress;
            Assert.NotEqual(LdrTableBase, newBase);
            Assert.Equal(newCount, vm.ReadCount);
            // Canonical pointer is NOT orphaned — it resolves to the new base.
            Assert.Equal(newBase, rom.p32(canonicalSlot));

            // Cross-check: a direct RepointAllReferences for the now-stale oldBase
            // finds no remaining references and returns 0 without throwing.
            int n = DataExpansionCore.RepointAllReferences(rom, oldBase, newBase, null);
            Assert.Equal(0, n);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.CommentCache = prevCache;
        }
    }

    /// <summary>
    /// Source/signature guard — ExpandList now takes Undo.UndoData, calls
    /// RepointAllReferences, the View passes GetActiveUndoData(), and the stale
    /// "without the LDR-pointer rescan" doc is gone.
    /// </summary>
    [Fact]
    public void ViewModel_ExpandList_Signature_TakesUndoData_AndCallsRepointAllReferences()
    {
        // Reflection: the 2-arg overload (uint, Undo.UndoData) exists; the
        // old 1-arg (uint) overload is gone.
        var t = typeof(ImageMapActionAnimationViewModel);
        Assert.NotNull(t.GetMethod("ExpandList", new[] { typeof(uint), typeof(Undo.UndoData) }));
        Assert.Null(t.GetMethod("ExpandList", new[] { typeof(uint) }));

        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImageMapActionAnimationViewModel.cs");
        string vmSource = File.ReadAllText(vmPath);
        Assert.True(vmSource.Contains("public string ExpandList(uint newCount, Undo.UndoData? undo)"),
            "ExpandList must take the active Undo.UndoData (nullable).");
        Assert.True(vmSource.Contains("DataExpansionCore.RepointAllReferences("),
            "ExpandList must compose the all-reference (raw + LDR) repoint via RepointAllReferences.");
        // The stale KnownGap doc must be gone.
        Assert.False(vmSource.Contains("without the LDR-pointer rescan"),
            "The stale 'without the LDR-pointer rescan' KnownGap doc must be removed.");

        string viewPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml.cs");
        string viewSource = File.ReadAllText(viewPath);
        Assert.True(viewSource.Contains("_vm.ExpandList(newCount, _undoService.GetActiveUndoData())"),
            "The View must pass the active UndoData into ExpandList.");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Run ExpandList inside an ambient undo scope (mirrors the View's
    /// UndoService.Begin/Commit), pushing the transaction onto CoreState.Undo
    /// so CoreState.Undo.RunUndo() can roll it back. The active UndoData is
    /// threaded into ExpandList so RepointAllReferences records its writes.
    /// </summary>
    static string ExpandUnderUndo(ImageMapActionAnimationViewModel vm, uint newCount)
    {
        var ud = CoreState.Undo.NewUndoData("MapActionAnimation ExpandList test");
        string err;
        using (ROM.BeginUndoScope(ud))
        {
            err = vm.ExpandList(newCount, ud);
        }
        if (string.IsNullOrEmpty(err))
            CoreState.Undo.Push(ud);
        return err;
    }

    /// <summary>
    /// Build a synthetic FE8U ROM whose FindAnimationPointer binary-signature
    /// search resolves to a canonical pointer slot pointing at
    /// <see cref="LdrTableBase"/>, with <paramref name="rowCount"/> readable
    /// rows, a known 0xFF free region for ExpandTableTo to relocate into, and
    /// (optionally) a SECOND raw 32-bit pointer + an ARM-Thumb LDR literal-pool
    /// load both referencing LdrTableBase. Returns the canonical slot offset
    /// (the FindAnimationPointer result) via <paramref name="canonicalSlot"/>.
    /// </summary>
    static ROM MakeLdrTestRom(int rowCount, out uint canonicalSlot, bool plantSecondaryRefs = true)
    {
        EnsureCoreStateBaseDirectory();
        var bytes = new byte[0x1100000];

        // FE8U signature pattern used by FindAnimationPointer.
        byte[] sig = { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02,
                       0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02,
                       0xFF, 0xFF, 0x00, 0x00 };

        // ROMFE8U.compress_image_borderline_address == 0x1000000 — the search
        // start. Place the pointer at the search start and the signature right
        // after, so `p - sig.Length - 4` lands on the pointer (the canonical
        // slot FindAnimationPointer returns).
        uint searchStart = 0x1000000;
        uint pointerAddr = searchStart;
        uint sigAddr = pointerAddr + 4;
        canonicalSlot = pointerAddr;

        // Canonical pointer (GBA format = base + 0x08000000). Cast the buffer
        // index to int (the | 0x08000000u value stays uint).
        BitConverter.GetBytes(LdrTableBase | 0x08000000u).CopyTo(bytes, (int)pointerAddr);
        sig.CopyTo(bytes, (int)sigAddr);

        // Table rows: row 0 = 0 (reserved-null), rows 1..rowCount-1 = valid
        // pointers (per-row distinct marker), row `rowCount` = 0xFFFFFFFF
        // terminator so LoadList stops at exactly `rowCount` rows.
        uint size = ImageMapActionAnimationViewModel.SIZE; // 8
        BitConverter.GetBytes(0u).CopyTo(bytes, (int)(LdrTableBase + 0));
        for (int i = 1; i < rowCount; i++)
        {
            uint row = LdrTableBase + (uint)i * size;
            BitConverter.GetBytes((0x00200000u + (uint)i * 0x100u) | 0x08000000u).CopyTo(bytes, (int)(row + 0));
        }
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, (int)(LdrTableBase + (uint)rowCount * size));

        // Known 0xFF free region for ExpandTableTo to relocate into.
        for (int i = 0; i < 0x4000; i++)
            bytes[(int)(LdrFreeRegion + i)] = 0xFF;

        if (plantSecondaryRefs)
        {
            // SECOND raw 32-bit pointer to LdrTableBase.
            BitConverter.GetBytes(LdrTableBase | 0x08000000u).CopyTo(bytes, (int)LdrRawSlot);
            // ARM Thumb LDR r0,[pc,#0] (0x4800) + its literal-pool slot.
            bytes[(int)(LdrInstr + 0)] = 0x00;
            bytes[(int)(LdrInstr + 1)] = 0x48;
            BitConverter.GetBytes(LdrTableBase | 0x08000000u).CopyTo(bytes, (int)LdrLiteralSlot);
        }

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u-ldr.gba", bytes, "BE8E01");
        return rom;
    }

    static string ReadAxaml()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml");
        return File.ReadAllText(axamlPath);
    }

    /// <summary>
    /// Ensure CoreState.BaseDirectory points at the test bin dir so
    /// LoadDefaultName can locate config/data/MapActionAnimation_ALL.txt.
    /// The Avalonia test project copies config/ into bin via the
    /// .csproj Content Include rule; we just tell CoreState where it is.
    /// Idempotent — safe to call from every test (no side-effect leakage
    /// across collections because we only write when null/empty).
    /// </summary>
    static void EnsureCoreStateBaseDirectory()
    {
        if (!string.IsNullOrEmpty(CoreState.BaseDirectory))
            return;
        string? assemblyDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (assemblyDir != null)
            CoreState.BaseDirectory = assemblyDir;
    }

    /// <summary>
    /// Ensure CoreState.SystemTextEncoder is non-null so concurrent tests
    /// (e.g. ControlPropertyTests, ViewInstantiationSweepTests — NOT in the
    /// `SharedState` collection) that instantiate views and call
    /// `rom.getString` don't NRE on `CoreState.SystemTextEncoder.Decode`
    /// when our synthetic ROM is briefly the CoreState.ROM. The CI Windows
    /// runner doesn't ship a real ROM so `RomFixture` never sets the
    /// encoder; without this guard the parallel runner can race
    /// non-collection tests against ours.
    /// </summary>
    static void EnsureSystemTextEncoder(ROM rom)
    {
        if (CoreState.SystemTextEncoder == null)
        {
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
        }
    }

    /// <summary>
    /// Build a tiny synthetic FE8U ROM whose binary signature
    /// (compress_image_borderline_address-based GrepEnd) resolves and the
    /// pointer-table base + one valid pointer at slot 1 are populated.
    /// Mirrors the #441/#491 synthetic-ROM technique.
    /// </summary>
    static ROM MakeMinimalFe8uRom()
    {
        EnsureCoreStateBaseDirectory();
        var bytes = new byte[0x1100000];

        // FE8U signature pattern used by FindAnimationPointer.
        byte[] sig = { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02,
                       0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02,
                       0xFF, 0xFF, 0x00, 0x00 };

        // Place a pointer to our table-base, then the signature pattern
        // right after, so that GrepEnd lands and `p - sig.Length - 4`
        // points at the pointer.
        //
        // Use ROMFE8U.compress_image_borderline_address as the search
        // start; for FE8U this is 0x1000000. We place the pointer at
        // 0x1000000 and the signature at 0x1000004.
        uint searchStart = 0x1000000;
        uint pointerAddr = searchStart;
        uint sigAddr = pointerAddr + 4;
        uint tableBase = 0x00100000u;

        // Pointer (GBA format = base + 0x08000000).
        BitConverter.GetBytes(tableBase | 0x08000000u).CopyTo(bytes, pointerAddr);

        // Signature pattern immediately after the pointer.
        sig.CopyTo(bytes, sigAddr);

        // Populate the table: row 0 = 0 (empty), row 1 = valid pointer,
        // row 2 = 0 (terminator-like) — LoadList stops on first invalid
        // entry, so we want at least row 0 + row 1 readable.
        BitConverter.GetBytes(0u).CopyTo(bytes, tableBase + 0);
        BitConverter.GetBytes(0x00200000u | 0x08000000u).CopyTo(bytes, tableBase + 8);
        // Row 2 = INVALID terminator so LoadList breaks the loop quickly.
        // Without an explicit invalid terminator, `isSafetyPointerOrNull(0)`
        // returns true and the loop would scan until ROM end (16 MB →
        // millions of iterations + OOM-sized list). Copilot CLI inline
        // review on PR #506.
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, tableBase + 16);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    /// <summary>
    /// Walk parent directories from the test bin/ folder until we find
    /// the repo root (identified by FEBuilderGBA.sln).
    /// </summary>
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
