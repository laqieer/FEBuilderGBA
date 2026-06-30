// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression tests for ImageBattleScreenView's Palette grid (#1724).
//
// Root cause: the 16-column palette grid holds 48 R/G/B NumericUpDowns at
// Width=60 columns (Increment=8, Margin=2 => ~56px inner). Each NUD clamps to
// its ~120px spinner-chrome minimum and overlaps neighbours. Widening to >=120px
// x 16 = 1920px is rejected (pushes colours off-screen), so we collapse the
// spinners instead: a Grid.Styles implicit style sets ShowButtonSpinner=False
// scoped to PaletteGrid, turning each NUD into a compact text field that fits
// 56px. AllowSpin stays true so focused wheel/keyboard +/-8 still works.
//
// This DOES reproduce on Windows headless, so these tests are meaningful here.
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ImageBattleScreenPaletteLayoutTests
    {
        private readonly ITestOutputHelper _output;
        public ImageBattleScreenPaletteLayoutTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// Every NumericUpDown inside PaletteGrid must have its spinner buttons
        /// collapsed (ShowButtonSpinner=false) so it fits the 56px inner column.
        /// </summary>
        [AvaloniaFact]
        public void PaletteNuds_HaveSpinnerButtonsCollapsed()
        {
            var view = new ImageBattleScreenView();
            view.Show();
            try
            {
                view.UpdateLayout();
                var grid = view.FindControl<Grid>("PaletteGrid");
                Assert.NotNull(grid);

                var nuds = grid!.GetVisualDescendants().OfType<NumericUpDown>().ToList();
                // 16 columns x 3 (R/G/B) = 48 palette spinners.
                Assert.Equal(48, nuds.Count);

                foreach (var nud in nuds)
                {
                    Assert.False(nud.ShowButtonSpinner,
                        $"Palette NUD '{nud.Name}' must have ShowButtonSpinner=false so it fits " +
                        "the 56px column without overlapping its neighbour (#1724).");
                    // AllowSpin must remain true so focused wheel/keyboard +/-8 works.
                    Assert.True(nud.AllowSpin,
                        $"Palette NUD '{nud.Name}' should keep AllowSpin=true for wheel/keyboard editing.");
                }
            }
            finally
            {
                view.Close();
            }
        }

        /// <summary>
        /// With the spinners collapsed, adjacent palette NUDs (left-to-right) must
        /// not overlap at the 60px column width.
        /// </summary>
        [AvaloniaFact]
        public void PaletteNuds_DoNotOverlapHorizontally()
        {
            var view = new ImageBattleScreenView();
            view.Show();
            try
            {
                view.Width = 1280;
                view.Height = 900;
                view.Measure(new Size(1280, 900));
                view.Arrange(new Rect(0, 0, 1280, 900));
                view.UpdateLayout();

                var grid = view.FindControl<Grid>("PaletteGrid");
                Assert.NotNull(grid);

                var nuds = grid!.GetVisualDescendants().OfType<NumericUpDown>()
                    .Select(n => new { N = n, P = n.TranslatePoint(new Point(0, 0), view) })
                    .Where(x => x.P.HasValue && x.N.Bounds.Width > 0)
                    .ToList();
                Assert.True(nuds.Count >= 48, $"expected 48 palette NUDs, got {nuds.Count}");

                // Group into rows by Y, then check left-to-right adjacency per row.
                var rows = nuds.GroupBy(x => System.Math.Round(x.P!.Value.Y / 4) * 4);
                int overlaps = 0;
                foreach (var row in rows)
                {
                    var ordered = row.OrderBy(x => x.P!.Value.X).ToList();
                    for (int i = 0; i + 1 < ordered.Count; i++)
                    {
                        double aRight = ordered[i].P!.Value.X + ordered[i].N.Bounds.Width;
                        double bLeft = ordered[i + 1].P!.Value.X;
                        if (aRight > bLeft + 0.5)
                        {
                            overlaps++;
                            _output.WriteLine(
                                $"overlap: #{i} right={aRight:F0} > #{i + 1} left={bLeft:F0} (y~{ordered[i].P!.Value.Y:F0})");
                        }
                    }
                }

                Assert.True(overlaps == 0,
                    $"{overlaps} adjacent palette NUD pair(s) overlap at 60px columns (#1724).");
            }
            finally
            {
                view.Close();
            }
        }
    }
}
