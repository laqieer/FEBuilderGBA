// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression tests for MoveCostEditorView (#1721).
//
// Root cause (verified against the reporter's 1440x900 macOS screenshot): each
// terrain cell is a horizontal StackPanel [TextBlock label][NumericUpDown] placed
// into a 5-column Grid with ColumnDefinitions="*,*,*,*,*". The window is
// Width=1601 SizeToContent=WidthAndHeight; on a 1440px macOS screen the window is
// clamped, and because the right-pane ScrollViewer had horizontal scrolling
// disabled (Avalonia default), the 5 "*" columns were forced to share the clamped
// viewport. Under macOS's wider system font each fixed-width cell (label +
// NumericUpDown) exceeds its share, so the NUD's spinner buttons overlap the NEXT
// column's "0xNN Name" label (clearly visible in the issue screenshot).
//
// Fix: Auto (content-sized) grid columns + narrower label (92) + wider NUD (120,
// >= the ~120px Avalonia NUD minimum) + HorizontalScrollBarVisibility=Auto.
//
// TEST NOTE (honest discrimination): the headless Windows layout engine sizes
// "*" columns to MAX(proportional-share, content-desired-width) and never
// compresses them below content — so it cannot reproduce the macOS wider-font
// compression that triggers the overlap. The primary discriminator here is
// therefore STRUCTURAL: TerrainGrid's columns must be Auto (content-sized), which
// is the exact invariant that makes the columns immune to compression-overlap on
// ANY font/screen. A secondary geometric guard asserts no sibling overlap at the
// realized layout (catches future width-budget regressions).
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MoveCostEditorLayoutTests
    {
        private readonly ITestOutputHelper _output;
        public MoveCostEditorLayoutTests(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void MoveCostEditorView_CanInstantiate()
        {
            var v = new MoveCostEditorView();
            Assert.NotNull(v.Content);
        }

        /// <summary>
        /// The right-pane content ScrollViewer must allow horizontal scrolling so
        /// the grid stays reachable on screens narrower than its content (#1721).
        /// </summary>
        [AvaloniaFact]
        public void ContentScrollViewer_AllowsHorizontalScroll()
        {
            var v = new MoveCostEditorView();
            var scroller = v.GetLogicalDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(sv => Grid.GetColumn(sv) == 1)
                ?? v.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(scroller);
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                scroller!.HorizontalScrollBarVisibility);
        }

        /// <summary>
        /// PRIMARY discrimination guard (#1721). The TerrainGrid's 5 columns must
        /// be content-sized (GridUnitType.Auto), NOT star ("*"). Auto columns grow
        /// to fit each cell's content on any font/screen, so they can never be
        /// compressed below content width — which is exactly the macOS wider-font
        /// compression that pushed each NUD's spinner over the next column's label.
        /// This FAILS against the pre-fix "*,*,*,*,*" layout and PASSES on the fix,
        /// independent of the headless engine's "*"-sizing quirk.
        /// </summary>
        [AvaloniaFact]
        public void TerrainGrid_Columns_AreContentSized_NotStar()
        {
            var view = new MoveCostEditorView();
            try
            {
                var grid = view.FindControl<Grid>("TerrainGrid");
                Assert.NotNull(grid);
                Assert.Equal(5, grid!.ColumnDefinitions.Count);

                foreach (var col in grid.ColumnDefinitions)
                {
                    Assert.True(col.Width.IsAuto,
                        $"TerrainGrid column must be Auto (content-sized) so it cannot compress " +
                        $"below content and overlap the neighbour on macOS (#1721); found '{col.Width}'.");
                    Assert.False(col.Width.IsStar,
                        "TerrainGrid columns must NOT be '*' — star columns compress under the " +
                        "macOS wider font and the NUD spinner overlaps the next column's label (#1721).");
                }
            }
            finally
            {
                view.Close();
            }
        }

        /// <summary>
        /// SECONDARY geometric guard (#1721): at the realized layout, no terrain
        /// NUD's right edge may cross into the next column's label left edge. This
        /// catches a future width-budget regression (e.g. someone re-widening the
        /// label past the NUD column gap) even while the columns stay Auto. (On the
        /// headless engine Auto columns never compress, so this is a guard, not the
        /// primary pre-fix discriminator — see the structural test above.)
        /// </summary>
        [AvaloniaFact]
        public void TerrainGrid_NudsDoNotOverlapNextColumnLabel_AtRealizedLayout()
        {
            var view = new MoveCostEditorView();
            view.Show();
            try
            {
                view.UpdateLayout();

                var grid = view.FindControl<Grid>("TerrainGrid");
                Assert.NotNull(grid);

                var labels = grid!.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => new Box(t, t.TranslatePoint(new Point(0, 0), grid), t.Bounds.Width))
                    .Where(b => b.P.HasValue && b.W > 0)
                    .ToList();
                var nuds = grid.GetVisualDescendants().OfType<NumericUpDown>()
                    .Select(n => new Box(n, n.TranslatePoint(new Point(0, 0), grid), n.Bounds.Width))
                    .Where(b => b.P.HasValue && b.W > 0)
                    .ToList();

                Assert.Equal(65, nuds.Count);
                Assert.Equal(65, labels.Count);

                int overlaps = 0;
                var detail = new List<string>();

                foreach (var nud in nuds)
                {
                    double nudLeft = nud.P!.Value.X;
                    double nudRight = nudLeft + nud.W;
                    double nudY = nud.P!.Value.Y;

                    Box? nextLabel = labels
                        .Where(l => l.P!.Value.X > nudLeft + 1 // strictly to the right
                                    && System.Math.Abs(l.P!.Value.Y - nudY) < 14) // same row band
                        .OrderBy(l => l.P!.Value.X)
                        .FirstOrDefault();

                    if (nextLabel == null) continue; // last column on this row

                    double labelLeft = nextLabel.P!.Value.X;
                    if (nudRight > labelLeft + 0.5)
                    {
                        overlaps++;
                        detail.Add($"NUD right={nudRight:F0} > nextLabel left={labelLeft:F0} (y~{nudY:F0})");
                    }
                }

                _output.WriteLine($"checked {nuds.Count} NUDs; overlaps={overlaps}");
                if (detail.Count > 0)
                    _output.WriteLine(string.Join("\n", detail.Take(10)));

                Assert.True(overlaps == 0,
                    $"{overlaps} terrain NUD(s) overlap the next column's label (#1721). " +
                    $"Sample:\n{string.Join("\n", detail.Take(5))}");
            }
            finally
            {
                view.Close();
            }
        }

        private sealed class Box
        {
            public Control C { get; }
            public Point? P { get; }
            public double W { get; }
            public Box(Control c, Point? p, double w) { C = c; P = p; W = w; }
        }
    }
}
