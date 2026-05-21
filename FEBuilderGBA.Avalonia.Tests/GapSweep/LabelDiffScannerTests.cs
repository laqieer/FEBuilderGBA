// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 2 tests — LabelDiffScanner label extraction + diff + formatting. (#374)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="LabelDiffScanner"/>. We mostly drive the scanner via
/// in-memory source / XML strings so the tests are hermetic. A handful of
/// repo-driven tests confirm the live-pair extraction picks up labels from
/// real Designer.cs / .axaml files.
/// </summary>
public class LabelDiffScannerTests
{
    // =====================================================================
    // Normalize — strips trailing colons, collapses whitespace, lowercases,
    // and removes mnemonic markers (& for WF, _ for AV).
    // =====================================================================

    [Fact]
    public void Normalize_TrimsLeadingAndTrailingWhitespace()
    {
        Assert.Equal("name", LabelDiffScanner.Normalize("  Name  "));
    }

    [Fact]
    public void Normalize_StripsTrailingColon()
    {
        Assert.Equal("name", LabelDiffScanner.Normalize("Name:"));
    }

    [Fact]
    public void Normalize_StripsMultipleTrailingColonsAndWhitespace()
    {
        // Pathological but legal: "  Name :  : " should still collapse to "name".
        Assert.Equal("name", LabelDiffScanner.Normalize("  Name :  : "));
    }

    [Fact]
    public void Normalize_CollapsesInternalWhitespace()
    {
        Assert.Equal("first name", LabelDiffScanner.Normalize("First   Name"));
    }

    [Fact]
    public void Normalize_Lowercases()
    {
        Assert.Equal("save", LabelDiffScanner.Normalize("SAVE"));
    }

    [Fact]
    public void Normalize_StripsWfMnemonicAmpersand()
    {
        // WinForms uses & for keyboard mnemonics; Avalonia uses _. Both must
        // normalise to the same key so cross-platform-equivalent labels match.
        Assert.Equal("save", LabelDiffScanner.Normalize("&Save"));
    }

    [Fact]
    public void Normalize_StripsAvMnemonicUnderscore()
    {
        Assert.Equal("save", LabelDiffScanner.Normalize("_Save"));
    }

    [Fact]
    public void Normalize_MnemonicAndColonTogether()
    {
        // WF designer literal: "&Save:" → "save"
        Assert.Equal("save", LabelDiffScanner.Normalize("&Save:"));
    }

    [Fact]
    public void Normalize_AllVariantsCollideToSameKey()
    {
        // The core property: every reasonable cross-platform spelling of the
        // SAME label collides to the same set key.
        string baseline = LabelDiffScanner.Normalize("Save");
        Assert.Equal(baseline, LabelDiffScanner.Normalize("save"));
        Assert.Equal(baseline, LabelDiffScanner.Normalize("Save:"));
        Assert.Equal(baseline, LabelDiffScanner.Normalize("&Save"));
        Assert.Equal(baseline, LabelDiffScanner.Normalize("_Save"));
        Assert.Equal(baseline, LabelDiffScanner.Normalize("  Save  "));
        Assert.Equal(baseline, LabelDiffScanner.Normalize("SAVE"));
    }

    [Fact]
    public void Normalize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LabelDiffScanner.Normalize(""));
        Assert.Equal(string.Empty, LabelDiffScanner.Normalize(null!));
    }

    // =====================================================================
    // ExtractWfLabelsFromSource — pulls `.Text = "..."` from designer code
    // when the LHS is a recognised label-host control.
    // =====================================================================

    [Fact]
    public void ExtractWfLabels_CountsLabelTextAssignments()
    {
        string src = @"
namespace X {
    partial class F {
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        void Init() {
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label1.Text = ""Hello"";
            this.label2.Text = ""World"";
            this.label3.Text = ""!"";
        }
    }
}";
        var labels = LabelDiffScanner.ExtractWfLabelsFromSource(src);
        Assert.Equal(3, labels.Count);
        Assert.Contains("Hello", labels);
        Assert.Contains("World", labels);
        Assert.Contains("!", labels);
    }

    [Fact]
    public void ExtractWfLabels_PropertyInitializerSyntax()
    {
        // Hand-coded forms sometimes use `new Label { Text = "X" }` instead of
        // the assignment-statement designer style.
        string src = @"
namespace X {
    class F {
        void Init() {
            var hello = new System.Windows.Forms.Label { Text = ""Hello"" };
            var world = new System.Windows.Forms.Button { Text = ""World"" };
        }
    }
}";
        var labels = LabelDiffScanner.ExtractWfLabelsFromSource(src);
        Assert.Equal(2, labels.Count);
        Assert.Contains("Hello", labels);
        Assert.Contains("World", labels);
    }

    [Fact]
    public void ExtractWfLabels_SkipsNonLabelHostTypes()
    {
        // TextBox.Text and ComboBox.Text are DATA, not labels — must be excluded.
        // PictureBox doesn't have a meaningful Text but a designer might still
        // assign to it; we ignore that case too.
        string src = @"
namespace X {
    partial class F {
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label1;
        void Init() {
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox1.Text = ""DATA-1"";
            this.comboBox1.Text = ""DATA-2"";
            this.pictureBox1.Text = ""DATA-3"";
            this.label1.Text = ""REAL LABEL"";
        }
    }
}";
        var labels = LabelDiffScanner.ExtractWfLabelsFromSource(src);
        Assert.Single(labels);
        Assert.Equal("REAL LABEL", labels[0]);
    }

    [Fact]
    public void ExtractWfLabels_GroupBoxAndButtonAndCheckBoxAndRadioButtonAndTabPage()
    {
        // All five non-Label host types that also count.
        string src = @"
namespace X {
    partial class F {
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.TabPage tabPage1;
        void Init() {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.button1 = new System.Windows.Forms.Button();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.radioButton1 = new System.Windows.Forms.RadioButton();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.groupBox1.Text = ""G"";
            this.button1.Text = ""B"";
            this.checkBox1.Text = ""C"";
            this.radioButton1.Text = ""R"";
            this.tabPage1.Text = ""T"";
        }
    }
}";
        var labels = LabelDiffScanner.ExtractWfLabelsFromSource(src);
        Assert.Equal(5, labels.Count);
        Assert.Equal(new[] { "G", "B", "C", "R", "T" }, labels);
    }

    [Fact]
    public void ExtractWfLabels_EmptyStringSkipped()
    {
        // Empty literals carry no signal — must be skipped (otherwise the
        // diff would treat "" as a label and produce noise).
        string src = @"
namespace X {
    partial class F {
        private System.Windows.Forms.Label label1;
        void Init() {
            this.label1 = new System.Windows.Forms.Label();
            this.label1.Text = """";
        }
    }
}";
        var labels = LabelDiffScanner.ExtractWfLabelsFromSource(src);
        Assert.Empty(labels);
    }

    // =====================================================================
    // ExtractAvLabelsFromDocument — pulls Text/Content/Header/ToolTip/
    // Watermark literals from .axaml.
    // =====================================================================

    [Fact]
    public void ExtractAvLabels_ButtonContentAndTextBoxWatermark()
    {
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <StackPanel>
    <Button Content='Save' />
    <TextBox Watermark='search' />
  </StackPanel>
</UserControl>";
        var labels = LabelDiffScanner.ExtractAvLabelsFromDocument(XDocument.Parse(xml));
        Assert.Equal(2, labels.Count);
        Assert.Contains("Save", labels);
        Assert.Contains("search", labels);
    }

    [Fact]
    public void ExtractAvLabels_SkipsBindings()
    {
        // Markup-extension values (any value starting with `{`) must be
        // excluded — they're data references, not label text.
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <StackPanel>
    <TextBlock Text='{Binding Name}' />
    <Button Content='{StaticResource OkText}' />
    <Button Content='Real Text' />
  </StackPanel>
</UserControl>";
        var labels = LabelDiffScanner.ExtractAvLabelsFromDocument(XDocument.Parse(xml));
        Assert.Single(labels);
        Assert.Equal("Real Text", labels[0]);
    }

    [Fact]
    public void ExtractAvLabels_SkipsTemplateContent()
    {
        // Anything inside <DataTemplate> or <Style> is template content,
        // not realised label text. Must be excluded.
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <UserControl.Styles>
    <Style>
      <Setter Property='Content' Value='styled' />
    </Style>
  </UserControl.Styles>
  <ListBox>
    <ListBox.ItemTemplate>
      <DataTemplate>
        <StackPanel>
          <Button Content='Per-item Save' />
        </StackPanel>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
  <Button Content='Outer Save' />
</UserControl>";
        var labels = LabelDiffScanner.ExtractAvLabelsFromDocument(XDocument.Parse(xml));
        Assert.Single(labels);
        Assert.Equal("Outer Save", labels[0]);
    }

    [Fact]
    public void ExtractAvLabels_ToolTipLiteralCountedToolTipBindingSkipped()
    {
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <StackPanel>
    <Button ToolTip='Click to save' />
    <Button ToolTip='{Binding TipText}' />
  </StackPanel>
</UserControl>";
        var labels = LabelDiffScanner.ExtractAvLabelsFromDocument(XDocument.Parse(xml));
        Assert.Single(labels);
        Assert.Equal("Click to save", labels[0]);
    }

    [Fact]
    public void ExtractAvLabels_HeaderAndExpanderHeader()
    {
        string xml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <TabControl>
    <TabItem Header='Stats' />
    <Expander Header='Advanced'>
      <TextBlock Text='Body text' />
    </Expander>
  </TabControl>
</UserControl>";
        var labels = LabelDiffScanner.ExtractAvLabelsFromDocument(XDocument.Parse(xml));
        Assert.Equal(3, labels.Count);
        Assert.Contains("Stats", labels);
        Assert.Contains("Advanced", labels);
        Assert.Contains("Body text", labels);
    }

    // =====================================================================
    // ComputeDiff — the set-difference algorithm, case- and mnemonic-insensitive.
    // =====================================================================

    [Fact]
    public void ComputeDiff_BasicWfOnlyAndAvOnly()
    {
        var pair = new EditorPair("TestForm", null, "TestView", null,
            MatchMethod.ListParityHelper, Confidence.High);
        var wfLabels = new[] { "Name:", "Class:", "HP" };
        var avLabels = new[] { "Name", "Speed" };
        var row = LabelDiffScanner.ComputeDiff(pair, wfLabels, avLabels);
        // "Name:" and "Name" normalise the same → Common.
        // "Class:" only on WF → WfOnly.
        // "HP" only on WF → WfOnly.
        // "Speed" only on AV → AvOnly.
        Assert.Equal(2, row.WfOnlyLabels.Count);
        Assert.Contains("Class:", row.WfOnlyLabels);
        Assert.Contains("HP", row.WfOnlyLabels);
        Assert.Single(row.AvOnlyLabels);
        Assert.Equal("Speed", row.AvOnlyLabels[0]);
        Assert.Single(row.CommonLabels);
        // Common preserves the FIRST occurrence (WF side, since it's iterated first).
        Assert.Equal("Name:", row.CommonLabels[0]);
    }

    [Fact]
    public void ComputeDiff_MnemonicEquivalenceCrossPlatform()
    {
        // The regression-grade case: WF "&Save", AV "_Save" must collide.
        // The plain "Save" variant on a third platform also collides.
        var pair = new EditorPair("X", null, "Y", null,
            MatchMethod.Heuristic, Confidence.High);
        var wfLabels = new[] { "&Save", "&Cancel" };
        var avLabels = new[] { "_Save", "Save" };
        var row = LabelDiffScanner.ComputeDiff(pair, wfLabels, avLabels);
        // "&Save" / "_Save" / "Save" all normalise to "save" — one Common row.
        // "&Cancel" only on WF → one WfOnly row.
        Assert.Single(row.CommonLabels);
        Assert.Single(row.WfOnlyLabels);
        Assert.Equal("&Cancel", row.WfOnlyLabels[0]);
        Assert.Empty(row.AvOnlyLabels);
    }

    [Fact]
    public void ComputeDiff_PreservesOriginalCasing()
    {
        // The diff key is lowercased but the report must show the ORIGINAL
        // casing from the first occurrence on each side.
        var pair = new EditorPair("X", null, "Y", null,
            MatchMethod.Heuristic, Confidence.High);
        var wfLabels = new[] { "ATTACK Power" };
        var avLabels = new[] { "Defense" };
        var row = LabelDiffScanner.ComputeDiff(pair, wfLabels, avLabels);
        Assert.Equal("ATTACK Power", row.WfOnlyLabels[0]);
        Assert.Equal("Defense", row.AvOnlyLabels[0]);
    }

    [Fact]
    public void ComputeDiff_EmptyWfLabels_ProducesEmptyWfOnly()
    {
        // Edge case: WF designer has no label assignments → WfOnly is empty.
        var pair = new EditorPair("Stub", null, "Stub", null,
            MatchMethod.Heuristic, Confidence.High);
        var row = LabelDiffScanner.ComputeDiff(pair, Array.Empty<string>(), new[] { "Hello" });
        Assert.Empty(row.WfOnlyLabels);
        Assert.Single(row.AvOnlyLabels);
        Assert.Empty(row.CommonLabels);
    }

    [Fact]
    public void ComputeDiff_DuplicateLabelsAreDeduplicatedViaNormalisation()
    {
        // A designer might emit the same label twice (e.g. two GroupBox
        // headers both reading "Name:"). The Common list de-dupes via the
        // normalised key.
        var pair = new EditorPair("X", null, "Y", null,
            MatchMethod.Heuristic, Confidence.High);
        var wfLabels = new[] { "Name:", "Name:", "Class" };
        var avLabels = new[] { "Name" };
        var row = LabelDiffScanner.ComputeDiff(pair, wfLabels, avLabels);
        Assert.Single(row.CommonLabels);
        Assert.Single(row.WfOnlyLabels);
        Assert.Equal("Class", row.WfOnlyLabels[0]);
    }

    // =====================================================================
    // FormatReport — exercises the markdown structure end-to-end.
    // =====================================================================

    [Fact]
    public void FormatReport_OutputsHeaderAndSummary()
    {
        // Empty rows still produce a well-formed report skeleton.
        string report = LabelDiffScanner.FormatReport(Array.Empty<LabelDiffRow>());
        Assert.Contains("# Avalonia vs WinForms — Field Label Diff Sweep", report);
        Assert.Contains("## Summary", report);
        Assert.Contains("## Top 20 Forms by WF-only Label Count", report);
        Assert.Contains("## Per-pair WF-only Labels (gaps)", report);
    }

    [Fact]
    public void FormatReport_OrdersByWfOnlyDescending()
    {
        // Three rows with WfOnly counts 5, 1, 3 must appear in the per-pair
        // sections in the order 5 → 3 → 1.
        var rows = new List<LabelDiffRow>
        {
            new LabelDiffRow(
                new EditorPair("AaForm", null, "AaView", null, MatchMethod.Heuristic, Confidence.High),
                new[] { "x" },
                Array.Empty<string>(),
                Array.Empty<string>()),
            new LabelDiffRow(
                new EditorPair("BbForm", null, "BbView", null, MatchMethod.Heuristic, Confidence.High),
                new[] { "a", "b", "c", "d", "e" },
                Array.Empty<string>(),
                Array.Empty<string>()),
            new LabelDiffRow(
                new EditorPair("CcForm", null, "CcView", null, MatchMethod.Heuristic, Confidence.High),
                new[] { "x", "y", "z" },
                Array.Empty<string>(),
                Array.Empty<string>()),
        };
        string report = LabelDiffScanner.FormatReport(rows);

        int idxBb = report.IndexOf("### BbForm", StringComparison.Ordinal);
        int idxCc = report.IndexOf("### CcForm", StringComparison.Ordinal);
        int idxAa = report.IndexOf("### AaForm", StringComparison.Ordinal);
        Assert.True(idxBb >= 0);
        Assert.True(idxCc >= 0);
        Assert.True(idxAa >= 0);
        Assert.True(idxBb < idxCc, $"BbForm (5) must come before CcForm (3) — got {idxBb} vs {idxCc}");
        Assert.True(idxCc < idxAa, $"CcForm (3) must come before AaForm (1) — got {idxCc} vs {idxAa}");
    }

    [Fact]
    public void FormatReport_SkipsRowsWithoutGap()
    {
        // A row with WfOnly == 0 must NOT get a per-pair section (we only
        // surface forms with at least one candidate missing field).
        var rows = new[]
        {
            new LabelDiffRow(
                new EditorPair("PerfectForm", null, "PerfectView", null, MatchMethod.Heuristic, Confidence.High),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "Name", "Class" }),
            new LabelDiffRow(
                new EditorPair("GapForm", null, "GapView", null, MatchMethod.Heuristic, Confidence.High),
                new[] { "Missing1" },
                Array.Empty<string>(),
                Array.Empty<string>()),
        };
        string report = LabelDiffScanner.FormatReport(rows);
        Assert.DoesNotContain("### PerfectForm", report);
        Assert.Contains("### GapForm", report);
        Assert.Contains("`Missing1`", report);
    }

    [Fact]
    public void FormatReport_IncludesAvOnlyLabelsWhenPresent()
    {
        // AV-only labels should appear under an "AV-only" sub-heading when
        // the row has any (they're informational — usually fine).
        var row = new LabelDiffRow(
            new EditorPair("MixedForm", null, "MixedView", null, MatchMethod.Heuristic, Confidence.High),
            new[] { "MissingField" },
            new[] { "ExtraField" },
            Array.Empty<string>());
        string report = LabelDiffScanner.FormatReport(new[] { row });
        Assert.Contains("`MissingField`", report);
        Assert.Contains("`ExtraField`", report);
        Assert.Contains("AV-only labels", report);
    }

    [Fact]
    public void FormatReport_CrossLinksDensityVerdictWhenProvided()
    {
        var pair = new EditorPair("UnitForm", null, "UnitEditorView", null,
            MatchMethod.ListParityHelper, Confidence.High);
        var row = new LabelDiffRow(pair, new[] { "X" }, Array.Empty<string>(), Array.Empty<string>());
        var density = new DensityRow(pair, 100, 30, -70.0, Verdict.High);
        string report = LabelDiffScanner.FormatReport(new[] { row }, new[] { density });
        // The cross-link emits the density verdict and counts.
        Assert.Contains("Density verdict", report);
        Assert.Contains("High", report);
        Assert.Contains("100", report);
        Assert.Contains("30", report);
    }

    // =====================================================================
    // Scan — orchestration, parallel-safety. Mostly an integration smoke test
    // using temp files because Scan reads from disk.
    // =====================================================================

    [Fact]
    public void Scan_SkipsPairsWithMissingFiles()
    {
        // Pair with null paths → no row. Confirms Scan filters orphans / half-pairs.
        var pair = new EditorPair("Lonely", null, "Lonely", null,
            MatchMethod.Orphan, Confidence.Low);
        var rows = LabelDiffScanner.Scan(new[] { pair });
        Assert.Empty(rows);
    }

    [Fact]
    public void Scan_ReadsRealTempFilesAndProducesDiff()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fbgba-labelsdiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string wfPath = Path.Combine(tempDir, "FakeForm.cs");
            string designerPath = Path.Combine(tempDir, "FakeForm.Designer.cs");
            string avPath = Path.Combine(tempDir, "FakeView.axaml");

            File.WriteAllText(wfPath, "namespace X { partial class FakeForm {} }");
            File.WriteAllText(designerPath, @"
namespace X {
    partial class FakeForm {
        private System.Windows.Forms.Label label1;
        void Init() {
            this.label1 = new System.Windows.Forms.Label();
            this.label1.Text = ""Missing in Avalonia"";
        }
    }
}");
            File.WriteAllText(avPath, @"<UserControl xmlns='https://github.com/avaloniaui'>
  <TextBlock Text='Different' />
</UserControl>");

            var pair = new EditorPair("FakeForm", wfPath, "FakeView", avPath,
                MatchMethod.Heuristic, Confidence.High);
            var rows = LabelDiffScanner.Scan(new[] { pair });
            Assert.Single(rows);
            var row = rows[0];
            Assert.Single(row.WfOnlyLabels);
            Assert.Equal("Missing in Avalonia", row.WfOnlyLabels[0]);
            Assert.Single(row.AvOnlyLabels);
            Assert.Equal("Different", row.AvOnlyLabels[0]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
