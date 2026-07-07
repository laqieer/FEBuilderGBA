using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Source-code guard for embeddable editors converted from Window roots.
    /// Their size metadata moved from AXAML Width/Height attributes to EditorDescriptor.
    /// </summary>
    public class EmbeddableEditorDescriptorTests
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

        private static string ViewsDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views");

        private static readonly Regex ClassDeclWithEmbeddable = new(
            @"public\s+partial\s+class\s+(?<name>\w+)\s*:\s*[^\r\n{]*\bIEmbeddableEditor\b",
            RegexOptions.Compiled);

        private static readonly Regex DescriptorExpr = new(
            @"public\s+EditorDescriptor\s+Descriptor\s*=>\s*new\s*\(\s*""(?<title>[^""]+)""\s*,\s*(?<width>[0-9]+(?:\.[0-9]+)?)\s*,\s*(?<height>[0-9]+(?:\.[0-9]+)?)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static IEnumerable<object[]> EmbeddableEditorSources()
        {
            foreach (var file in Directory.GetFiles(ViewsDir, "*.axaml.cs"))
            {
                var src = File.ReadAllText(file);
                if (ClassDeclWithEmbeddable.IsMatch(src))
                    yield return new object[] { Path.GetFileName(file), src };
            }
        }

        [Theory]
        [MemberData(nameof(EmbeddableEditorSources))]
        public void EmbeddableEditor_DescriptorHasSaneTitleAndPreferredSize(string fileName, string src)
        {
            var match = DescriptorExpr.Match(src);
            Assert.True(match.Success, $"{fileName} must declare an EditorDescriptor with literal title, width, and height.");

            var title = match.Groups["title"].Value;
            var width = double.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
            var height = double.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);

            Assert.False(string.IsNullOrWhiteSpace(title), $"{fileName} descriptor title must be non-empty.");
            Assert.InRange(width, 100, 4000);
            Assert.InRange(height, 100, 4000);
        }

        [Fact]
        public void EmbeddableEditor_DescriptorCoverage_IsNotEmpty()
        {
            var count = 0;
            foreach (var _ in EmbeddableEditorSources())
                count++;

            Assert.True(count >= 1, "Expected at least one converted embeddable editor descriptor to be covered.");
        }
    }
}
