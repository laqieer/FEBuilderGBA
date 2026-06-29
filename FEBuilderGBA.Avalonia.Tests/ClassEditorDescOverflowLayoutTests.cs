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
//      to 380px — sized to the measured worst case (NumericUpDown desires
//      ~120px incl. spinner chrome + label clamped to MaxWidth=200 + 34px
//      "Desc" button + StackPanel spacing => ~378px natural block width).
//
// IMPORTANT (test-discrimination): the Desc block is a Horizontal StackPanel
// with default HorizontalAlignment=Stretch, so its ARRANGED bounds always
// equal the full grid-column width regardless of content — asserting on the
// arranged right edge is a FALSE GUARD that passes against both the buggy and
// fixed layouts. Instead we populate DescTextLabel with a long string (so the
// label reaches its MaxWidth), then re-Measure the StackPanel with infinite
// available width to obtain its NATURAL (unconstrained) content width, and
// assert that natural width fits inside the column. That genuinely FAILS when
// the column is too narrow (the pre-fix 170px) and PASSES at the fixed 380px.
// Mirrors the proven pattern in ClassEditorListPreviewTests
// (PreviewBorder_FitsInsideLeftColumn_EvenWithLongName).
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
        // Worst-case content: long enough that DescTextLabel reaches its
        // MaxWidth=200 so the Desc block hits its natural maximum width.
        private const string LongText = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW"; // 59 W's
        // The pre-fix Identity/Misc Desc column width (#1682). The populated
        // Desc block must exceed this — proving the test would fail against
        // the buggy layout.
        private const double OldColumnWidth = 170.0;

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
        /// Discriminating regression: with a long description, the Identity /
        /// Misc Desc block's NATURAL content width must fit inside the widened
        /// Desc column. Populating the label forces the block to its true
        /// maximum width (NumericUpDown 100 + label up to MaxWidth=200 + "Desc"
        /// button + spacing). The block's natural width is measured with
        /// infinite available width (so Stretch cannot mask it), then compared
        /// to the actual column width. The companion assertion proves the
        /// populated block would have overflowed the pre-fix 170px column, so
        /// this test FAILS against the buggy layout and PASSES against the fix.
        /// </summary>
        [AvaloniaFact]
        public void IdentityMisc_DescBlock_DoesNotOverflowColumn()
        {
            var view = new ClassEditorView();
            view.Show();
            try
            {
                var descBox = view.FindControl<NumericUpDown>("DescIdBox");
                var descLabel = view.FindControl<TextBlock>("DescTextLabel");
                Assert.NotNull(descBox);
                Assert.NotNull(descLabel);

                // Force worst-case content so the block reaches its natural max.
                descLabel!.Text = LongText;

                view.UpdateLayout();
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();

                // The Desc block = the horizontal StackPanel hosting DescIdBox.
                var descBlock = descBox!.GetVisualAncestors().OfType<StackPanel>().First();

                // The Identity/Misc grid = nearest 5-column Grid ancestor.
                var grid = descBox.GetVisualAncestors().OfType<Grid>()
                    .FirstOrDefault(g => g.ColumnDefinitions.Count == 5);
                Assert.NotNull(grid);

                double colWidth = grid!.ColumnDefinitions[4].ActualWidth;
                Assert.True(colWidth > 0,
                    $"Identity/Misc Desc column ActualWidth should be populated after Arrange; got {colWidth}.");

                // Natural (unconstrained) width of the populated Desc block.
                descBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double naturalWidth = descBlock.DesiredSize.Width;

                _output.WriteLine(
                    $"Desc block natural width = {naturalWidth:F1}, col[4] ActualWidth = {colWidth:F1}, " +
                    $"old column = {OldColumnWidth:F1}");

                // (a) Discrimination guard: the populated block exceeds the old
                //     170px column — so this test genuinely fails on the buggy
                //     layout (where col[4] was 170px).
                Assert.True(naturalWidth > OldColumnWidth,
                    $"Populated Desc block natural width ({naturalWidth:F1}) must exceed the pre-fix " +
                    $"column width ({OldColumnWidth:F1}); otherwise this test cannot detect the regression.");

                // (b) The fix: the block fits inside the widened column (1px
                //     tolerance for layout rounding).
                Assert.True(naturalWidth <= colWidth + 1.0,
                    $"Desc block natural width ({naturalWidth:F1}) overflows the Identity/Misc Desc " +
                    $"column ({colWidth:F1}). The column is too narrow.");
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
    }
}
