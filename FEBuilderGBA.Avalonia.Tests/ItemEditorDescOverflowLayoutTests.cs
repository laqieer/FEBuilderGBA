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
//      widened to 320px. This test proves the DescId block no longer
//      horizontally overlaps the UseDescId_Link label after arrange.
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
        /// After Measure + Arrange at a realistic editor size, the DescId
        /// block (the StackPanel hosting DescIdBox + DescTextLabel + "Desc"
        /// button) must not horizontally overlap the "Use Desc (W4):" label
        /// (UseDescId_Link) that sits in the next column pair. We translate
        /// both into the shared Basic Info grid coordinate space and assert
        /// the DescId block's right edge stays left of the UseDescId_Link
        /// label's left edge. Assertions tolerate sub-pixel layout rounding.
        /// </summary>
        [AvaloniaFact]
        public void BasicInfo_DescBlock_DoesNotOverlapUseDescLabel()
        {
            var view = new ItemEditorView();
            view.Show();
            try
            {
                view.UpdateLayout();
                view.Measure(new Size(1408, 856));
                view.Arrange(new Rect(0, 0, 1408, 856));
                view.UpdateLayout();

                var descBox = view.FindControl<NumericUpDown>("DescIdBox");
                var useDescLabel = view.FindControl<TextBlock>("UseDescId_Link");
                Assert.NotNull(descBox);
                Assert.NotNull(useDescLabel);

                // The DescId value block is the StackPanel hosting DescIdBox.
                var descBlock = descBox!.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault();
                Assert.NotNull(descBlock);

                // Shared ancestor: the Basic Info grid (5 columns).
                var grid = descBox.GetVisualAncestors().OfType<Grid>()
                    .FirstOrDefault(g => g.ColumnDefinitions.Count == 5);
                Assert.NotNull(grid);

                var descRight = descBlock!.TranslatePoint(
                    new Point(descBlock.Bounds.Width, 0), grid!);
                var useDescLeft = useDescLabel!.TranslatePoint(new Point(0, 0), grid!);
                Assert.NotNull(descRight);
                Assert.NotNull(useDescLeft);

                _output.WriteLine(
                    $"DescId block right edge X = {descRight!.Value.X:F1}, " +
                    $"UseDesc label left edge X = {useDescLeft!.Value.X:F1}");

                // Allow 1px tolerance for layout rounding.
                Assert.True(descRight.Value.X <= useDescLeft.Value.X + 1.0,
                    $"DescId block right edge ({descRight.Value.X:F1}) overlaps the " +
                    $"'Use Desc (W4):' label left edge ({useDescLeft.Value.X:F1}). " +
                    "The Basic Info Desc column is too narrow.");
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
