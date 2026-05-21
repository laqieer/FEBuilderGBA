// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 tests — ControlDensityScanner counting + report formatting. (#374)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="ControlDensityScanner"/>. Where possible we use
/// in-memory source and XML strings so the tests do not depend on the live
/// repo. A single end-to-end test exercises file I/O against a temp dir.
/// </summary>
public class ControlDensityScannerTests : IDisposable
{
    readonly string _tempRoot;

    public ControlDensityScannerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "fbgba-density-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    // -------------------------------------------------------------
    // WinForms counting (Roslyn) on in-memory source.
    // -------------------------------------------------------------

    [Fact]
    public void CountWfControls_BasicAllowList()
    {
        // Two Buttons + one TextBox = 3.
        string src = @"
namespace X {
    class F {
        void Init() {
            var b1 = new System.Windows.Forms.Button();
            var b2 = new System.Windows.Forms.Button();
            var t = new System.Windows.Forms.TextBox();
        }
    }
}";
        int n = ControlDensityScanner.CountObjectCreationsInSource(src);
        Assert.Equal(3, n);
    }

    [Fact]
    public void CountWfControls_IgnoresUnknownTypes()
    {
        // FlowLayoutPanel + Splitter aren't in our allow-list; only the Button counts.
        string src = @"
class F {
    void Init() {
        var p = new System.Windows.Forms.FlowLayoutPanel();
        var s = new System.Windows.Forms.Splitter();
        var b = new System.Windows.Forms.Button();
    }
}";
        int n = ControlDensityScanner.CountObjectCreationsInSource(src);
        Assert.Equal(1, n);
    }

    [Fact]
    public void CountWfControls_AcceptsUnqualifiedAndAliasedNames()
    {
        // Designer code often uses different qualification styles. All three
        // identifiers below resolve to "Button" for our trailing-identifier match.
        string src = @"
using SWF = System.Windows.Forms;
using System.Windows.Forms;
class F {
    void Init() {
        var b1 = new System.Windows.Forms.Button();
        var b2 = new SWF.Button();
        var b3 = new Button();
    }
}";
        int n = ControlDensityScanner.CountObjectCreationsInSource(src);
        Assert.Equal(3, n);
    }

    [Fact]
    public void CountWfControls_AllTwelveAllowedTypes()
    {
        // The full allow-list. One instantiation of each must yield exactly 12.
        string src = @"
class F {
    void Init() {
        var a = new System.Windows.Forms.Button();
        var b = new System.Windows.Forms.TextBox();
        var c = new System.Windows.Forms.NumericUpDown();
        var d = new System.Windows.Forms.ComboBox();
        var e = new System.Windows.Forms.CheckBox();
        var f = new System.Windows.Forms.RadioButton();
        var g = new System.Windows.Forms.Label();
        var h = new System.Windows.Forms.DataGridView();
        var i = new System.Windows.Forms.GroupBox();
        var j = new System.Windows.Forms.TabPage();
        var k = new System.Windows.Forms.ListBox();
        var l = new System.Windows.Forms.PictureBox();
    }
}";
        int n = ControlDensityScanner.CountObjectCreationsInSource(src);
        Assert.Equal(12, n);
    }

    // -------------------------------------------------------------
    // Avalonia counting (XML) on in-memory documents.
    // -------------------------------------------------------------

    [Fact]
    public void CountAvControls_BasicAllowList()
    {
        // Two Buttons + one TextBox = 3.
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <StackPanel>
    <Button />
    <Button />
    <TextBox />
  </StackPanel>
</UserControl>";
        int n = ControlDensityScanner.CountAvControlsInDocument(XDocument.Parse(xml));
        Assert.Equal(3, n);
    }

    [Fact]
    public void CountAvControls_SkipsDataTemplates()
    {
        // Buttons inside <DataTemplate> are template content, not realised
        // controls — exclude them. The realised Button outside the template
        // is the only one counted.
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <ListBox>
    <ListBox.ItemTemplate>
      <DataTemplate>
        <StackPanel>
          <Button />
          <Button />
        </StackPanel>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
  <Button />
</UserControl>";
        int n = ControlDensityScanner.CountAvControlsInDocument(XDocument.Parse(xml));
        // ListBox + Button (outside the template) = 2
        Assert.Equal(2, n);
    }

    [Fact]
    public void CountAvControls_SkipsStylesAndDesignDataContext()
    {
        // <Style> and <Design.DataContext> are non-realised — exclude.
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <UserControl.Styles>
    <Style>
      <Button />
    </Style>
  </UserControl.Styles>
  <Design.DataContext>
    <TextBox />
  </Design.DataContext>
  <Button />
</UserControl>";
        int n = ControlDensityScanner.CountAvControlsInDocument(XDocument.Parse(xml));
        // Only the trailing Button counts.
        Assert.Equal(1, n);
    }

    // -------------------------------------------------------------
    // Verdict thresholds.
    // -------------------------------------------------------------

    // Use exact integer ratios so the delta we observe matches the test name
    // without floating-point rounding noise. WF=1000 makes each ±1 control a
    // 0.1 % delta, so we can express threshold-adjacent percentages precisely.
    //
    // Mapping of (WF, AV) → DeltaPct:
    //   (1000, 1500) →  50.0 % → HIGH (≥50)
    //   (1000, 1499) →  49.9 % → MEDIUM (just below)
    //   (1000, 1250) →  25.0 % → MEDIUM (≥25)
    //   (1000, 1249) →  24.9 % → LOW (just below)
    //   (1000, 1000) →   0.0 % → LOW
    //   (1000, 500)  → -50.0 % → HIGH (negative side)
    //   (1000, 700)  → -30.0 % → MEDIUM (negative side)
    [Theory]
    [InlineData(1000, 1500, Verdict.High)]
    [InlineData(1000, 1499, Verdict.Medium)]
    [InlineData(1000, 1250, Verdict.Medium)]
    [InlineData(1000, 1249, Verdict.Low)]
    [InlineData(1000, 1000, Verdict.Low)]
    [InlineData(1000, 500, Verdict.High)]
    [InlineData(1000, 700, Verdict.Medium)]
    public void Scan_VerdictThresholds(int wfControls, int avControls, Verdict expected)
    {
        // Drive Scan() against a synthesized minimal repo so the scanner's own
        // delta math + classifier is exercised end-to-end (not just unit-tested
        // in isolation). The repo has exactly one paired editor.
        string tempRoot = CreateMinimalRepo(_tempRoot, wfControls, avControls, "X");

        var pairs = new List<EditorPair>
        {
            new EditorPair(
                WfFormName: "XForm",
                WfPath: Path.Combine(tempRoot, "FEBuilderGBA", "XForm.cs"),
                AvViewName: "XView",
                AvPath: Path.Combine(tempRoot, "FEBuilderGBA.Avalonia", "Views", "XView.axaml"),
                Match: MatchMethod.Heuristic,
                Confidence: Confidence.Medium),
        };
        var rows = ControlDensityScanner.Scan(pairs, tempRoot);
        Assert.Single(rows);
        Assert.Equal(expected, rows[0].Verdict);
    }

    // -------------------------------------------------------------
    // End-to-end Scan over a tiny synthetic repo.
    // -------------------------------------------------------------

    [Fact]
    public void Scan_EndToEnd_OnSyntheticRepo()
    {
        // Layout: WF "BigForm" (5 controls) + AV "BigView" (2 controls).
        string repoRoot = CreateMinimalRepo(_tempRoot, wfControls: 5, avControls: 2, baseName: "Big");

        var pairs = new List<EditorPair>
        {
            new EditorPair(
                WfFormName: "BigForm",
                WfPath: Path.Combine(repoRoot, "FEBuilderGBA", "BigForm.cs"),
                AvViewName: "BigView",
                AvPath: Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "BigView.axaml"),
                Match: MatchMethod.Heuristic,
                Confidence: Confidence.Medium),
        };
        var rows = ControlDensityScanner.Scan(pairs, repoRoot);
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(5, row.WfControlCount);
        Assert.Equal(2, row.AvControlCount);
        // (2 - 5) / 5 * 100 = -60% → HIGH
        Assert.Equal(-60.0, row.DeltaPct, precision: 5);
        Assert.Equal(Verdict.High, row.Verdict);
    }

    [Fact]
    public void Scan_DropsRowsWithZeroOnBothSides()
    {
        // A pair whose WF Designer and AV view both have no recognised controls
        // produces no row — it would skew the average and tells us nothing.
        string repoRoot = CreateMinimalRepo(_tempRoot, wfControls: 0, avControls: 0, baseName: "Empty");

        var pairs = new List<EditorPair>
        {
            new EditorPair(
                WfFormName: "EmptyForm",
                WfPath: Path.Combine(repoRoot, "FEBuilderGBA", "EmptyForm.cs"),
                AvViewName: "EmptyView",
                AvPath: Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "EmptyView.axaml"),
                Match: MatchMethod.Heuristic,
                Confidence: Confidence.Medium),
        };
        var rows = ControlDensityScanner.Scan(pairs, repoRoot);
        Assert.Empty(rows);
    }

    [Fact]
    public void Scan_WfZeroAvPositive_GivesInfinity()
    {
        // WF=0, AV>0 → +∞%. Mathematically undefined, but we encode the case
        // so the report can surface it.
        string repoRoot = CreateMinimalRepo(_tempRoot, wfControls: 0, avControls: 5, baseName: "WfNone");

        var pairs = new List<EditorPair>
        {
            new EditorPair(
                WfFormName: "WfNoneForm",
                WfPath: Path.Combine(repoRoot, "FEBuilderGBA", "WfNoneForm.cs"),
                AvViewName: "WfNoneView",
                AvPath: Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "WfNoneView.axaml"),
                Match: MatchMethod.Heuristic,
                Confidence: Confidence.Medium),
        };
        var rows = ControlDensityScanner.Scan(pairs, repoRoot);
        Assert.Single(rows);
        Assert.True(double.IsPositiveInfinity(rows[0].DeltaPct));
        Assert.Equal(Verdict.High, rows[0].Verdict);
    }

    [Fact]
    public void FormatReport_EmitsAllKnownSections()
    {
        // A single synthetic row should produce a markdown body with the
        // summary, ranked-deltas, top-20 HIGH, and orphans sections.
        var pairs = new List<EditorPair>
        {
            new EditorPair(
                WfFormName: "FooForm",
                WfPath: "/tmp/FooForm.cs",
                AvViewName: "FooView",
                AvPath: "/tmp/FooView.axaml",
                Match: MatchMethod.Heuristic,
                Confidence: Confidence.Medium),
        };
        var rows = new List<DensityRow>
        {
            new DensityRow(pairs[0], WfControlCount: 100, AvControlCount: 30, DeltaPct: -70.0, Verdict: Verdict.High),
        };
        string md = ControlDensityScanner.FormatReport(rows);
        Assert.Contains("# Avalonia vs WinForms — Control Density Sweep", md);
        Assert.Contains("## Summary", md);
        Assert.Contains("## Ranked Density Deltas", md);
        Assert.Contains("## Top-20 HIGH Gaps", md);
        Assert.Contains("## Unmatched WinForms Counterparts", md);
        Assert.Contains("## Unmatched Avalonia Counterparts", md);
        Assert.Contains("## Unpaired Orphans", md);
        Assert.Contains("`FooForm`", md);
        Assert.Contains("-70.0%", md);
    }

    [Fact]
    public void FormatReport_AvZeroRows_GoToUnmatchedAvSection_NotRankedTable()
    {
        // A row with AV count == 0 (e.g. AXAML parse failure) must NOT appear in
        // the ranked-deltas table (where it would look like a -100 % gap) and
        // must NOT appear in any Top-20 triage subsection. It belongs in the
        // dedicated "Unmatched Avalonia Counterparts" table.
        var rankedPair = new EditorPair(
            WfFormName: "RankedForm",
            WfPath: "/tmp/RankedForm.cs",
            AvViewName: "RankedView",
            AvPath: "/tmp/RankedView.axaml",
            Match: MatchMethod.ListParityHelper,
            Confidence: Confidence.High);
        var avMissingPair = new EditorPair(
            WfFormName: "AvMissingForm",
            WfPath: "/tmp/AvMissingForm.cs",
            AvViewName: "AvMissingView",
            AvPath: "/tmp/AvMissingView.axaml", // path present, but count 0 simulates parse failure
            Match: MatchMethod.ListParityHelper,
            Confidence: Confidence.High);

        var rows = new List<DensityRow>
        {
            new DensityRow(rankedPair, WfControlCount: 100, AvControlCount: 50, DeltaPct: -50.0, Verdict: Verdict.High),
            new DensityRow(avMissingPair, WfControlCount: 80, AvControlCount: 0, DeltaPct: -100.0, Verdict: Verdict.High),
        };
        string md = ControlDensityScanner.FormatReport(rows);

        // Locate the ranked-deltas section and the unmatched-AV section.
        int ranked = md.IndexOf("## Ranked Density Deltas", StringComparison.Ordinal);
        int unmatchedAv = md.IndexOf("## Unmatched Avalonia Counterparts", StringComparison.Ordinal);
        Assert.True(ranked > 0 && unmatchedAv > ranked);

        string rankedSection = md.Substring(ranked, unmatchedAv - ranked);
        Assert.Contains("`RankedForm`", rankedSection);
        Assert.DoesNotContain("`AvMissingForm`", rankedSection);

        string unmatchedAvSection = md.Substring(unmatchedAv);
        Assert.Contains("`AvMissingForm`", unmatchedAvSection);

        // Top-20 triage subsections must not pick up the AV==0 row either.
        int triage = md.IndexOf("## Top-20 HIGH Gaps", StringComparison.Ordinal);
        int unmatchedWf = md.IndexOf("## Unmatched WinForms Counterparts", StringComparison.Ordinal);
        string triageSection = md.Substring(triage, unmatchedWf > triage ? unmatchedWf - triage : md.Length - triage);
        // No `### AvMissingForm` heading should exist.
        Assert.DoesNotContain("### AvMissingForm", triageSection);
        // The normal ranked row is the only HIGH row that qualifies.
        Assert.Contains("### RankedForm", triageSection);
    }

    /// <summary>
    /// Create a minimal repo under <paramref name="root"/> that PairMatcher and
    /// the scanner can read: an FEBuilderGBA/{baseName}Form.cs designer-style
    /// file with the requested control count, plus an FEBuilderGBA.Avalonia/Views/
    /// {baseName}View.axaml with the requested control count.
    /// </summary>
    static string CreateMinimalRepo(string root, int wfControls, int avControls, string baseName)
    {
        string wfDir = Path.Combine(root, "FEBuilderGBA");
        string avDir = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views");
        Directory.CreateDirectory(wfDir);
        Directory.CreateDirectory(avDir);

        // WF file: one Button per requested control.
        var wfSrc = new System.Text.StringBuilder();
        wfSrc.AppendLine("namespace FEBuilderGBA { partial class " + baseName + "Form { void Init() {");
        for (int i = 0; i < wfControls; i++)
            wfSrc.AppendLine($"    var c{i} = new System.Windows.Forms.Button();");
        wfSrc.AppendLine("}}}");
        File.WriteAllText(Path.Combine(wfDir, baseName + "Form.cs"), wfSrc.ToString());

        // AV file: one <Button /> per requested control.
        var avSrc = new System.Text.StringBuilder();
        avSrc.AppendLine("<UserControl xmlns='https://github.com/avaloniaui'>");
        avSrc.AppendLine("  <StackPanel>");
        for (int i = 0; i < avControls; i++)
            avSrc.AppendLine("    <Button />");
        avSrc.AppendLine("  </StackPanel>");
        avSrc.AppendLine("</UserControl>");
        File.WriteAllText(Path.Combine(avDir, baseName + "View.axaml"), avSrc.ToString());

        return root;
    }
}
