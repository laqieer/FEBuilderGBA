// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 tests — JumpParityScanner WF callsite extraction + AV reflection +
// status classification. (#374)
//
// All tests are pure unit-level: WF-side tests use in-memory source strings
// (no temp files), AV-side tests use a synthetic test type implementing
// INavigationTargetSource. The scanner is exercised against synthetic inputs
// so the tests are deterministic and independent of the live repo state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Unit tests for <see cref="JumpParityScanner"/>'s scanner-only logic — the
/// repo-driven integration test lives in <see cref="AvaloniaJumpParityTests"/>.
/// </summary>
public class JumpParityScannerTests
{
    // =====================================================================
    // ExtractCallsitesFromSource — Roslyn extracts WF JumpForm callsites.
    // =====================================================================

    [Fact]
    public void ExtractCallsites_BasicJumpForm()
    {
        // Standard designer-style invocation; expect one callsite.
        string src = @"
namespace X {
    class FooForm {
        void OnJump() {
            InputFormRef.JumpForm<TargetForm>(addr);
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Single(rows);
        Assert.Equal("FooForm", rows[0].SourceForm);
        Assert.Equal("TargetForm", rows[0].TargetForm);
        Assert.True(rows[0].HasAddressArgument);
    }

    [Fact]
    public void ExtractCallsites_JumpFormLowAlsoMatches()
    {
        // JumpFormLow has the same parity meaning (open without preselect).
        string src = @"
namespace X {
    class FooForm {
        void OnJump() {
            InputFormRef.JumpFormLow<TargetForm>();
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Single(rows);
        Assert.Equal("FooForm", rows[0].SourceForm);
        Assert.Equal("TargetForm", rows[0].TargetForm);
        Assert.False(rows[0].HasAddressArgument);
    }

    [Fact]
    public void ExtractCallsites_IgnoresUnrelatedInvocations()
    {
        // Bare JumpForm without InputFormRef receiver — REJECT (could be
        // anything else with the same method name).
        string src = @"
namespace X {
    class F {
        void M() {
            // Not the canonical receiver — must be ignored.
            myService.JumpForm<TargetForm>(0);
            // Method name doesn't match — must be ignored.
            InputFormRef.NavigateForm<TargetForm>(0);
            // Bare method invocation without receiver — must be ignored.
            JumpForm<TargetForm>(0);
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Empty(rows);
    }

    [Fact]
    public void ExtractCallsites_MultiplePerFile()
    {
        // Designer with two distinct jumps; both must surface.
        string src = @"
namespace X {
    class FooForm {
        void A() { InputFormRef.JumpForm<TargetA>(addr); }
        void B() { InputFormRef.JumpForm<TargetB>(0); }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.TargetForm == "TargetA");
        Assert.Contains(rows, r => r.TargetForm == "TargetB");
    }

    [Fact]
    public void ExtractCallsites_RecognisesQualifiedReceiver()
    {
        // `Some.Namespace.InputFormRef.JumpForm<T>` — the qualified form.
        // Receiver-name match must look at the trailing identifier.
        string src = @"
namespace X {
    class FooForm {
        void OnJump() {
            FEBuilderGBA.InputFormRef.JumpForm<TargetForm>(addr);
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Single(rows);
        Assert.Equal("TargetForm", rows[0].TargetForm);
    }

    [Fact]
    public void ExtractCallsites_PartialClassEnclosingResolution()
    {
        // The enclosing class is the partial declaration the invocation lives
        // in, not the file name — we test the resolver by putting the call
        // inside a nested class.
        string src = @"
namespace X {
    partial class OuterForm {
        public class InnerHelper {
            public void Jump() {
                InputFormRef.JumpForm<TargetForm>();
            }
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Single(rows);
        // Enclosing class is the IMMEDIATE class — InnerHelper, not OuterForm.
        Assert.Equal("InnerHelper", rows[0].SourceForm);
    }

    [Fact]
    public void ExtractCallsites_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(JumpParityScanner.ExtractCallsitesFromSource(""));
        Assert.Empty(JumpParityScanner.ExtractCallsitesFromSource(null!));
    }

    [Fact]
    public void ExtractCallsites_MalformedSource_DoesNotCrash()
    {
        // Roslyn parses unparseable code into an error tree — we should NOT
        // crash, just return what we can recover.
        string src = @"
class FooForm {
    void M() {
        InputFormRef.JumpForm<TargetForm>(addr);
        // intentional syntax error vvvvvv
        if (foo bar baz quux)
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        // The callsite preceding the syntax error must still be recovered.
        Assert.Single(rows);
        Assert.Equal("TargetForm", rows[0].TargetForm);
    }

    [Fact]
    public void ExtractCallsites_FiltersDispatcherClass_InputFormRef()
    {
        // Per Copilot PR #379 review concern #1: InputFormRef's own callsites
        // (~130 of them) are the dispatcher itself, not real navigation
        // sources. They must be filtered out so the report focuses on
        // editor-to-editor jumps.
        string src = @"
namespace X {
    class InputFormRef {
        void DispatchByLinkType() {
            InputFormRef.JumpForm<UnitForm>(value);
            InputFormRef.JumpForm<ClassForm>(value);
        }
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Empty(rows);
    }

    [Fact]
    public void ExtractCallsites_FiltersShellClass_MainFEForm()
    {
        // Main shell forms open editors at startup — no source-editor context.
        string src = @"
class MainFE8Form {
    void OnButton() {
        InputFormRef.JumpForm<ClassForm>();
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Empty(rows);
    }

    [Fact]
    public void ExtractCallsites_KeepsRealEditorClass()
    {
        // A real editor Form whose code-behind navigates to another editor
        // MUST be kept — this is the canonical cross-editor jump.
        string src = @"
class ItemForm {
    void OnJumpToEffectiveness() {
        InputFormRef.JumpForm<ItemEffectivenessForm>(addr);
    }
}";
        var rows = JumpParityScanner.ExtractCallsitesFromSource(src);
        Assert.Single(rows);
        Assert.Equal("ItemForm", rows[0].SourceForm);
        Assert.Equal("ItemEffectivenessForm", rows[0].TargetForm);
    }

    [Fact]
    public void IsDispatcherOrShellClass_KnownDispatchers()
    {
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("InputFormRef"));
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("SkillUtil"));
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("MainFE6Form"));
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("MainFE7Form"));
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("MainFE8Form"));
        Assert.True(JumpParityScanner.IsDispatcherOrShellClass("MainSimpleMenuForm"));
        Assert.False(JumpParityScanner.IsDispatcherOrShellClass("ItemForm"));
        Assert.False(JumpParityScanner.IsDispatcherOrShellClass("ClassForm"));
        Assert.False(JumpParityScanner.IsDispatcherOrShellClass(""));
    }

    // =====================================================================
    // DeriveViewNameFromVmName — VM→View name conversion.
    // =====================================================================

    [Fact]
    public void DeriveViewName_StandardSuffix()
    {
        Assert.Equal("ClassEditorView", JumpParityScanner.DeriveViewNameFromVmName("ClassEditorViewModel"));
        Assert.Equal("ItemEditorView", JumpParityScanner.DeriveViewNameFromVmName("ItemEditorViewModel"));
        Assert.Equal("CCBranchEditorView", JumpParityScanner.DeriveViewNameFromVmName("CCBranchEditorViewModel"));
    }

    [Fact]
    public void DeriveViewName_NonViewModelTypeFallsThrough()
    {
        // Names that don't follow the convention pass through unchanged —
        // a defensive diagnostic signal rather than a silent default.
        Assert.Equal("RandomClass", JumpParityScanner.DeriveViewNameFromVmName("RandomClass"));
    }

    [Fact]
    public void DeriveViewName_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", JumpParityScanner.DeriveViewNameFromVmName(""));
    }

    // =====================================================================
    // ScanAvManifests — reflection over the assembly.
    // =====================================================================

    [Fact]
    public void ScanAvManifests_FindsImplementations()
    {
        // Phase 4 wires INavigationTargetSource onto ~7 VMs — at minimum, the
        // ClassEditor entries should surface.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        Assert.NotEmpty(manifests);
        Assert.Contains(manifests, m => m.SourceVm == "ClassEditorViewModel");
        Assert.Contains(manifests, m => m.SourceView == "ClassEditorView");
    }

    [Fact]
    public void ScanAvManifests_SurvivesVmConstructorFailure()
    {
        // We're using the real assembly here — VMs whose construction fails
        // (e.g. they touch CoreState.ROM in their ctor and CoreState is null
        // in the test environment) must be silently skipped. The scanner
        // returning ANY result is itself the assertion: an unhandled ctor
        // exception would have thrown.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        Assert.NotNull(manifests);
    }

    [Fact]
    public void ScanAvManifests_NullAssembly_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JumpParityScanner.ScanAvManifests(null!));
    }

    // =====================================================================
    // ComputeJumpRows — status classification logic.
    // =====================================================================

    [Fact]
    public void ComputeJumpRows_EmptyInputs_ReturnsEmpty()
    {
        var rows = JumpParityScanner.ComputeJumpRows(
            Array.Empty<WfJumpCallsite>(),
            Array.Empty<AvManifestEntry>());
        Assert.Empty(rows);
    }

    [Fact]
    public void ComputeJumpRows_WfOnlyEntry_SurfacesMissingAvManifest()
    {
        // WF callsite from ClassForm → MoveCostForm; AV side empty.
        // (ListParityHelper maps ClassForm → {ClassEditorView, ClassFE6View},
        //  MoveCostForm → MoveCostEditorView, so the expansion should produce
        //  rows.)
        var wf = new[] { new WfJumpCallsite("ClassForm", "MoveCostForm", true) };
        var rows = JumpParityScanner.ComputeJumpRows(wf, Array.Empty<AvManifestEntry>());
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(JumpRowStatus.MissingAvManifest, r.Status));
        // Every row should carry the WF source and target form names.
        Assert.All(rows, r => Assert.Equal("ClassForm", r.SourceForm));
        Assert.All(rows, r => Assert.Equal("MoveCostForm", r.TargetWfType));
    }

    [Fact]
    public void ComputeJumpRows_AvOnlyEntry_SurfacesNoWfCallsite()
    {
        // AV manifest entry that doesn't correspond to any WF callsite.
        var av = new[] { new AvManifestEntry(
            SourceVm: "TestVmModel",
            SourceView: "TestView",
            Command: "JumpToOther",
            TargetView: "OtherView",
            IssueRef: null) };
        var rows = JumpParityScanner.ComputeJumpRows(Array.Empty<WfJumpCallsite>(), av);
        Assert.Single(rows);
        Assert.Equal(JumpRowStatus.NoWfCallsite, rows[0].Status);
        Assert.Null(rows[0].IssueRef);
    }

    [Fact]
    public void ComputeJumpRows_AvOnlyWithIssueRef_SurfacesKnownGap()
    {
        // AV manifest with IssueRef must be classified as KnownGap, NOT
        // NoWfCallsite, even when there's no WF callsite to match.
        var av = new[] { new AvManifestEntry(
            SourceVm: "TestVmModel",
            SourceView: "TestView",
            Command: "JumpToBroken",
            TargetView: "BrokenView",
            IssueRef: "#999") };
        var rows = JumpParityScanner.ComputeJumpRows(Array.Empty<WfJumpCallsite>(), av);
        Assert.Single(rows);
        Assert.Equal(JumpRowStatus.KnownGap, rows[0].Status);
        Assert.Equal("#999", rows[0].IssueRef);
    }

    [Fact]
    public void ComputeJumpRows_PreservesIssueRefThroughClassification()
    {
        // IssueRef must pass through the row regardless of how the row was
        // classified. We assert both with-issue and without-issue rows.
        var av = new[]
        {
            new AvManifestEntry("VmA", "ViewA", "Cmd1", "TargetX", IssueRef: "#100"),
            new AvManifestEntry("VmB", "ViewB", "Cmd2", "TargetY", IssueRef: null),
        };
        var rows = JumpParityScanner.ComputeJumpRows(Array.Empty<WfJumpCallsite>(), av);
        Assert.Equal(2, rows.Count);
        var rowWithIssue = rows.Single(r => r.SourceView == "ViewA");
        Assert.Equal("#100", rowWithIssue.IssueRef);
        Assert.Equal(JumpRowStatus.KnownGap, rowWithIssue.Status);
        var rowNoIssue = rows.Single(r => r.SourceView == "ViewB");
        Assert.Null(rowNoIssue.IssueRef);
        Assert.Equal(JumpRowStatus.NoWfCallsite, rowNoIssue.Status);
    }

    [Fact]
    public void ComputeJumpRows_DeterministicOrdering()
    {
        // Same inputs in different orders must produce the same output rows
        // in the same order. Status-asc, then source-view, then target.
        var wf1 = new[]
        {
            new WfJumpCallsite("ZForm", "Target1", true),
            new WfJumpCallsite("AForm", "Target2", true),
        };
        var wf2 = wf1.Reverse().ToArray();
        var rows1 = JumpParityScanner.ComputeJumpRows(wf1, Array.Empty<AvManifestEntry>());
        var rows2 = JumpParityScanner.ComputeJumpRows(wf2, Array.Empty<AvManifestEntry>());
        Assert.Equal(rows1.Count, rows2.Count);
        for (int i = 0; i < rows1.Count; i++)
        {
            Assert.Equal(rows1[i].SourceForm, rows2[i].SourceForm);
            Assert.Equal(rows1[i].TargetWfType, rows2[i].TargetWfType);
        }
    }

    // =====================================================================
    // BuildWfFormToAvViewsMap — inverse lookup table.
    // =====================================================================

    [Fact]
    public void BuildWfFormToAvViewsMap_ReturnsListParityHelperMappings()
    {
        // ListParityHelper has hundreds of mappings — at minimum, some common
        // forms should appear here.
        var map = JumpParityScanner.BuildWfFormToAvViewsMap();
        Assert.NotEmpty(map);
        Assert.Contains("ClassForm", map.Keys);
        // ClassForm should expand to multiple AV views (ClassEditorView +
        // ClassFE6View) — the multi-mapping is what makes this map useful.
        Assert.Contains("ClassEditorView", map["ClassForm"]);
    }

    [Fact]
    public void BuildWfFormToAvViewsMap_WithRepoRoot_HasBroaderCoverage()
    {
        // Per Copilot PR #379 review concern #2: when given a repo root, the
        // PairMatcher discovery layer adds more form↔view pairs beyond what
        // ListParityHelper tracks. Compare the two maps' key counts.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return;
        var withoutPairMatcher = JumpParityScanner.BuildWfFormToAvViewsMap();
        var withPairMatcher = JumpParityScanner.BuildWfFormToAvViewsMap(repoRoot);
        // The PairMatcher layer is additive — it never removes entries, only
        // adds them. So withPairMatcher should have >= keys.
        Assert.True(withPairMatcher.Count >= withoutPairMatcher.Count,
            $"PairMatcher layer should add entries. Without={withoutPairMatcher.Count}, With={withPairMatcher.Count}");
    }

    // =====================================================================
    // Integration smoke test — Scan() against the live worktree.
    // =====================================================================

    [Fact]
    public void Scan_AgainstLiveWorktree_ProducesRows()
    {
        // Walk up to find the worktree root (FEBuilderGBA.sln). This
        // mirrors the FindRepoRoot logic in App.axaml.cs.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            // Running outside the source tree — the scanner should still
            // gracefully degrade.
            return;
        }
        var rows = JumpParityScanner.Scan(repoRoot);
        // The repo has hundreds of InputFormRef.JumpForm callsites; expect
        // a non-trivial row count.
        Assert.NotEmpty(rows);
        // We seeded several VMs with INavigationTargetSource; at LEAST one
        // KnownGap entry should be present. The #359/#360/#362/#363/#365
        // gaps are now closed (their IssueRef tags dropped); the remaining
        // open IssueRef-backed gaps are #374/#385/#500.
        Assert.Contains(rows, r => r.Status == JumpRowStatus.KnownGap);
    }

    [Fact]
    public void Scan_MissingRepoRoot_GracefullyDegrades()
    {
        // Non-existent path — the scanner should still return AV manifest
        // rows from reflection, even though WF callsites come up empty.
        var rows = JumpParityScanner.Scan(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(rows);
        // The reflection side still discovers manifests; expect at least
        // the KnownGap rows we wired.
        Assert.Contains(rows, r => r.Status == JumpRowStatus.KnownGap);
    }

    [Fact]
    public void Scan_NullRepoRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => JumpParityScanner.Scan(""));
        Assert.Throws<ArgumentException>(() => JumpParityScanner.Scan(null!));
    }

    static string? FindRepoRoot()
    {
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        return null;
    }
}
