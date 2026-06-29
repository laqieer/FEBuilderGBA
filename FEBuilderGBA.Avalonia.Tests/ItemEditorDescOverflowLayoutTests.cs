// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression tests for ItemEditorView (#1683).
//
// Two AXAML-only fixes are guarded here:
//   1. The content ScrollViewer (Grid.Column=1) must allow horizontal
//      scrolling (HorizontalScrollBarVisibility=Auto) so widened Basic Info
//      rows remain reachable when the editor is narrower than its content.
//   2. The Basic Info grid's two value columns (Col 1 and Col 4) were 200px,
//      too narrow for NumericUpDown(100) + DescTextLabel(MaxWidth=160) +
//      "Desc" button + padding, so the Desc / mini-desc block overflowed and
//      overlapped the adjacent "Use Desc (W4):" label. Both columns were
//      widened to 340px — sized to the measured worst case (NumericUpDown
//      desires ~120px incl. spinner chrome + label clamped to MaxWidth=160 +
//      34px "Desc" button + StackPanel spacing => ~338px natural block width).
//
// IMPORTANT (test-discrimination): the Desc / UseDesc blocks are Horizontal
// StackPanels with default HorizontalAlignment=Stretch, so their ARRANGED
// bounds always equal the full grid-column width regardless of content —
// asserting on arranged right/left edges is a FALSE GUARD that passes against
// both the buggy and fixed layouts. Instead we populate DescTextLabel (and
// UseDescTextLabel) with a long string so the label reaches its MaxWidth=160,
// then re-Measure the DescId StackPanel with infinite available width to get
// its NATURAL content width, and assert it fits inside the (widened) value
// column. Because the block fits within its own column it cannot paint into
// the next column pair (the "Use Desc (W4):" label) — that is the no-overlap
// guarantee. The companion assertion proves the populated block exceeds the
// pre-fix 200px column, so this test FAILS against the buggy layout.
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
    /// Regression for #1683: Item Editor Basic Info description block overflow
    /// overlapping the "Use Desc (W4):" label. Layout-only AXAML fix
    /// (ScrollViewer horizontal scroll + widened Basic Info value columns).
    /// </summary>
    public class ItemEditorDescOverflowLayoutTests
    {
        // Worst-case content: long enough that DescTextLabel reaches its
        // MaxWidth=160 so the Desc block hits its natural maximum width.
        private const string LongText = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW"; // 59 W's
        // The pre-fix Basic Info value column width (#1683). The populated
        // Desc block must exceed this — proving the test would fail against
        // the buggy layout.
        private const double OldColumnWidth = 200.0;

        private readonly ITestOutputHelper _output;

        public ItemEditorDescOverflowLayoutTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// The content ScrollViewer (the one in Grid.Column=1 hosting the
        /// editor body) must enable horizontal scrolling.
        /// </summary>
        [AvaloniaFact]
        public void ContentScrollViewer_AllowsHorizontalScroll()
        {
            var view = new ItemEditorView();
            var scroller = FindContentScrollViewer(view);
            Assert.NotNull(scroller);
            Assert.Equal(ScrollBarVisibility.Auto, scroller!.HorizontalScrollBarVisibility);
        }

        /// <summary>
        /// Discriminating regression: with long descriptions, the Basic Info
        /// DescId block's NATURAL content width must fit inside the widened
        /// value column, so it cannot overflow into and overlap the next
        /// column pair's "Use Desc (W4):" label. We populate both desc labels,
        /// measure the DescId block with infinite available width (so Stretch
        /// cannot mask its true size), and compare to the actual column width.
        /// The companion assertion proves the populated block would have
        /// overflowed the pre-fix 200px column, so this test FAILS against the
        /// buggy layout and PASSES against the fix.
        /// </summary>
        [AvaloniaFact]
        public void BasicInfo_DescBlock_DoesNotOverlapUseDescLabel()
        {
            var view = new ItemEditorView();
            view.Show();
            try
            {
                var descBox = view.FindControl<NumericUpDown>("DescIdBox");
                var descLabel = view.FindControl<TextBlock>("DescTextLabel");
                var useDescLabel = view.FindControl<TextBlock>("UseDescTextLabel");
                Assert.NotNull(descBox);
                Assert.NotNull(descLabel);
                Assert.NotNull(useDescLabel);

                // Force worst-case content on both desc blocks.
                descLabel!.Text = LongText;
                useDescLabel!.Text = LongText;

                view.UpdateLayout();
                view.Measure(new Size(1408, 856));
                view.Arrange(new Rect(0, 0, 1408, 856));
                view.UpdateLayout();

                // The DescId block = the horizontal StackPanel hosting DescIdBox.
                var descBlock = descBox!.GetVisualAncestors().OfType<StackPanel>().First();

                // The Basic Info grid = nearest 5-column Grid ancestor.
                var grid = descBox.GetVisualAncestors().OfType<Grid>()
                    .FirstOrDefault(g => g.ColumnDefinitions.Count == 5);
                Assert.NotNull(grid);

                double colWidth = grid!.ColumnDefinitions[1].ActualWidth;
                Assert.True(colWidth > 0,
                    $"Basic Info Desc column ActualWidth should be populated after Arrange; got {colWidth}.");

                // Natural (unconstrained) width of the populated Desc block.
                descBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double naturalWidth = descBlock.DesiredSize.Width;

                _output.WriteLine(
                    $"DescId block natural width = {naturalWidth:F1}, col[1] ActualWidth = {colWidth:F1}, " +
                    $"old column = {OldColumnWidth:F1}");

                // (a) Discrimination guard: the populated block exceeds the old
                //     200px column — so this test genuinely fails on the buggy
                //     layout (where col[1] was 200px).
                Assert.True(naturalWidth > OldColumnWidth,
                    $"Populated DescId block natural width ({naturalWidth:F1}) must exceed the pre-fix " +
                    $"column width ({OldColumnWidth:F1}); otherwise this test cannot detect the regression.");

                // (b) The fix: the block fits inside the widened column (1px
                //     tolerance), so it cannot overlap the "Use Desc" label.
                Assert.True(naturalWidth <= colWidth + 1.0,
                    $"DescId block natural width ({naturalWidth:F1}) overflows the Basic Info value " +
                    $"column ({colWidth:F1}) and would overlap the 'Use Desc (W4):' label. " +
                    "The column is too narrow.");
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
        /// root grid (the editor body host). Uses the logical tree so the
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
