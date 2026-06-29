// SPDX-License-Identifier: GPL-3.0-or-later
//
// Layout-regression tests for the field-spacing fixes reported in
// discussion #1674 and tracked by:
//   #1684 SupportUnitEditorView  — cramped columns
//   #1685 MoveCostEditorView     — terrain names overflow + spinner overlap
//   #1686 MapExitPointView       — Coordinate/Direction/Flag fields compressed
//
// These are pure cosmetic AXAML / grid-builder layout changes, so the
// guard parses the View source directly (ROM-free, render-free, disk-light)
// and asserts the intended layout constants are present. This prevents a
// future edit from silently re-introducing the cramped widths.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class FieldSpacingLayoutTests
    {
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

        static string ViewPath(string fileName)
        {
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views", fileName);
            Assert.True(File.Exists(path), $"View source not found at {path}");
            return path;
        }

        static readonly XNamespace Av = "https://github.com/avaloniaui";

        // -----------------------------------------------------------------
        // #1684 SupportUnitEditorView — columns must be wide enough that the
        // partner-name labels don't clip and the source-unit name doesn't
        // butt against the "Open Unit" button.
        // -----------------------------------------------------------------

        [Fact]
        public void SupportUnitEditor_SourceUnitRow_NameColumnWidened_AndTrims()
        {
            var doc = XDocument.Load(ViewPath("SupportUnitEditorView.axaml"));

            // Source-unit name label gets ellipsis trimming so a long name
            // can never overflow its cell into the Open Unit button.
            var nameLabel = doc.Descendants(Av + "TextBlock")
                .FirstOrDefault(e => (string?)e.Attribute("Name") == "SourceUnitNameLabel");
            Assert.NotNull(nameLabel);
            Assert.Equal("CharacterEllipsis", (string?)nameLabel!.Attribute("TextTrimming"));
            // #650: a trimmed preview must self-bind a tooltip so the full
            // (possibly truncated) text is recoverable on hover.
            AssertSelfBindingToolTip(nameLabel, "SourceUnitNameLabel");

            // Its grid's name column must be wider than the old cramped 150.
            var grid = nameLabel.Ancestors(Av + "Grid").First();
            int nameColWidth = ParseColumn(grid, index: 2);
            Assert.True(nameColWidth >= 200,
                $"Source-unit name column must be >= 200 (was 150); got {nameColWidth}");
        }

        [Fact]
        public void SupportUnitEditor_PartnersGrid_WidenedColumns_AndTrimmingLabels()
        {
            var doc = XDocument.Load(ViewPath("SupportUnitEditorView.axaml"));

            // Locate the partner grid via the first partner NUD.
            var partner1Nud = doc.Descendants(Av + "NumericUpDown")
                .FirstOrDefault(e => (string?)e.Attribute("Name") == "Partner1Nud");
            Assert.NotNull(partner1Nud);

            // Every partner NUD must have an explicit width + left alignment so
            // the spinner buttons never overlap the adjacent name column.
            for (int i = 1; i <= 7; i++)
            {
                var nud = doc.Descendants(Av + "NumericUpDown")
                    .FirstOrDefault(e => (string?)e.Attribute("Name") == $"Partner{i}Nud");
                Assert.NotNull(nud);
                Assert.False(string.IsNullOrEmpty((string?)nud!.Attribute("Width")),
                    $"Partner{i}Nud must have an explicit Width");
                Assert.Equal("Left", (string?)nud.Attribute("HorizontalAlignment"));
            }

            // Every partner-name label must trim with an ellipsis AND self-bind
            // a tooltip (#650) so the truncated unit name is visible on hover.
            for (int i = 1; i <= 7; i++)
            {
                var lbl = doc.Descendants(Av + "TextBlock")
                    .FirstOrDefault(e => (string?)e.Attribute("Name") == $"Partner{i}NameLabel");
                Assert.NotNull(lbl);
                Assert.Equal("CharacterEllipsis", (string?)lbl!.Attribute("TextTrimming"));
                AssertSelfBindingToolTip(lbl, $"Partner{i}NameLabel");
            }

            // The partner grid's name column is now a star column (consumes
            // available width) instead of the old fixed 150.
            var grid = partner1Nud!.Ancestors(Av + "Grid").First();
            string cols = (string)grid.Attribute("ColumnDefinitions")!;
            var parts = cols.Split(',');
            Assert.Equal("*", parts[2].Trim());
            // Talk-button column widened from 60 -> 80.
            Assert.True(int.TryParse(parts[3].Trim(), out int talkCol) && talkCol >= 80,
                $"Talk button column must be >= 80 (was 60); got '{parts[3]}'");
        }

        // -----------------------------------------------------------------
        // #1685 MoveCostEditorView — the terrain grid is built in code-behind.
        // The terrain-name label must trim (no overflow) and the NUD must have
        // an explicit width + left alignment (no spinner overlap).
        // -----------------------------------------------------------------

        [Fact]
        public void MoveCostEditor_TerrainGridBuilder_TrimsNames_AndPinsNudWidth()
        {
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "MoveCostEditorView.axaml.cs");
            Assert.True(File.Exists(path), $"Code-behind not found at {path}");
            string src = File.ReadAllText(path);

            // The label must use ellipsis trimming so long terrain names are
            // truncated cleanly instead of overflowing the column.
            Assert.Contains("TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis", src);
            Assert.Contains("MaxWidth = 140", src);

            // The NUD must have an explicit Width and left alignment — relying
            // on MinWidth alone let the spinner buttons bleed into the neighbour.
            Assert.Contains("Width = 110", src);
            Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", src);

            // Guard against the regressed pattern returning.
            Assert.DoesNotContain("MinWidth = 120", src);
        }

        // -----------------------------------------------------------------
        // #1686 MapExitPointView — the Coordinate/Direction/Flag grid columns
        // must be wide enough for the NUDs (>=120) and the spanned
        // combo/textbox (>=180), and the NUDs left-aligned.
        // -----------------------------------------------------------------

        [Fact]
        public void MapExitPoint_CoordinateGrid_WidenedColumns_AndLeftAlignedNuds()
        {
            var doc = XDocument.Load(ViewPath("MapExitPointView.axaml"));

            // Locate the coordinate grid via the X NUD.
            var xBox = doc.Descendants(Av + "NumericUpDown")
                .FirstOrDefault(e => (string?)e.Attribute("Name") == "ExitXBox");
            Assert.NotNull(xBox);
            var grid = xBox!.Ancestors(Av + "Grid").First();

            // Numeric value column (col 2) must be >= 120 for the spinner.
            int valueCol = ParseColumn(grid, index: 2);
            Assert.True(valueCol >= 120,
                $"Coordinate value column must be >= 120 (was 80); got {valueCol}");

            // Spanned column 4 must accommodate the 180px Direction combo /
            // Flag-name box (cols 3+4 span). Col 4 alone is now >= 180.
            int spanCol = ParseColumn(grid, index: 4);
            Assert.True(spanCol >= 180,
                $"Spanned value column must be >= 180 for the Direction combo; got {spanCol}");

            // X/Y/Escape/Flag NUDs must be left-aligned so a stretched cell
            // doesn't blow the spinner up across the whole column.
            foreach (var name in new[] { "ExitXBox", "ExitYBox", "EscapeMethodBox", "FlagBox" })
            {
                var nud = doc.Descendants(Av + "NumericUpDown")
                    .FirstOrDefault(e => (string?)e.Attribute("Name") == name);
                Assert.NotNull(nud);
                Assert.Equal("Left", (string?)nud!.Attribute("HorizontalAlignment"));
            }
        }

        [Fact]
        public void MoveCostEditor_TerrainLabelTooltip_SetInCodeBehind()
        {
            // The terrain labels are built in code-behind and trimmed with an
            // ellipsis, so #650 coverage is provided by mirroring the full text
            // into a tooltip whenever the label text is (re)assigned.
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "MoveCostEditorView.axaml.cs");
            string src = File.ReadAllText(path);
            Assert.Contains("ToolTip.SetTip(_labelFields[i], _labelFields[i].Text)", src);
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Assert a TextBlock element declares the #650 element-name self-binding
        /// tooltip — ToolTip.Tip="{Binding #&lt;name&gt;.Text}" — matching its own Name.
        /// In unprefixed AXAML the attached-property attribute has the literal
        /// local name "ToolTip.Tip" (the dot is part of the name, no namespace).
        /// </summary>
        static void AssertSelfBindingToolTip(XElement textBlock, string expectedName)
        {
            var tip = textBlock.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "ToolTip.Tip" ||
                                     a.Name.LocalName == "Tip");
            Assert.True(tip != null,
                $"{expectedName}: trimmed TextBlock must declare a ToolTip.Tip self-binding (#650)");
            Assert.Equal($"{{Binding #{expectedName}.Text}}", tip!.Value);
        }

        /// <summary>
        /// Parse the integer width of the Nth column from a Grid's
        /// ColumnDefinitions string (e.g. "160,40,120,40,200"). Star/Auto
        /// columns return int.MaxValue so width assertions treat them as
        /// "at least as wide as needed".
        /// </summary>
        static int ParseColumn(XElement grid, int index)
        {
            string cols = (string)grid.Attribute("ColumnDefinitions")!;
            var parts = cols.Split(',');
            Assert.True(index < parts.Length,
                $"Grid only has {parts.Length} columns; asked for index {index}");
            string p = parts[index].Trim();
            if (p == "*" || p.EndsWith("*") || p == "Auto")
                return int.MaxValue;
            return int.Parse(p);
        }
    }
}
