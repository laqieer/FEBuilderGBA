// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 0 / Phase 1 tests — PairMatcher pair-discovery logic. (#374)
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="PairMatcher.DiscoverAll"/>. The tests exercise the live
/// repo (worktree resolved via the AppContext base directory + walk-up) so they
/// double as a regression check on the actual pair-map we report in the density
/// report. A synthetic in-memory case would not catch real ListParityHelper
/// drift.
/// </summary>
public class GapSweepPairMatcherTests
{
    /// <summary>
    /// Locate the repo root by walking up from the test-bin directory until we
    /// find FEBuilderGBA.sln. This is the same algorithm the App uses when
    /// running --gap-sweep-density without an explicit --repo-root.
    /// </summary>
    static string FindRepoRoot()
    {
        string start = AppContext.BaseDirectory;
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not find FEBuilderGBA.sln (test must run from inside the worktree).");
    }

    // ---------------- Known-good ListParityHelper pairs ----------------

    [Fact]
    public void KnownPair_UnitForm_UnitEditorView_DiscoveredViaListParityHelper()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        var unit = pairs.SingleOrDefault(p => p.WfFormName == "UnitForm" && p.AvViewName == "UnitEditorView");
        Assert.NotNull(unit);
        Assert.Equal(MatchMethod.ListParityHelper, unit!.Match);
        Assert.Equal(Confidence.High, unit.Confidence);
        Assert.NotNull(unit.WfPath);
        Assert.NotNull(unit.AvPath);
    }

    [Fact]
    public void KnownPair_ItemForm_ItemEditorView_DiscoveredViaListParityHelper()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        var item = pairs.SingleOrDefault(p => p.WfFormName == "ItemForm" && p.AvViewName == "ItemEditorView");
        Assert.NotNull(item);
        Assert.Equal(MatchMethod.ListParityHelper, item!.Match);
        Assert.Equal(Confidence.High, item.Confidence);
    }

    [Fact]
    public void KnownPair_ClassForm_ClassEditorView_DiscoveredViaListParityHelper()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        var cls = pairs.SingleOrDefault(p => p.WfFormName == "ClassForm" && p.AvViewName == "ClassEditorView");
        Assert.NotNull(cls);
        Assert.Equal(MatchMethod.ListParityHelper, cls!.Match);
    }

    // ---------------- Heuristic suffix-strip fallbacks ----------------

    [Fact]
    public void Heuristic_StripsFormSuffixAndMatchesViewSuffix()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        // EventScriptForm has no ListParityHelper mapping. Because the on-disk
        // form base name (EventScript) matches the on-disk view base name
        // (EventScript → EventScriptView), the exact-name pre-pass picks it up
        // with Heuristic match + High confidence (the names line up exactly).
        var pair = pairs.SingleOrDefault(p => p.WfFormName == "EventScriptForm" && p.AvViewName == "EventScriptView");
        Assert.NotNull(pair);
        Assert.Equal(MatchMethod.Heuristic, pair!.Match);
        Assert.Equal(Confidence.High, pair.Confidence);
    }

    [Fact]
    public void Heuristic_HandlesVersionSuffixForm_ItemFE6Form()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        // ItemFE6Form has no ListParityHelper mapping. The exact-name pre-pass
        // matches `ItemFE6View` → strip "View" → `ItemFE6` → on-disk `ItemFE6Form`
        // exists → emit pair as Heuristic + High (exact-name matches earn High).
        var pair = pairs.SingleOrDefault(p => p.WfFormName == "ItemFE6Form" && p.AvViewName == "ItemFE6View");
        Assert.NotNull(pair);
        Assert.Equal(MatchMethod.Heuristic, pair!.Match);
        Assert.Equal(Confidence.High, pair.Confidence);
    }

    // ---- Regression: exact same-base-name wins over ListParityHelper cross-map ----

    [Fact]
    public void ExactBaseName_ClassFE6_PrefersDirectPairing()
    {
        // ListParityHelper maps `ClassFE6View` to `ClassForm` (shared-impl form),
        // but `ClassFE6Form.cs` ALSO exists on disk. The natural pairing is
        // ClassFE6Form ↔ ClassFE6View; the cross-map to ClassForm would put
        // ClassFE6Form into the WF-orphan section and double-count ClassForm
        // (it's separately mapped to ClassEditorView too). PR #375 review (#374
        // tracking) caught this — the pair matcher must prefer the on-disk
        // same-base-name match.
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());

        // Direct pair must exist.
        var direct = pairs.SingleOrDefault(p => p.WfFormName == "ClassFE6Form" && p.AvViewName == "ClassFE6View");
        Assert.NotNull(direct);
        Assert.True(direct!.Match == MatchMethod.ListParityHelper || direct.Match == MatchMethod.Heuristic);
        Assert.Equal(Confidence.High, direct.Confidence);

        // ClassFE6Form should NOT appear as a WF-only orphan.
        var orphan = pairs.FirstOrDefault(p =>
            p.WfFormName == "ClassFE6Form" && p.AvViewName == null && p.Match == MatchMethod.Orphan);
        Assert.Null(orphan);

        // And no row should map ClassForm to ClassFE6View any more.
        var crossMap = pairs.FirstOrDefault(p => p.WfFormName == "ClassForm" && p.AvViewName == "ClassFE6View");
        Assert.Null(crossMap);
    }

    [Theory]
    [InlineData("UnitEditorView", "Unit")]
    [InlineData("ItemEditorView", "Item")]
    [InlineData("PortraitViewerView", "Portrait")]
    [InlineData("UnitFE6View", "UnitFE6")]
    [InlineData("ClassFE6View", "ClassFE6")]
    [InlineData("MainWindow", null)] // no view suffix
    public void StripViewSuffix_ReturnsExpectedBaseName(string viewName, string? expectedBase)
    {
        Assert.Equal(expectedBase, PairMatcher.StripViewSuffix(viewName));
    }

    // ---------------- Main forms excluded ----------------

    [Theory]
    [InlineData("MainFE6Form")]
    [InlineData("MainFE7Form")]
    [InlineData("MainFE8Form")]
    [InlineData("MainSimpleMenuForm")]
    public void MainForms_ExcludedFromPairs(string formName)
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        Assert.DoesNotContain(pairs, p => p.WfFormName == formName);
    }

    [Fact]
    public void MainWindow_ExcludedFromPairs()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        Assert.DoesNotContain(pairs, p => p.AvViewName == "MainWindow");
    }

    // ---------------- Designer.cs files are NOT separate pairs ----------------

    [Fact]
    public void DesignerCs_NotTreatedAsSeparateForm()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        // No pair should have a WfFormName ending in ".Designer" — the pair
        // matcher must strip the Designer.cs files before constructing the form
        // name. (Designer.cs files are partial-class siblings, not separate forms.)
        Assert.DoesNotContain(pairs, p => p.WfFormName != null && p.WfFormName.EndsWith(".Designer"));
        // ClassForm legitimately maps to two AV views via ListParityHelper
        // (ClassEditorView + ClassFE6View), so we don't assert "each form once".
        // The invariant we DO want is that each form's pair carries the
        // expected absolute path with no ".Designer.cs" suffix anywhere.
        foreach (var p in pairs.Where(p => p.WfPath != null))
        {
            Assert.False(p.WfPath!.EndsWith(".Designer.cs", System.StringComparison.OrdinalIgnoreCase),
                $"WfPath must not point at a Designer.cs file: {p.WfPath}");
        }
    }

    // ---------------- Orphan rows ----------------

    [Fact]
    public void Orphans_HaveLowConfidence_And_OnlyOneSide()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        foreach (var p in pairs.Where(p => p.Match == MatchMethod.Orphan))
        {
            Assert.Equal(Confidence.Low, p.Confidence);
            // Exactly one side null:
            bool wfMissing = p.WfFormName == null;
            bool avMissing = p.AvViewName == null;
            Assert.True(wfMissing ^ avMissing,
                $"Orphan must have exactly one side null: WF={p.WfFormName} AV={p.AvViewName}");
        }
    }

    [Fact]
    public void Pairs_AreDeterministicAcrossRuns()
    {
        // Re-run the discovery; rows must come back in the same order so
        // committed reports diff cleanly.
        var a = PairMatcher.DiscoverAll(FindRepoRoot());
        var b = PairMatcher.DiscoverAll(FindRepoRoot());
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].WfFormName, b[i].WfFormName);
            Assert.Equal(a[i].AvViewName, b[i].AvViewName);
            Assert.Equal(a[i].Match, b[i].Match);
        }
    }

    // ---------------- Sanity checks on the live repo ----------------

    [Fact]
    public void Discovery_FindsAtLeastOneHundredPairs()
    {
        // The Avalonia migration has ~300+ views and the WF tree has ~300 forms.
        // A discovery returning fewer than 100 is almost certainly a globbing bug.
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        Assert.InRange(pairs.Count, 100, 1000);
    }

    [Fact]
    public void AllListParityHelperEditors_AppearInPairDiscovery()
    {
        // Every AV view name registered in ListParityHelper must appear in the
        // pair list with High confidence (whether the match was applied via the
        // exact-base-name pre-pass — which can shadow the helper's cross-mapping
        // when both XForm.cs and XView.axaml exist — or via the helper itself).
        // We don't require MatchMethod == ListParityHelper anymore because the
        // exact-name pre-pass is allowed to shadow the helper when its WF target
        // and the AV view's base name diverge.
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot()).ToList();
        var listParityNames = ListParityHelper.GetAllMappedEditors()
            .Where(n => n != "MainWindow")
            .ToList();
        foreach (string name in listParityNames)
        {
            var entry = pairs.FirstOrDefault(p => p.AvViewName == name && p.Match != MatchMethod.Orphan);
            Assert.True(entry != null,
                $"ListParityHelper editor '{name}' missing from pair discovery.");
            Assert.Equal(Confidence.High, entry!.Confidence);
        }
    }

    [Fact]
    public void NoPair_HasBothSidesNull()
    {
        var pairs = PairMatcher.DiscoverAll(FindRepoRoot());
        foreach (var p in pairs)
        {
            Assert.False(p.WfFormName == null && p.AvViewName == null,
                "Pair must have at least one side populated.");
        }
    }

    [Fact]
    public void DiscoverAll_ThrowsOnEmptyOrInvalidRoot()
    {
        Assert.Throws<System.ArgumentException>(() => PairMatcher.DiscoverAll(""));
        Assert.Throws<DirectoryNotFoundException>(() => PairMatcher.DiscoverAll("Z:/does-not-exist-zzzzz"));
    }
}
