namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that all 302 Avalonia editor views opened from MainWindow
    /// are listed in the --smoke-test-all code path and in the documentation.
    /// </summary>
    public class AvaloniaAllEditorsCoverageTests
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

        /// <summary>
        /// All editors opened from MainWindow click handlers must also appear
        /// in GetAllEditorFactories() for smoke test coverage.
        /// </summary>
        [Fact]
        public void AllMainWindowEditors_AreInSmokeTestFactories()
        {
            var mainWindowSrc = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

            // Extract all "WindowManager.Instance.Open<XxxView>()" from click handlers
            var openPattern = new System.Text.RegularExpressions.Regex(
                @"WindowManager\.Instance\.Open<(\w+)>\(\)");
            var clickEditors = openPattern.Matches(mainWindowSrc)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToHashSet();

            // Extract all editor names from GetAllEditorFactories()
            var factoryPattern = new System.Text.RegularExpressions.Regex(
                @"\(""(\w+)"",\s*\(\)\s*=>\s*wm\.Open<\w+>\(\)\)");
            var factoryEditors = factoryPattern.Matches(mainWindowSrc)
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            var missing = clickEditors.Except(factoryEditors).OrderBy(x => x).ToList();

            Assert.True(missing.Count == 0,
                $"These editors are in MainWindow click handlers but missing from " +
                $"GetAllEditorFactories(): {string.Join(", ", missing)}");
        }

        /// <summary>
        /// All editors in GetAllEditorFactories() must be listed in the
        /// avalonia-gui-forms.md documentation.
        /// </summary>
        [Fact]
        public void AllSmokeTestEditors_AreInDocumentation()
        {
            var mainWindowSrc = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

            var factoryPattern = new System.Text.RegularExpressions.Regex(
                @"\(""(\w+)"",\s*\(\)\s*=>\s*wm\.Open<\w+>\(\)\)");
            var factoryEditors = factoryPattern.Matches(mainWindowSrc)
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            var docPath = Path.Combine(SolutionDir, "docs", "avalonia-gui-forms.md");
            Assert.True(File.Exists(docPath), "docs/avalonia-gui-forms.md must exist");

            var docContent = File.ReadAllText(docPath);

            var missing = factoryEditors
                .Where(e => !docContent.Contains(e))
                .OrderBy(x => x)
                .ToList();

            Assert.True(missing.Count == 0,
                $"These editors are in GetAllEditorFactories() but missing from docs: " +
                $"{string.Join(", ", missing)}");
        }

        /// <summary>
        /// Verify the doc lists exactly 302 editor views.
        /// </summary>
        [Fact]
        public void Documentation_Lists302Editors()
        {
            var docPath = Path.Combine(SolutionDir, "docs", "avalonia-gui-forms.md");
            var docContent = File.ReadAllText(docPath);

            // Count rows that contain "View" in table format
            var viewPattern = new System.Text.RegularExpressions.Regex(
                @"\|\s*\d+\s*\|\s*\w+View\s*\|");
            int count = viewPattern.Matches(docContent).Count;

            Assert.Equal(302, count);
        }

        /// <summary>
        /// Verify GetAllEditorFactories has exactly 302 entries.
        /// </summary>
        [Fact]
        public void SmokeTestFactories_Has302Entries()
        {
            var mainWindowSrc = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

            var factoryPattern = new System.Text.RegularExpressions.Regex(
                @"\(""(\w+)"",\s*\(\)\s*=>\s*wm\.Open<\w+>\(\)\)");
            int count = factoryPattern.Matches(mainWindowSrc).Count;

            Assert.Equal(302, count);
        }
    }
}
