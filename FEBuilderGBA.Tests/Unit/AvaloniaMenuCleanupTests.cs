using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Validates that MainWindow.axaml has no hidden (dead) menu items.
    /// </summary>
    public class AvaloniaMenuCleanupTests
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

        [Fact]
        public void MainWindow_HasNoHiddenMenuItems()
        {
            var axamlPath = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml");
            Assert.True(File.Exists(axamlPath), $"MainWindow.axaml should exist at {axamlPath}");

            var content = File.ReadAllText(axamlPath);

            // Match <MenuItem ... IsVisible="False" ... /> patterns
            var matches = Regex.Matches(content, @"<MenuItem\b[^>]*IsVisible\s*=\s*""False""[^>]*/?>");
            Assert.True(matches.Count == 0,
                $"MainWindow.axaml should not contain hidden MenuItems but found {matches.Count}: " +
                string.Join(", ", matches.Cast<Match>().Select(m => m.Value)));
        }

        [Fact]
        public void MainWindow_HasNoRedoMenuItem()
        {
            var axamlPath = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml");
            Assert.True(File.Exists(axamlPath), $"MainWindow.axaml should exist at {axamlPath}");

            var content = File.ReadAllText(axamlPath);

            Assert.DoesNotContain("Redo", content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
