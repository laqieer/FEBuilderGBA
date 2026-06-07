namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression tests for issue #982 — components overlap on the Battle
    /// Animation Palette Editor (Avalonia <c>ImageBattleAnimePalletView</c>).
    ///
    /// The bug: the R/G/B row labels lived in a SEPARATE grid (outer
    /// <c>Grid.Row="2"</c>) above the palette spinner grid (outer
    /// <c>Grid.Row="3"</c>), so the labels overlapped / misaligned with the
    /// spinner rows they were supposed to annotate.
    ///
    /// The fix: the R/G/B labels and the palette grid now share a single Row 2
    /// container (<c>ColumnDefinitions="24,*"</c>). The fixed label column uses
    /// <c>RowDefinitions="36,32,32,32"</c> — exactly mirroring the
    /// <c>PaletteGrid</c>'s row definitions — and the R/G/B TextBlocks are
    /// placed on <c>Grid.Row="1/2/3"</c> instead of inside a vertical
    /// StackPanel, so each label lines up with its spinner row. The dedicated
    /// header row is removed, dropping the outer grid from 6 to 5 rows.
    /// </summary>
    public class ImageBattleAnimePalletLayoutTests
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

        private static string AxamlPath =>
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "ImageBattleAnimePalletView.axaml");

        [Fact]
        public void RgbHeaderLabels_StillExist()
        {
            var src = File.ReadAllText(AxamlPath);

            Assert.Contains("ImageBattleAnimePallet_RHeader_Label", src);
            Assert.Contains("ImageBattleAnimePallet_GHeader_Label", src);
            Assert.Contains("ImageBattleAnimePallet_BHeader_Label", src);
        }

        [Fact]
        public void RgbHeaderLabels_AreOnGridRows_NotInVerticalStackPanel()
        {
            var src = File.ReadAllText(AxamlPath);

            // The labels must no longer live in a vertical StackPanel in the
            // label column — that was the overlap-causing layout.
            Assert.DoesNotContain("<StackPanel Grid.Column=\"0\" Orientation=\"Vertical\"", src);

            // Each label now carries its own Grid.Row so it aligns with the
            // matching spinner row. Match the <TextBlock ...> opening tag that
            // owns each AutomationId and assert it carries the expected
            // Grid.Row — order-independent, resilient to attribute reordering.
            AssertTextBlockHasGridRow(src, "ImageBattleAnimePallet_RHeader_Label", "1");
            AssertTextBlockHasGridRow(src, "ImageBattleAnimePallet_GHeader_Label", "2");
            AssertTextBlockHasGridRow(src, "ImageBattleAnimePallet_BHeader_Label", "3");
        }

        [Fact]
        public void LabelColumn_RowDefinitions_MatchPaletteGrid()
        {
            var src = File.ReadAllText(AxamlPath);

            // Drift guard: BOTH the fixed R/G/B label column AND the PaletteGrid
            // must declare the exact same inline "36,32,32,32" row sequence so
            // every label stays aligned with its spinner row. Asserting the
            // literal appears at least twice covers both grids without depending
            // on attribute order or surrounding markup.
            Assert.True(
                CountOccurrences(src, "36,32,32,32") >= 2,
                "Expected both the R/G/B label column grid and PaletteGrid to "
                + "declare the same inline RowDefinitions=\"36,32,32,32\" sequence.");
        }

        [Fact]
        public void OuterContentGrid_HasFiveRows()
        {
            var src = File.ReadAllText(AxamlPath);

            // The dedicated RGB header row is removed: the outer content grid
            // drops from 6 rows to 5.
            Assert.Contains("RowDefinitions=\"Auto,Auto,*,Auto,Auto\"", src);
            Assert.DoesNotContain("RowDefinitions=\"Auto,Auto,Auto,*,Auto,Auto\"", src);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private static void AssertTextBlockHasGridRow(string src, string automationId, string row)
        {
            // Match the <TextBlock ...> opening tag up to the first '>' so this
            // works for BOTH the self-closing form (<TextBlock ... />) and a
            // non-self-closing opening tag (<TextBlock ...>...</TextBlock>) —
            // mirroring AssertButtonHasShortJumpContent. XAML doesn't allow an
            // unescaped '>' inside an attribute value, so a non-greedy match up
            // to the next '>' captures the full attribute list either way.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                src,
                @"<TextBlock\b[^>]*?>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var tag = m.Value;
                if (tag.Contains($"AutomationId=\"{automationId}\"")
                    && tag.Contains($"Grid.Row=\"{row}\""))
                {
                    return;
                }
            }

            Assert.Fail(
                $"Expected a <TextBlock> with AutomationId=\"{automationId}\" "
                + $"and Grid.Row=\"{row}\" (in any attribute order), but none was found.");
        }
    }
}
