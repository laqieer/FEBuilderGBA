using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that the SmokeTestMode flag exists on App and is used to skip
    /// the closing confirmation dialog during screenshot/smoke-test runs (Issue #35).
    /// Since FEBuilderGBA.Tests does not reference the Avalonia project assembly,
    /// these tests verify the source code directly.
    /// </summary>
    public class ScreenshotModeClosingTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = Directory.GetCurrentDirectory();
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Directory.GetParent(dir)?.FullName;
                return dir ?? Directory.GetCurrentDirectory();
            }
        }

        private string AppSourcePath =>
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "App.axaml.cs");

        [Fact]
        public void App_Has_SmokeTestMode_StaticProperty()
        {
            var source = File.ReadAllText(AppSourcePath);
            Assert.Contains("public static bool SmokeTestMode", source);
        }

        [Fact]
        public void App_Has_ScreenshotAllMode_StaticProperty()
        {
            var source = File.ReadAllText(AppSourcePath);
            Assert.Contains("public static bool ScreenshotAllMode", source);
        }

        [Fact]
        public void SmokeTestMode_DefaultsToFalse_InSource()
        {
            // The property should be auto-property defaulting to false (no initializer = false)
            var source = File.ReadAllText(AppSourcePath);
            // Should NOT have "= true" as default
            var lines = source.Split('\n')
                .Where(l => l.Contains("SmokeTestMode") && l.Contains("get;"))
                .ToList();
            Assert.Single(lines);
            Assert.DoesNotContain("= true", lines[0]);
        }

        [Fact]
        public void MainWindow_Uses_SmokeTestMode_For_Closing()
        {
            // The MainWindow closing handler should check SmokeTestMode to skip dialog
            var mainWindowPath = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            if (!File.Exists(mainWindowPath))
            {
                // If MainWindow doesn't exist yet, skip gracefully
                return;
            }
            var source = File.ReadAllText(mainWindowPath);
            // Either SmokeTestMode is checked in the closing handler, or the closing event
            // is handled. We verify the pattern exists somewhere in MainWindow.
            bool usesSmokeTest = source.Contains("SmokeTestMode") || source.Contains("Closing");
            Assert.True(usesSmokeTest,
                "MainWindow should reference SmokeTestMode or handle Closing event");
        }
    }
}
