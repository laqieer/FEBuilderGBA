using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for dark mode support and hardcoded color cleanup in Avalonia views.
    /// Validates that no hardcoded hex color values remain in AXAML files and that
    /// the theme toggle infrastructure exists.
    /// </summary>
    public class AvaloniaDarkModeTests
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

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        [Fact]
        public void NoHardcodedHexColorsInViewAxamlFiles()
        {
            var viewsDir = Path.Combine(AvaloniaDir, "Views");
            Assert.True(Directory.Exists(viewsDir), "Views directory must exist");

            var axamlFiles = Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories);
            Assert.NotEmpty(axamlFiles);

            // Match Background="#XXXXXX" or Foreground="#XXXXXX" (6 or 8 hex digit colors)
            var hexColorPattern = new Regex(@"(Background|Foreground)=""#([0-9A-Fa-f]{6,8})""");
            // Semi-transparent overlays (8-digit ARGB with low alpha, e.g. #20000000) are intentional
            // and work acceptably in both light and dark themes — exclude them.
            var semiTransparentPattern = new Regex(@"^[0-3][0-9A-Fa-f][0-9A-Fa-f]{6}$");
            var violations = new List<string>();

            foreach (var file in axamlFiles)
            {
                var content = File.ReadAllText(file);
                var matches = hexColorPattern.Matches(content);
                foreach (Match m in matches)
                {
                    var hexValue = m.Groups[2].Value;
                    // Allow semi-transparent 8-digit ARGB colors (alpha < 0x40)
                    if (hexValue.Length == 8 && semiTransparentPattern.IsMatch(hexValue))
                        continue;
                    violations.Add($"{Path.GetFileName(file)}: {m.Value}");
                }
            }

            Assert.True(violations.Count == 0,
                $"Found {violations.Count} hardcoded hex color(s) in AXAML files that should use DynamicResource:\n" +
                string.Join("\n", violations));
        }

        [Fact]
        public void DarkModeMenuItemExistsInMainWindow()
        {
            var mainWindowAxaml = Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml");
            Assert.True(File.Exists(mainWindowAxaml));

            var content = File.ReadAllText(mainWindowAxaml);
            Assert.Contains("DarkModeMenuItem", content);
            Assert.Contains("ToggleDarkMode_Click", content);
        }

        [Fact]
        public void DarkModeToggleHandlerExistsInCodeBehind()
        {
            var mainWindowCs = Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs");
            Assert.True(File.Exists(mainWindowCs));

            var content = File.ReadAllText(mainWindowCs);
            Assert.Contains("ToggleDarkMode_Click", content);
            Assert.Contains("ToggleTheme", content);
        }

        [Fact]
        public void FluentThemeExistsInAppAxaml()
        {
            var appAxaml = Path.Combine(AvaloniaDir, "App.axaml");
            Assert.True(File.Exists(appAxaml));

            var content = File.ReadAllText(appAxaml);
            Assert.Contains("<FluentTheme />", content);
        }

        [Fact]
        public void ThemeResourcesDefinedInAppAxaml()
        {
            var appAxaml = Path.Combine(AvaloniaDir, "App.axaml");
            var content = File.ReadAllText(appAxaml);

            // Verify key theme resources are defined
            Assert.Contains("StatusBarBackgroundBrush", content);
            Assert.Contains("ToolbarBackgroundBrush", content);
            Assert.Contains("SectionHeadingBrush", content);
            Assert.Contains("CardBorderBrush", content);
            Assert.Contains("WarningBackgroundBrush", content);
            Assert.Contains("InfoBannerBackgroundBrush", content);
        }

        [Fact]
        public void ThemeToggleMethodExistsInAppCs()
        {
            var appCs = Path.Combine(AvaloniaDir, "App.axaml.cs");
            Assert.True(File.Exists(appCs));

            var content = File.ReadAllText(appCs);
            Assert.Contains("public void ToggleTheme()", content);
            Assert.Contains("IsDarkMode", content);
            Assert.Contains("ThemeConfigKey", content);
        }

        [Fact]
        public void ThemePreferencePersistedViaConfig()
        {
            var appCs = Path.Combine(AvaloniaDir, "App.axaml.cs");
            var content = File.ReadAllText(appCs);

            // Verify config persistence pattern
            Assert.Contains("CoreState.Config", content);
            Assert.Contains("Avalonia_Theme", content);
            Assert.Contains(".Save()", content);
        }

        [Fact]
        public void AxamlViewsUseDynamicResourceForColors()
        {
            var viewsDir = Path.Combine(AvaloniaDir, "Views");
            var axamlFiles = Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories);

            // Check that at least some views use DynamicResource for colors
            int dynamicResourceCount = 0;
            foreach (var file in axamlFiles)
            {
                var content = File.ReadAllText(file);
                if (content.Contains("DynamicResource") &&
                    (content.Contains("BackgroundBrush") || content.Contains("BorderBrush}")))
                {
                    dynamicResourceCount++;
                }
            }

            Assert.True(dynamicResourceCount >= 5,
                $"Expected at least 5 views using DynamicResource for theme colors, found {dynamicResourceCount}");
        }
    }
}
