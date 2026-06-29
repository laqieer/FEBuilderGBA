// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression tests for ClassEditorView (#1682).
//
// Two AXAML-only fixes are guarded here:
//   1. The content ScrollViewer (Grid.Column=1) must allow horizontal
//      scrolling (HorizontalScrollBarVisibility=Auto) so the wider
//      Pointers / Movement / Terrain rows (P60 Rain / P64 Snow etc.) can
//      always be reached even when the editor is narrower than the content.
//   2. The Identity / Misc grid's Desc-hosting column (Col 4) was 170px,
//      too narrow for NumericUpDown(100) + DescTextLabel(MaxWidth=200) +
//      "Desc" button + theme padding, so the Desc block overflowed and the
//      far-right pointer fields became unreachable. The column was widened
//      to 360px. This test proves the Desc block (DescTextLabel + the
//      JumpToDesc "Desc" button) fits inside that column once arranged, and
//      that the Movement/Terrain pointer inputs (Ptr60Box / Ptr64Box) exist.
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Regression for #1682: Class Editor description overflow / unreachable
    /// pointer fields. Layout-only AXAML fix (ScrollViewer horizontal scroll +
    /// widened Identity/Misc Desc column).
    /// </summary>
    public class ClassEditorDescOverflowLayoutTests
    {
        private readonly ITestOutputHelper _output;

        public ClassEditorDescOverflowLayoutTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// The content ScrollViewer (the one in Grid.Column=1 hosting the
        /// editor body) must enable horizontal scrolling so widened pointer
        /// rows remain reachable.
        /// </summary>
        [AvaloniaFact]
        public void ContentScrollViewer_AllowsHorizontalScroll()
        {
            var view = new ClassEditorView();
            var scroller = FindContentScrollViewer(view);
            Assert.NotNull(scroller);
            Assert.Equal(ScrollBarVisibility.Auto, scroller!.HorizontalScrollBarVisibility);
        }

        /// <summary>
        /// The Movement / Terrain pointer inputs that were previously pushed
        /// off-screen must exist (Ptr60 Rain / Ptr64 Snow are the fields
        /// called out in #1682).
        /// </summary>
        [AvaloniaTheory]
        [InlineData("Ptr60Box")]
        [InlineData("Ptr64Box")]
        public void MovementTerrain_PointerInputs_Exist(string controlName)
        {
            var view = new ClassEditorView();
            var box = view.FindControl<TextBox>(controlName);
            Assert.NotNull(box);
        }

        /// <summary>
        /// After Measure + Arrange at a realistic editor size, the Identity /
        /// Misc Desc block (DescTextLabel + the "Desc" jump button) must fit
        /// horizontally inside the grid column it lives in — i.e. it must not
        /// overflow past the column's right edge. We assert the Desc button's
        /// right edge stays within the arranged width of the parent Grid that
        /// hosts the Identity / Misc fields. Assertions stay tolerant of
        /// sub-pixel layout rounding rather than over-fitting exact values.
        /// </summary>
        [AvaloniaFact]
        public void IdentityMisc_DescBlock_DoesNotOverflowColumn()
        {
            var view = new ClassEditorView();
            view.Show();
            try
            {
                view.UpdateLayout();
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();

                var descLabel = view.FindControl<TextBlock>("DescTextLabel");
                var descButton = FindByContent<Button>(view, "Desc");
                Assert.NotNull(descLabel);
                Assert.NotNull(descButton);

                // The Identity/Misc grid is the Grid ancestor of DescTextLabel
                // that defines 5 columns (the widened one). Walk up to it.
                var grid = descLabel!.GetVisualAncestors().OfType<Grid>()
                    .FirstOrDefault(g => g.ColumnDefinitions.Count == 5);
                Assert.NotNull(grid);

                // Translate the Desc button's right edge into the grid's
                // coordinate space; it must not exceed the grid's width.
                var rightEdge = descButton!.TranslatePoint(
                    new Point(descButton.Bounds.Width, 0), grid!);
                Assert.NotNull(rightEdge);

                double gridWidth = grid!.Bounds.Width;
                _output.WriteLine(
                    $"Desc button right edge X = {rightEdge!.Value.X:F1}, grid width = {gridWidth:F1}");

                // Allow 1px tolerance for layout rounding.
                Assert.True(rightEdge.Value.X <= gridWidth + 1.0,
                    $"Desc block right edge ({rightEdge.Value.X:F1}) overflows the " +
                    $"Identity/Misc grid width ({gridWidth:F1}). The Desc column is too narrow.");
            }
            finally
            {
                view.Close();
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// The content ScrollViewer is the one placed in Grid.Column=1 of the
        /// root grid (the editor body host). Pick the ScrollViewer whose
        /// Grid.Column attached value is 1. Uses the logical tree so the
        /// control resolves even before the view is shown/realized.
        /// </summary>
        private static ScrollViewer? FindContentScrollViewer(Control root)
        {
            return root.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault(sv => Grid.GetColumn(sv) == 1)
                ?? root.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }

        private static T? FindByContent<T>(Control root, string content) where T : ContentControl
        {
            return root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(c => c.Content as string == content);
        }
    }
}
