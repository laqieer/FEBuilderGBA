using System.Xml.Linq;

namespace FEBuilderGBA.Tests.Unit
{
    public class AndroidThemeContractTests
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
        public void MainActivity_UsesAppCompatThemeResource()
        {
            string source = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Android", "MainActivity.cs"));

            Assert.Contains("Theme = \"@style/FEBuilderTheme\"", source);
        }

        [Fact]
        public void ThemeResource_InheritsAppCompatNoActionBar()
        {
            string path = Path.Combine(
                SolutionDir,
                "FEBuilderGBA.Android",
                "Resources",
                "values",
                "styles.xml");

            Assert.True(File.Exists(path), $"Android theme resource should exist at {path}");

            var document = XDocument.Load(path);
            var style = document.Root!.Elements("style")
                .SingleOrDefault(element => element.Attribute("name")?.Value == "FEBuilderTheme");
            Assert.NotNull(style);
            Assert.Equal("Theme.AppCompat.DayNight.NoActionBar", style!.Attribute("parent")?.Value);
        }
    }
}
