using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression tests for #1381: the Avalonia Song Track Editor's
    /// "Search for new resources online" link must open the populated fork wiki
    /// page in the browser (parity with ImageMagicFEditorView / WinForms
    /// MainFormUtil.GotoMoreData), NOT pop a no-op info dialog with the dead
    /// upstream wiki URL.
    ///
    /// Assertions are scoped to the LinkInternet_Click method body so a sibling
    /// handler's unrelated Process.Start cannot false-pass the test.
    /// </summary>
    public class AvaloniaSongTrackLinkTests
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

        private static string SongTrackViewSource =>
            File.ReadAllText(Path.Combine(
                SolutionDir, "FEBuilderGBA.Avalonia", "Views", "SongTrackView.axaml.cs"));

        /// <summary>
        /// Extracts the body of the LinkInternet_Click handler by locating its
        /// signature and brace-matching to the corresponding closing brace, so
        /// assertions are scoped to that handler only. Brace counting is robust
        /// to reformatting/indentation changes (does not assume a fixed indent
        /// for the closing brace).
        /// </summary>
        private static string LinkInternetClickBody
        {
            get
            {
                var src = SongTrackViewSource;
                var sig = Regex.Match(src, @"void\s+LinkInternet_Click\s*\([^)]*\)");
                Assert.True(sig.Success,
                    "LinkInternet_Click handler not found in SongTrackView.axaml.cs");

                int open = src.IndexOf('{', sig.Index + sig.Length);
                Assert.True(open >= 0, "Opening brace of LinkInternet_Click not found");

                int depth = 0;
                int end = -1;
                for (int i = open; i < src.Length; i++)
                {
                    if (src[i] == '{') depth++;
                    else if (src[i] == '}')
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                Assert.True(end > open, "Closing brace of LinkInternet_Click not found");
                return src.Substring(sig.Index, end - sig.Index + 1);
            }
        }

        [Fact]
        public void LinkInternetClick_OpensForkWikiUrl()
        {
            var body = LinkInternetClickBody;
            Assert.Contains("github.com/laqieer/FEBuilderGBA/wiki/MoreData", body);
        }

        [Fact]
        public void LinkInternetClick_DoesNotUseDeadUpstreamWikiUrl()
        {
            var body = LinkInternetClickBody;
            Assert.DoesNotContain("github.com/FEBuilderGBA/FEBuilderGBA/wiki", body);
        }

        [Fact]
        public void LinkInternetClick_LaunchesBrowserViaProcessStart()
        {
            var body = LinkInternetClickBody;
            Assert.Contains("Process.Start", body);
            Assert.Contains("UseShellExecute = true", body);
        }

        [Fact]
        public void LinkInternetClick_IsNotANoOpInfoDialog()
        {
            var body = LinkInternetClickBody;
            // The old broken implementation only called ShowInfo with the dead
            // URL; the fixed handler must not fall back to an info dialog.
            Assert.DoesNotContain("ShowInfo", body);
        }
    }
}
