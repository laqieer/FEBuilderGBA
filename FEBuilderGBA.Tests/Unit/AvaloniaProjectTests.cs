using System.Xml.Linq;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Validates that the FEBuilderGBA.Avalonia project scaffold is correctly set up.
    /// These tests check csproj structure, required files, and Core dependency.
    /// </summary>
    public class AvaloniaProjectTests
    {
        private static string SolutionDir
        {
            get
            {
                // Walk up from test bin directory to find solution root
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string AvaloniaProjectDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        [Fact]
        public void CsprojExists()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            Assert.True(File.Exists(csproj), $"Avalonia csproj should exist at {csproj}");
        }

        [Fact]
        public void CsprojTargetsNet9()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var tfm = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
            Assert.Equal("net9.0", tfm);
        }

        [Fact]
        public void CsprojIsWinExe()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var outputType = doc.Descendants(ns + "OutputType").FirstOrDefault()?.Value;
            Assert.Equal("WinExe", outputType);
        }

        [Fact]
        public void CsprojReferencesCore()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var coreRef = doc.Descendants(ns + "ProjectReference")
                .Any(e => e.Attribute("Include")?.Value?.Contains("FEBuilderGBA.Core") == true);
            Assert.True(coreRef, "Avalonia project must reference FEBuilderGBA.Core");
        }

        [Fact]
        public void CsprojReferencesAvalonia()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var avaloniaRef = doc.Descendants(ns + "PackageReference")
                .Any(e => e.Attribute("Include")?.Value == "Avalonia");
            Assert.True(avaloniaRef, "Avalonia project must reference Avalonia NuGet package");
        }

        [Fact]
        public void CsprojReferencesFluentTheme()
        {
            var csproj = Path.Combine(AvaloniaProjectDir, "FEBuilderGBA.Avalonia.csproj");
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var fluentRef = doc.Descendants(ns + "PackageReference")
                .Any(e => e.Attribute("Include")?.Value == "Avalonia.Themes.Fluent");
            Assert.True(fluentRef, "Avalonia project must reference Avalonia.Themes.Fluent");
        }

        [Fact]
        public void RequiredFilesExist()
        {
            var expectedFiles = new[]
            {
                "Program.cs",
                "App.axaml",
                "App.axaml.cs",
                "AvaloniaAppServices.cs",
                "app.manifest",
                Path.Combine("Views", "MainWindow.axaml"),
                Path.Combine("Views", "MainWindow.axaml.cs"),
            };

            foreach (var file in expectedFiles)
            {
                var fullPath = Path.Combine(AvaloniaProjectDir, file);
                Assert.True(File.Exists(fullPath), $"Required file should exist: {file}");
            }
        }

        [Fact]
        public void AppAxamlContainsFluentTheme()
        {
            var appAxaml = File.ReadAllText(Path.Combine(AvaloniaProjectDir, "App.axaml"));
            Assert.Contains("FluentTheme", appAxaml);
        }

        [Fact]
        public void ProgramCsContainsEntryPoint()
        {
            var programCs = File.ReadAllText(Path.Combine(AvaloniaProjectDir, "Program.cs"));
            Assert.Contains("static void Main", programCs);
            Assert.Contains("BuildAvaloniaApp", programCs);
            Assert.Contains("StartWithClassicDesktopLifetime", programCs);
        }

        [Fact]
        public void AvaloniaAppServicesSetsUpCoreState()
        {
            var appCs = File.ReadAllText(Path.Combine(AvaloniaProjectDir, "App.axaml.cs"));
            Assert.Contains("CoreState.BaseDirectory", appCs);
            Assert.Contains("CoreState.Services", appCs);
            Assert.Contains("AvaloniaAppServices", appCs);
        }

        [Fact]
        public void SolutionContainsAvaloniaProject()
        {
            var slnPath = Path.Combine(SolutionDir, "FEBuilderGBA.sln");
            var slnContent = File.ReadAllText(slnPath);
            Assert.Contains("FEBuilderGBA.Avalonia", slnContent);
        }
    }
}
