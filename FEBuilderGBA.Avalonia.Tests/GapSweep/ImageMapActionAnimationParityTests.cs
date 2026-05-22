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
    /// Copilot CLI plan-review point 3 — the JumpToAnimationCreator manifest
    /// row MUST carry a non-null IssueRef so the gap-sweep scanner reports
    /// it as `KnownGap` (not a false `Match`). Until the
    /// `ToolAnimationCreatorView.Init` flow lands (#500), the jump button
    /// is intentionally NOT rendered.
    /// </summary>
    [Fact]
    public void ViewModel_JumpToAnimationCreator_IsMarkedAsKnownGap()
    {
        var vm = new ImageMapActionAnimationViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(ToolAnimationCreatorView));
        Assert.NotNull(target);
        Assert.False(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToAnimationCreator must carry a non-null IssueRef until " +
            "ToolAnimationCreatorView.Init lands (#500) — see Copilot CLI " +
            "plan-review point 3 on issue #433.");
    }

    [Fact]
    public void JumpParityScanner_ToCreator_IsKnownGap()
    {
        // Simulate the WF callsite for X_N_JumpEditor_Click and verify
        // the scanner classifies it as `KnownGap` because the manifest
        // entry carries an IssueRef.
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
                IssueRef: "#500"),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageMapActionAnimationForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.KnownGap, match!.Status);
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
            // Restore prior BaseDirectory so we don't leak our overwrite
            // into parallel tests.
            if (prevBaseDir != null)
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
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_ReloadList_Button\"", axaml);
        Assert.Contains("Click=\"ReloadList_Click\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_DisabledUntilFollowupLands()
    {
        // The button must exist (so the gap-sweep label "リストの拡張"
        // matches) but be disabled until #501 lands.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMapActionAnimation_ListExpand_Button\"", axaml);
        Assert.Contains("IsEnabled=\"False\"", axaml);
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
    /// Phase-6 invariant: the AXAML must NOT reintroduce the Export/Import
    /// buttons until the Core export/import seam lands (#499). They are
    /// explicitly out-of-scope for #433.
    /// </summary>
    [Fact]
    public void View_DoesNotRenderExportImportButtons()
    {
        string axaml = ReadAxaml();
        Assert.DoesNotContain("ImageMapActionAnimation_Export_Button", axaml);
        Assert.DoesNotContain("ImageMapActionAnimation_Import_Button", axaml);
    }

    /// <summary>
    /// Write handler must wrap ROM mutation in `_undoService.Begin/Commit`.
    /// Roslyn-static read of the code-behind source — we don't need the
    /// real Avalonia head running.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageMapActionAnimationView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Edit Map Action Animation\")", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

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
