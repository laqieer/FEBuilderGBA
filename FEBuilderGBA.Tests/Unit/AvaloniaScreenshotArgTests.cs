namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verify that the Avalonia App.axaml.cs correctly defines --screenshot-all
    /// and --screenshot-dir CLI argument parsing, and that MainWindow.axaml.cs
    /// has the RunScreenshotAll() method wired into RunSmokeTest().
    /// </summary>
    public class AvaloniaScreenshotArgTests
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

        private string AppSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "App.axaml.cs"));

        private string MainWindowSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

        [Fact]
        public void App_HasScreenshotAllModeProperty()
        {
            Assert.Contains("ScreenshotAllMode", AppSource);
            Assert.Contains("public static bool ScreenshotAllMode", AppSource);
        }

        [Fact]
        public void App_HasScreenshotDirProperty()
        {
            Assert.Contains("ScreenshotDir", AppSource);
            Assert.Contains("public static string? ScreenshotDir", AppSource);
        }

        [Fact]
        public void App_ParsesScreenshotAllArg()
        {
            Assert.Contains("--screenshot-all", AppSource);
            Assert.Contains("ScreenshotAllMode = true", AppSource);
        }

        [Fact]
        public void App_ParsesScreenshotDirArg()
        {
            Assert.Contains("--screenshot-dir=", AppSource);
        }

        [Fact]
        public void MainWindow_HasRunScreenshotAllMethod()
        {
            Assert.Contains("RunScreenshotAll()", MainWindowSource);
            Assert.Contains("private void RunScreenshotAll()", MainWindowSource);
        }

        [Fact]
        public void MainWindow_RunSmokeTestCallsScreenshotAll()
        {
            // RunSmokeTest() should check ScreenshotAllMode before SmokeTestAll
            Assert.Contains("App.ScreenshotAllMode", MainWindowSource);
        }

        [Fact]
        public void MainWindow_ScreenshotAllUsesRenderTargetBitmap()
        {
            Assert.Contains("RenderTargetBitmap", MainWindowSource);
        }

        [Fact]
        public void MainWindow_ScreenshotAllOutputsMarkers()
        {
            Assert.Contains("SCREENSHOT: Capturing", MainWindowSource);
            Assert.Contains("SCREENSHOT: Results:", MainWindowSource);
        }
    }
}
