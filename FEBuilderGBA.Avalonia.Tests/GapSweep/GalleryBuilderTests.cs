// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 3 tests — GalleryBuilder PNG pairing + markdown emission + expected-editor lookup. (#374)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="GalleryBuilder"/>. These cover:
///   1. Basename pairing with the literal `WinForms_` / `Avalonia_` prefixes
///      and the `_{romTag}.png` suffix (no underscore-splitting — Copilot
///      review #1).
///   2. Asymmetric capture classification (AvOnly / WfOnly).
///   3. Expected-editor cross-check (`MissingFromExpected`).
///   4. Empty / missing directories tolerated.
///   5. Side-by-side markdown table emission.
///   6. LF-only newlines in the generated markdown (consistent with Phase 0/1/2).
///   7. `LoadExpectedEditorsFromFile` parses the live coverage doc and a fixture.
/// </summary>
public class GalleryBuilderTests : IDisposable
{
    readonly string _tempRoot;

    public GalleryBuilderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "fbgba-gallery-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    // Helpers ----------------------------------------------------------------

    string MakeDir(string sub)
    {
        string d = Path.Combine(_tempRoot, sub);
        Directory.CreateDirectory(d);
        return d;
    }

    static void TouchPng(string dir, string fileName)
    {
        // Files are empty by design — GalleryBuilder only reads file names.
        File.WriteAllBytes(Path.Combine(dir, fileName), Array.Empty<byte>());
    }

    // =====================================================================
    // BuildGallery — happy path, identical capture sets pair cleanly.
    // =====================================================================

    [Fact]
    public void BuildGallery_PairsFilesByInnerBasename()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_UnitEditorView_FE8U.png");
        TouchPng(av, "Avalonia_UnitEditorView_FE8U.png");
        TouchPng(wf, "WinForms_ItemEditorView_FE8U.png");
        TouchPng(av, "Avalonia_ItemEditorView_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Equal(2, report.Pairs.Count);
        Assert.Empty(report.AvOnly);
        Assert.Empty(report.WfOnly);
        // Sorted Ordinal: ItemEditorView before UnitEditorView.
        Assert.Equal("ItemEditorView", report.Pairs[0].EditorName);
        Assert.Equal("UnitEditorView", report.Pairs[1].EditorName);
        Assert.EndsWith("WinForms_ItemEditorView_FE8U.png", report.Pairs[0].WfImagePath);
        Assert.EndsWith("Avalonia_ItemEditorView_FE8U.png", report.Pairs[0].AvImagePath);
    }

    [Fact]
    public void BuildGallery_HandlesUnderscoreInViewName()
    {
        // Real example: ToolWorkSupport_SelectUPSView contains an underscore.
        // If we ever split on `_` to recover the inner name, this test would
        // fail catastrophically (we'd pair "ToolWorkSupport" against
        // "ToolWorkSupport" but consider "SelectUPSView" an unrelated row).
        // The scanner strips by literal prefix/suffix, so the full inner name
        // is preserved.
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_ToolWorkSupport_SelectUPSView_FE8U.png");
        TouchPng(av, "Avalonia_ToolWorkSupport_SelectUPSView_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Single(report.Pairs);
        Assert.Equal("ToolWorkSupport_SelectUPSView", report.Pairs[0].EditorName);
    }

    // =====================================================================
    // Asymmetric capture classification
    // =====================================================================

    [Fact]
    public void BuildGallery_PopulatesAvOnly()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(av, "Avalonia_OrphanAvView_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Empty(report.Pairs);
        Assert.Single(report.AvOnly);
        Assert.Empty(report.WfOnly);
        Assert.Equal("OrphanAvView", report.AvOnly[0]);
    }

    [Fact]
    public void BuildGallery_PopulatesWfOnly()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_OrphanWfView_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Empty(report.Pairs);
        Assert.Empty(report.AvOnly);
        Assert.Single(report.WfOnly);
        Assert.Equal("OrphanWfView", report.WfOnly[0]);
    }

    [Fact]
    public void BuildGallery_MixedSymmetricAndAsymmetric()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_Shared_FE8U.png");
        TouchPng(av, "Avalonia_Shared_FE8U.png");
        TouchPng(wf, "WinForms_OnlyWf_FE8U.png");
        TouchPng(av, "Avalonia_OnlyAv_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Single(report.Pairs);
        Assert.Equal("Shared", report.Pairs[0].EditorName);
        Assert.Single(report.AvOnly);
        Assert.Equal("OnlyAv", report.AvOnly[0]);
        Assert.Single(report.WfOnly);
        Assert.Equal("OnlyWf", report.WfOnly[0]);
    }

    [Fact]
    public void BuildGallery_IgnoresUnrelatedPngs()
    {
        // Files that don't match the prefix or use a different ROM tag get
        // silently skipped — they're either unrelated PNGs or captures from
        // another ROM in the same scratch directory.
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_UnitEditorView_FE8U.png");
        TouchPng(wf, "WinForms_UnitEditorView_FE7U.png"); // wrong ROM tag
        TouchPng(wf, "unrelated.png"); // no prefix
        TouchPng(av, "Avalonia_UnitEditorView_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");

        Assert.Single(report.Pairs);
        Assert.Equal("UnitEditorView", report.Pairs[0].EditorName);
        Assert.Empty(report.WfOnly);
    }

    // =====================================================================
    // Expected-editor cross-check
    // =====================================================================

    [Fact]
    public void BuildGallery_PopulatesMissingFromExpected()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_CapturedView_FE8U.png");
        TouchPng(av, "Avalonia_CapturedView_FE8U.png");

        var expected = new[] { "CapturedView", "ExpectedButMissingView", "AnotherMissingView" };
        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U", expected);

        Assert.Single(report.Pairs);
        Assert.Equal(2, report.MissingFromExpected.Count);
        Assert.Contains("AnotherMissingView", report.MissingFromExpected);
        Assert.Contains("ExpectedButMissingView", report.MissingFromExpected);
        // Sorted Ordinal.
        Assert.Equal("AnotherMissingView", report.MissingFromExpected[0]);
    }

    [Fact]
    public void BuildGallery_MissingFromExpected_EmptyWhenExpectedIsNull()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(wf, "WinForms_X_FE8U.png");
        TouchPng(av, "Avalonia_X_FE8U.png");

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U", expectedEditors: null);

        Assert.Empty(report.MissingFromExpected);
    }

    [Fact]
    public void BuildGallery_AvOnlyAndWfOnlyCountTowardExpected()
    {
        // An AvOnly capture still counts as "captured" for MissingFromExpected;
        // only entries totally absent from BOTH sides are missing.
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        TouchPng(av, "Avalonia_AvOrphanView_FE8U.png");
        TouchPng(wf, "WinForms_WfOrphanView_FE8U.png");

        var expected = new[] { "AvOrphanView", "WfOrphanView", "ActuallyMissingView" };
        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U", expected);

        Assert.Single(report.MissingFromExpected);
        Assert.Equal("ActuallyMissingView", report.MissingFromExpected[0]);
    }

    // =====================================================================
    // Empty / missing directory tolerance
    // =====================================================================

    [Fact]
    public void BuildGallery_EmptyDirsProduceEmptyReport()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");
        // Both directories exist but contain no PNGs.
        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U");
        Assert.Empty(report.Pairs);
        Assert.Empty(report.AvOnly);
        Assert.Empty(report.WfOnly);
    }

    [Fact]
    public void BuildGallery_MissingWfDirToleratedForAvOnlyHosts()
    {
        // Non-Windows hosts skip the WinForms runner; the gallery builder must
        // not crash on a missing wf/ directory.
        string av = MakeDir("av");
        TouchPng(av, "Avalonia_X_FE8U.png");
        string nonexistentWf = Path.Combine(_tempRoot, "wf-does-not-exist");

        var report = GalleryBuilder.BuildGallery(nonexistentWf, av, "FE8U");

        Assert.Empty(report.Pairs);
        Assert.Empty(report.WfOnly);
        Assert.Single(report.AvOnly);
    }

    [Fact]
    public void BuildGallery_MissingBothDirsProducesEmptyReport()
    {
        string nonexistentWf = Path.Combine(_tempRoot, "wf-missing");
        string nonexistentAv = Path.Combine(_tempRoot, "av-missing");
        var report = GalleryBuilder.BuildGallery(nonexistentWf, nonexistentAv, "FE8U");
        Assert.Empty(report.Pairs);
        Assert.Empty(report.AvOnly);
        Assert.Empty(report.WfOnly);
    }

    // =====================================================================
    // FormatIndexMarkdown
    // =====================================================================

    [Fact]
    public void FormatIndexMarkdown_OutputsHeaderAndSummary()
    {
        var report = new GalleryReport(
            "FE8U",
            Pairs: Array.Empty<GalleryEntry>(),
            AvOnly: Array.Empty<string>(),
            WfOnly: Array.Empty<string>(),
            MissingFromExpected: Array.Empty<string>());

        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");

        Assert.Contains("# Avalonia vs WinForms — Side-by-side Screenshot Gallery", md);
        Assert.Contains("ROM tag: **FE8U**", md);
        Assert.Contains("## Summary", md);
        Assert.Contains("## Side-by-side gallery (paired editors)", md);
        Assert.Contains("## Avalonia-only captures", md);
        Assert.Contains("## WinForms-only captures", md);
        Assert.Contains("## Expected editors not captured", md);
    }

    [Fact]
    public void FormatIndexMarkdown_RendersSideBySideTableWithImageLinks()
    {
        var report = new GalleryReport(
            "FE8U",
            Pairs: new[]
            {
                new GalleryEntry(
                    "UnitEditorView",
                    Path.Combine("anything", "WinForms_UnitEditorView_FE8U.png"),
                    Path.Combine("anything", "Avalonia_UnitEditorView_FE8U.png")),
            },
            AvOnly: Array.Empty<string>(),
            WfOnly: Array.Empty<string>(),
            MissingFromExpected: Array.Empty<string>());

        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");

        Assert.Contains("| Editor | WinForms | Avalonia |", md);
        Assert.Contains("| `UnitEditorView` |", md);
        Assert.Contains("![WF](wf/WinForms_UnitEditorView_FE8U.png)", md);
        Assert.Contains("![AV](av/Avalonia_UnitEditorView_FE8U.png)", md);
    }

    [Fact]
    public void FormatIndexMarkdown_UsesLfNewlinesOnly()
    {
        // Phase 0/1/2 reports use LF only (Copilot enforced during #375 re-review).
        // Phase 3 must keep that policy.
        var report = new GalleryReport(
            "FE8U",
            Pairs: new[]
            {
                new GalleryEntry(
                    "X",
                    Path.Combine("a", "WinForms_X_FE8U.png"),
                    Path.Combine("a", "Avalonia_X_FE8U.png")),
            },
            AvOnly: new[] { "AvOnlyView" },
            WfOnly: new[] { "WfOnlyView" },
            MissingFromExpected: new[] { "MissingView" });

        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");

        Assert.DoesNotContain("\r\n", md);
        Assert.DoesNotContain("\r", md);
    }

    [Fact]
    public void FormatIndexMarkdown_RendersAvOnlyAndWfOnlyAndMissingBullets()
    {
        var report = new GalleryReport(
            "FE8U",
            Pairs: Array.Empty<GalleryEntry>(),
            AvOnly: new[] { "OnlyAv1View", "OnlyAv2View" },
            WfOnly: new[] { "OnlyWf1View" },
            MissingFromExpected: new[] { "MissingAView" });

        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");

        Assert.Contains("`OnlyAv1View`", md);
        Assert.Contains("`OnlyAv2View`", md);
        Assert.Contains("`OnlyWf1View`", md);
        Assert.Contains("`MissingAView`", md);
        // No "(none)" placeholder when each section has content.
        Assert.DoesNotContain("_(none)_", md);
    }

    [Fact]
    public void FormatIndexMarkdown_EmptyPairsSectionShowsExplanatoryNote()
    {
        var report = new GalleryReport(
            "FE8U",
            Pairs: Array.Empty<GalleryEntry>(),
            AvOnly: Array.Empty<string>(),
            WfOnly: Array.Empty<string>(),
            MissingFromExpected: Array.Empty<string>());

        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");
        Assert.Contains("_No paired editors captured.", md);
        // Every empty section gets the "(none)" placeholder.
        Assert.Contains("_(none)_", md);
    }

    [Fact]
    public void FormatIndexMarkdown_UrlEscapesSpacesInPath()
    {
        // Defensive: someone drops a directory name with a space into the
        // wf/av paths. The image link must URL-escape the space.
        var report = new GalleryReport(
            "FE8U",
            Pairs: new[]
            {
                new GalleryEntry(
                    "X",
                    Path.Combine("with space", "WinForms_X_FE8U.png"),
                    Path.Combine("with space", "Avalonia_X_FE8U.png")),
            },
            AvOnly: Array.Empty<string>(),
            WfOnly: Array.Empty<string>(),
            MissingFromExpected: Array.Empty<string>());

        // Pass a relative dir with a space in it; the helper normalises to URL.
        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf dir", "av dir");
        Assert.Contains("wf%20dir/WinForms_X_FE8U.png", md);
        Assert.Contains("av%20dir/Avalonia_X_FE8U.png", md);
    }

    // =====================================================================
    // LoadExpectedEditorsFromFile / LoadExpectedEditorsFromDoc
    // =====================================================================

    [Fact]
    public void LoadExpectedEditorsFromFile_ParsesFixtureTable()
    {
        string fixturePath = Path.Combine(_tempRoot, "fixture.md");
        File.WriteAllText(fixturePath,
            "# Coverage tracker fixture\n\n" +
            "| # | View | E2E Status | Data Verified | Aligned |\n" +
            "|---|------|-----------|---------------|---------|\n" +
            "| 1 | UnitEditorView | E2E COVERED | YES | ALIGNED |\n" +
            "| 2 | ItemEditorView | E2E COVERED | YES | ALIGNED |\n" +
            "| 3 | ClassFE6View | E2E COVERED | - | ALIGNED |\n" +
            "\n" +
            "## More forms\n\n" +
            "| # | View | E2E Status |\n" +
            "|---|------|-----------|\n" +
            "| 4 | MapSettingView | E2E COVERED |\n");

        var names = GalleryBuilder.LoadExpectedEditorsFromFile(fixturePath);

        Assert.Equal(4, names.Count);
        // Sorted Ordinal.
        Assert.Equal(new[] { "ClassFE6View", "ItemEditorView", "MapSettingView", "UnitEditorView" }, names);
    }

    [Fact]
    public void LoadExpectedEditorsFromFile_MissingFileReturnsEmpty()
    {
        // Non-failing fallback: missing coverage doc must not break the gallery.
        var names = GalleryBuilder.LoadExpectedEditorsFromFile(
            Path.Combine(_tempRoot, "does-not-exist.md"));
        Assert.Empty(names);
    }

    [Fact]
    public void LoadExpectedEditorsFromFile_IgnoresNonIdentifierAndNonViewRows()
    {
        // Rows where the View cell is empty, has markdown formatting only, or
        // doesn't end with "View" should be skipped silently.
        string fixturePath = Path.Combine(_tempRoot, "noise.md");
        File.WriteAllText(fixturePath,
            "| # | View | E2E Status |\n" +
            "|---|------|-----------|\n" +
            "| 1 | UnitEditorView | OK |\n" +
            "| 2 | — | SKIP |\n" +
            "| 3 |  | SKIP |\n" +
            "| 4 | NotAViewName | SKIP |\n" +
            "| 5 | `BackticksAroundEditorView` | OK |\n");

        var names = GalleryBuilder.LoadExpectedEditorsFromFile(fixturePath);

        Assert.Contains("UnitEditorView", names);
        Assert.Contains("BackticksAroundEditorView", names);
        Assert.DoesNotContain("—", names);
        Assert.DoesNotContain("NotAViewName", names);
        Assert.DoesNotContain("", names);
    }

    [Fact]
    public void LoadExpectedEditorsFromDoc_LiveDocLoads()
    {
        // Repo-driven integration test: the actual docs/avalonia-gui-forms.md
        // should parse to at least a few dozen view names. Catches drift if
        // someone changes the doc structure.
        string repoRoot = FindRepoRoot();
        Assert.False(string.IsNullOrEmpty(repoRoot), "FindRepoRoot must return a non-empty path");
        var names = GalleryBuilder.LoadExpectedEditorsFromDoc(repoRoot);
        // Doc memory lists 325 forms; we accept anything > 50 to leave headroom
        // for the parser to evolve or rows to be reformatted.
        Assert.True(names.Count > 50, $"expected > 50 editor names parsed from live doc, got {names.Count}");
        Assert.Contains("UnitEditorView", names);
    }

    // =====================================================================
    // BuildGallery argument validation
    // =====================================================================

    [Fact]
    public void BuildGallery_ThrowsOnEmptyRomTag()
    {
        Assert.Throws<ArgumentException>(() => GalleryBuilder.BuildGallery("", "", ""));
    }

    // =====================================================================
    // End-to-end capture-summary scenario covering all four status states
    // (complete, av-only, wf-only, empty) — Copilot v2 review concern #5
    // requested an integration case proving paired/WF-only/AV-only/missing
    // all surface simultaneously.
    // =====================================================================

    [Fact]
    public void BuildGallery_EndToEnd_AllFourCategoriesPopulate()
    {
        string wf = MakeDir("wf");
        string av = MakeDir("av");

        // Symmetric: both sides capture
        TouchPng(wf, "WinForms_PairedView_FE8U.png");
        TouchPng(av, "Avalonia_PairedView_FE8U.png");
        TouchPng(wf, "WinForms_Paired2View_FE8U.png");
        TouchPng(av, "Avalonia_Paired2View_FE8U.png");
        // Asymmetric: AV captured, WF did not
        TouchPng(av, "Avalonia_AvOrphanView_FE8U.png");
        // Asymmetric: WF captured, AV did not
        TouchPng(wf, "WinForms_WfOrphanView_FE8U.png");

        var expected = new[]
        {
            "PairedView",
            "Paired2View",
            "AvOrphanView",
            "WfOrphanView",
            "TotallyMissingView",
        };

        var report = GalleryBuilder.BuildGallery(wf, av, "FE8U", expected);

        Assert.Equal(2, report.Pairs.Count);
        Assert.Single(report.AvOnly);
        Assert.Equal("AvOrphanView", report.AvOnly[0]);
        Assert.Single(report.WfOnly);
        Assert.Equal("WfOrphanView", report.WfOnly[0]);
        Assert.Single(report.MissingFromExpected);
        Assert.Equal("TotallyMissingView", report.MissingFromExpected[0]);

        // Format the markdown and verify all four categories surface.
        string md = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");
        Assert.Contains("| `Paired2View` |", md);
        Assert.Contains("| `PairedView` |", md);
        Assert.Contains("`AvOrphanView`", md);
        Assert.Contains("`WfOrphanView`", md);
        Assert.Contains("`TotallyMissingView`", md);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Walk up from the test-assembly directory until we find FEBuilderGBA.sln.
    /// Mirrors the App's FindRepoRoot but kept private to the tests so they
    /// don't pull in the Avalonia App entry point.
    /// </summary>
    static string FindRepoRoot()
    {
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (DirectoryInfo? d = new DirectoryInfo(start); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "FEBuilderGBA.sln")))
                return d.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
