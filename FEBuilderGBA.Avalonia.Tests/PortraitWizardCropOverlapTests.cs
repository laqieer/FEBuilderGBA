// SPDX-License-Identifier: GPL-3.0-or-later
// Layout regression for #1757: Portrait Import Wizard "2b. Detail" — the Eye/Mouth
// crop Y and H labels overlapped the preceding NumericUpDown's down-arrow.
//
// Root cause: a GLOBAL style (App.axaml) sets NumericUpDown MinWidth=120, but the
// two crop grids used 80px input columns, so each NUD's natural width (>=120)
// overflowed its 80px cell into the column-2 (Y/H) label. Fix widened the input
// columns to 120 (ColumnDefinitions="Auto,120,Auto,120") + a label margin.
//
// Discrimination (per ClassEditorDescOverflowLayoutTests): asserting on arranged
// Bounds edges is a false guard (fixed-pixel columns tile perfectly). Instead we
// measure each column-1 NUD's NATURAL width (infinite available width) and require
// it to fit the input column's ActualWidth; a companion guard asserts that natural
// width exceeds the pre-fix 80px column, so the test genuinely fails on the buggy
// layout and passes on the fix.
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
    public class PortraitWizardCropOverlapTests
    {
        // The pre-fix input-column width (#1757). A NUD wider than this proves the
        // test would fail against the buggy 80px layout.
        const double OldInputColumnWidth = 80.0;

        readonly ITestOutputHelper _output;
        public PortraitWizardCropOverlapTests(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void ImagePortraitImporterView_CanInstantiate()
        {
            var v = new ImagePortraitImporterView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaTheory]
        [InlineData("EyeCropXInput")]
        [InlineData("EyeCropWInput")]
        [InlineData("MouthCropXInput")]
        [InlineData("MouthCropWInput")]
        public void CropInput_FitsItsColumn_NoOverlapWithNextLabel(string inputName)
        {
            var view = new ImagePortraitImporterView();

            // The crop grids live inside the collapsed "2b. Detail" Expander;
            // it must be expanded or its content has no arranged width.
            var expander = view.FindControl<Expander>("DetailExpander");
            Assert.NotNull(expander);
            expander!.IsExpanded = true;

            view.Show();
            try
            {
                view.UpdateLayout();
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();

                var nud = view.FindControl<NumericUpDown>(inputName);
                Assert.NotNull(nud);

                // The crop grid = nearest 4-column Grid ancestor.
                var grid = nud!.GetVisualAncestors().OfType<Grid>()
                    .FirstOrDefault(g => g.ColumnDefinitions.Count == 4);
                Assert.NotNull(grid);

                // Column 1 hosts the X/W input.
                double colWidth = grid!.ColumnDefinitions[1].ActualWidth;
                Assert.True(colWidth > 0,
                    $"input column ActualWidth should be populated after Arrange; got {colWidth}.");

                // Natural (unconstrained) width of the NumericUpDown (>= global MinWidth 120).
                nud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double naturalWidth = nud.DesiredSize.Width;

                _output.WriteLine($"{inputName}: natural={naturalWidth:F1}  col[1].ActualWidth={colWidth:F1}  old={OldInputColumnWidth:F1}");

                // (a) Discrimination: the NUD is wider than the pre-fix 80px column,
                //     so this test genuinely fails against the buggy layout.
                Assert.True(naturalWidth > OldInputColumnWidth,
                    $"{inputName} natural width ({naturalWidth:F1}) must exceed the pre-fix column ({OldInputColumnWidth:F1}); " +
                    "otherwise the test cannot detect the overlap regression.");

                // (b) Fix: the NUD fits inside its (now 120px) input column, so it no
                //     longer overflows into the next (Y/H) label. 1px layout tolerance.
                Assert.True(naturalWidth <= colWidth + 1.0,
                    $"{inputName} natural width ({naturalWidth:F1}) overflows its input column ({colWidth:F1}); " +
                    "the down-arrow would collide with the next label.");
            }
            finally
            {
                view.Close();
            }
        }
    }
}
