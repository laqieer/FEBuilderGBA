using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that WinForms --screenshot-all infrastructure is correctly wired:
    /// ScreenshotFormRegistry, ScreenshotAllRunner, and Program.cs CLI flag parsing.
    /// </summary>
    public class WinFormsScreenshotRegistryTests
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

        private string WinFormsDir => Path.Combine(SolutionDir, "FEBuilderGBA");

        private string RegistrySource => File.ReadAllText(
            Path.Combine(WinFormsDir, "ScreenshotFormRegistry.cs"));

        private string RunnerSource => File.ReadAllText(
            Path.Combine(WinFormsDir, "ScreenshotAllRunner.cs"));

        private string ProgramSource => File.ReadAllText(
            Path.Combine(WinFormsDir, "Program.cs"));

        [Fact]
        public void Registry_HasGetAllFormFactoriesMethod()
        {
            Assert.Contains("GetAllFormFactories()", RegistrySource);
            Assert.Contains("List<(string Name, Func<Form> Factory)>", RegistrySource);
        }

        [Fact]
        public void Registry_ContainsAtLeast200Entries()
        {
            // Count entries matching the pattern ("ViewName", () => new FormType())
            var pattern = new Regex(@"\(""\w+View"",\s*\(\)\s*=>\s*new\s+\w+\(\)\)");
            int count = pattern.Matches(RegistrySource).Count;
            Assert.True(count >= 200,
                $"Expected at least 200 form factories but found {count}");
        }

        [Fact]
        public void Registry_NamesMatchAvaloniaConvention()
        {
            // All registry entry names should end with "View"
            var namePattern = new Regex(@"\(""(\w+)"",\s*\(\)\s*=>");
            var names = namePattern.Matches(RegistrySource)
                .Select(m => m.Groups[1].Value)
                .ToList();

            Assert.True(names.Count > 0, "No registry entries found");

            var nonViewNames = names.Where(n => !n.EndsWith("View")).ToList();
            Assert.True(nonViewNames.Count == 0,
                $"Registry names not ending in 'View': {string.Join(", ", nonViewNames)}");
        }

        [Fact]
        public void Registry_NoDuplicateNames()
        {
            var namePattern = new Regex(@"\(""(\w+)"",\s*\(\)\s*=>");
            var names = namePattern.Matches(RegistrySource)
                .Select(m => m.Groups[1].Value)
                .ToList();

            var duplicates = names.GroupBy(n => n)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0,
                $"Duplicate registry names: {string.Join(", ", duplicates)}");
        }

        [Fact]
        public void Runner_HasDrawToBitmapCapture()
        {
            Assert.Contains("DrawToBitmap", RunnerSource);
        }

        [Fact]
        public void Runner_OutputsScreenshotMarkers()
        {
            Assert.Contains("SCREENSHOT: Capturing", RunnerSource);
            Assert.Contains("SCREENSHOT: Results:", RunnerSource);
        }

        [Fact]
        public void Runner_UsesWinFormsPrefix()
        {
            Assert.Contains("WinForms_", RunnerSource);
        }

        [Fact]
        public void Program_ParsesScreenshotAllFlag()
        {
            Assert.Contains("--screenshot-all", ProgramSource);
            Assert.Contains("ScreenshotAllRunner", ProgramSource);
        }

        [Fact]
        public void Program_ParsesScreenshotDirFlag()
        {
            Assert.Contains("--screenshot-dir", ProgramSource);
        }

        [Fact]
        public void Registry_CoreEditorsPresent()
        {
            // Verify key editors are in the registry
            Assert.Contains("\"UnitEditorView\"", RegistrySource);
            Assert.Contains("\"ItemEditorView\"", RegistrySource);
            Assert.Contains("\"ClassEditorView\"", RegistrySource);
            Assert.Contains("\"MapSettingView\"", RegistrySource);
            Assert.Contains("\"EventCondView\"", RegistrySource);
            Assert.Contains("\"SongTableView\"", RegistrySource);
            Assert.Contains("\"TextViewerView\"", RegistrySource);
            Assert.Contains("\"HexEditorView\"", RegistrySource);
        }

        /// <summary>
        /// Verify that every name in the WinForms registry also appears in the
        /// Avalonia GetAllEditorFactories, ensuring full coverage for comparison.
        /// </summary>
        [Fact]
        public void Registry_AllNamesExistInAvaloniaRegistry()
        {
            var avaloniaMainWindow = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

            // Extract Avalonia editor names
            var avaloniaPattern = new Regex(@"\(""(\w+)"",\s*\(\)\s*=>\s*wm\.Open<\w+>\(\)\)");
            var avaloniaNames = avaloniaPattern.Matches(avaloniaMainWindow)
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            // Extract WinForms registry names
            var namePattern = new Regex(@"\(""(\w+)"",\s*\(\)\s*=>");
            var winFormsNames = namePattern.Matches(RegistrySource)
                .Select(m => m.Groups[1].Value)
                .ToList();

            var notInAvalonia = winFormsNames.Where(n => !avaloniaNames.Contains(n)).ToList();
            Assert.True(notInAvalonia.Count == 0,
                $"WinForms registry names not in Avalonia registry: {string.Join(", ", notInAvalonia)}");
        }
    }
}
