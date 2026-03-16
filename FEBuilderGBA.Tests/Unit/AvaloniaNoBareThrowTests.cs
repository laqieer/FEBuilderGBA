using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression test: ensures no bare/empty catch blocks remain in the Avalonia project.
    /// See GitHub issue #65: silent exception swallowing.
    /// </summary>
    public class AvaloniaNoBareThrowTests
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

        private string AvaloniaProjectDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        [Fact]
        public void NoBareEmptyCatchBlocks()
        {
            // Pattern matches: catch { }, catch (Exception) { }, catch (Exception ex) { }
            // where the body is empty (only whitespace)
            var bareCatchPattern = new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.Singleline);
            var violations = new List<string>();

            var csFiles = Directory.GetFiles(AvaloniaProjectDir, "*.cs", SearchOption.AllDirectories);
            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);
                var matches = bareCatchPattern.Matches(content);
                foreach (Match m in matches)
                {
                    // Find line number
                    int lineNum = content[..m.Index].Count(c => c == '\n') + 1;
                    var relPath = Path.GetRelativePath(AvaloniaProjectDir, file);
                    violations.Add($"{relPath}:{lineNum} => {m.Value.Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                $"Found {violations.Count} bare/empty catch block(s) in Avalonia project. " +
                $"All catch blocks should log via Log.Error(). Violations:\n" +
                string.Join("\n", violations));
        }
    }
}
