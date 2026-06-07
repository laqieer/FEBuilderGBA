using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Static AXAML regression tests for issue #984 — two layout fixes on the
    /// Avalonia Unit Palette Editor (<c>ImageUnitPaletteView.axaml</c>):
    ///
    /// B1: The header row after the "Selected Address" bar carried a stray,
    /// inert <c>ImageUnitPalette_Write_Label</c> TextBlock with Text="Write".
    /// It looked like a non-functional "Write" button next to the selected
    /// address. It is removed; the header grid drops from 5 columns to 4.
    /// The real Write affordance (<c>ImageUnitPalette_Write_Button</c> on the
    /// Edit tab) and the Palette Write button
    /// (<c>ImageUnitPalette_PaletteWrite_Button</c>) are untouched.
    ///
    /// B2: The Palette tab's Column-1 R/G/B header row used a mismatched
    /// 8-column layout (<c>Auto,Auto,Auto,Auto,40,60,60,60</c>) that did not
    /// line up with the swatch rows' <c>30,40,60,60,60</c> tracks, so the
    /// R/G/B captions sat off to the right of the spinner columns. The header
    /// now uses the same <c>30,40,60,60,60</c> tracks with the R/G/B labels at
    /// columns 2/3/4 (directly over the R/G/B spinners).
    /// </summary>
    public class ImageUnitPaletteLayoutTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private static string AxamlPath
        {
            get
            {
                var viewsDir = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views");
                var hit = Directory.GetFiles(viewsDir, "ImageUnitPaletteView.axaml", SearchOption.AllDirectories)
                    .FirstOrDefault();
                return hit ?? throw new FileNotFoundException(
                    "Cannot locate ImageUnitPaletteView.axaml under FEBuilderGBA.Avalonia/Views");
            }
        }

        private static string Src => File.ReadAllText(AxamlPath);

        // ===== B1: stray "Write" header label removed =====

        [Fact]
        public void B1_StrayWriteHeaderLabel_IsRemoved()
        {
            var src = Src;

            // The inert stray label must be gone entirely.
            Assert.DoesNotContain("ImageUnitPalette_Write_Label", src);

            // The header grid must no longer be the 5-column variant; it is now
            // a 4-column grid (Address caption + addr + "Selected Address:" +
            // the read-only selected-address box).
            Assert.DoesNotContain("ColumnDefinitions=\"Auto,Auto,Auto,Auto,Auto\"", src);
            Assert.Contains("ColumnDefinitions=\"Auto,Auto,Auto,Auto\"", src);
        }

        [Fact]
        public void B1_FunctionalWriteButtons_AreStillPresent()
        {
            var src = Src;

            // The real Write button (Edit tab) and Palette Write button
            // (Palette tab) MUST remain.
            Assert.Contains("ImageUnitPalette_Write_Button", src);
            Assert.Contains("ImageUnitPalette_PaletteWrite_Button", src);
        }

        // ===== B2: palette R/G/B header aligned with spinner tracks =====

        [Fact]
        public void B2_MismatchedHeaderColumnDefinitions_AreRemoved()
        {
            // The old 8-column header definition that misaligned R/G/B must be gone.
            Assert.DoesNotContain("ColumnDefinitions=\"Auto,Auto,Auto,Auto,40,60,60,60\"", Src);
        }

        [Theory]
        [InlineData("ImageUnitPalette_R_Header_Label", "2")]
        [InlineData("ImageUnitPalette_G_Header_Label", "3")]
        [InlineData("ImageUnitPalette_B_Header_Label", "4")]
        public void B2_HeaderLabel_SitsOverSpinnerColumn(string automationId, string expectedColumn)
        {
            var src = Src;

            // Capture every <TextBlock ...> opening tag (XAML disallows '>' inside
            // attribute values without escaping, so a non-greedy match to the next
            // '>' is safe). Find the one carrying this header's AutomationId and
            // assert its Grid.Column — order-independent so attribute re-orderings
            // don't break the test.
            var matches = Regex.Matches(src, @"<TextBlock\b[^>]*?>", RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                var tag = m.Value;
                if (tag.Contains($"AutomationId=\"{automationId}\""))
                {
                    Assert.Contains($"Grid.Column=\"{expectedColumn}\"", tag);
                    return;
                }
            }

            Assert.Fail(
                $"Expected a <TextBlock> with AutomationId=\"{automationId}\" "
                + $"and Grid.Column=\"{expectedColumn}\", but none was found.");
        }
    }
}
