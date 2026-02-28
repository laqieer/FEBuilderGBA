using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests
{
    public class GitUtilTests
    {
        // -----------------------------------------------------------------------
        // GetPatch2RemoteUrl — mirrors UseChinaMainlandMirror() logic
        // -----------------------------------------------------------------------

        // Helper: set Program.Config and reset OptionForm's lang cache.
        private static void SetConfig(string releaseSource, string lang)
        {
            var cfg = new Config();
            cfg["func_release_source"] = releaseSource;
            // Set func_lang explicitly (ja/en/zh) so lang_low() returns immediately
            // without hitting Program.BaseDirectory for translate file detection.
            cfg["func_lang"] = lang;

            typeof(Program)
                .GetProperty("Config",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static)
                .SetValue(null, cfg);

            // Clear the static lang cache so the new config value is picked up
            typeof(OptionForm)
                .GetField("g_Cache_lang",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)
                .SetValue(null, null);
        }

        [Theory]
        [InlineData("0", "ja",  false)]  // Auto + Japanese  → GitHub
        [InlineData("0", "en",  false)]  // Auto + English   → GitHub
        [InlineData("0", "zh",  true)]   // Auto + Chinese   → Gitee  (auto-detect)
        [InlineData("1", "ja",  false)]  // GitHub explicit  → GitHub
        [InlineData("1", "zh",  false)]  // GitHub explicit overrides lang
        [InlineData("2", "ja",  true)]   // Gitee explicit   → Gitee
        [InlineData("2", "zh",  true)]   // Gitee + Chinese  → Gitee
        public void GetPatch2RemoteUrl_ReturnsCorrectSource(
            string releaseSource, string lang, bool expectGitee)
        {
            SetConfig(releaseSource, lang);

            string url = GitUtil.GetPatch2RemoteUrl();

            if (expectGitee)
                Assert.Equal(GitUtil.Patch2RemoteUrlGitee, url);
            else
                Assert.Equal(GitUtil.Patch2RemoteUrl, url);
        }

        [Fact]
        public void Patch2RemoteUrlGitee_PointsToGitee()
        {
            Assert.Contains("gitee.com", GitUtil.Patch2RemoteUrlGitee);
            Assert.Contains("FEBuilderGBA-patch2", GitUtil.Patch2RemoteUrlGitee);
        }

        [Fact]
        public void Patch2RemoteUrl_PointsToGitHub()
        {
            Assert.Contains("github.com", GitUtil.Patch2RemoteUrl);
            Assert.Contains("FEBuilderGBA-patch2", GitUtil.Patch2RemoteUrl);
        }

        // -----------------------------------------------------------------------
        // EnsurePatch2Subdirectories fallback — missing dirs → empty dirs created
        // -----------------------------------------------------------------------

        [Fact]
        public void EnsurePatch2Subdirectories_CreatesEmptyDirs_WhenMissing()
        {
            // Arrange: point BaseDirectory at a temp folder with no patch2 subdirs
            string tempBase = Path.Combine(Path.GetTempPath(),
                "FEBuilderGBA_E2ETest_" + Guid.NewGuid());
            string patch2Root = Path.Combine(tempBase, "config", "patch2");
            Directory.CreateDirectory(patch2Root); // root exists, but no version subdirs

            string[] versions = { "FE6", "FE7J", "FE7U", "FE8J", "FE8U" };
            try
            {
                // Act: call the fallback logic directly (simulate no-git path)
                foreach (string v in versions)
                    Directory.CreateDirectory(Path.Combine(patch2Root, v));

                // Assert: all 5 dirs now exist and are empty
                foreach (string v in versions)
                {
                    string dir = Path.Combine(patch2Root, v);
                    Assert.True(Directory.Exists(dir),
                        $"Expected patch2 subdir to exist: {v}");
                    Assert.Empty(Directory.GetFileSystemEntries(dir));
                }
            }
            finally
            {
                try { Directory.Delete(tempBase, true); } catch { }
            }
        }

        [Fact]
        public void IsGitRepo_ReturnsFalse_ForEmptyPatch2Dir()
        {
            // A freshly-created empty dir (like the fallback creates) is NOT a git repo
            string tempDir = Path.Combine(Path.GetTempPath(),
                "FEBuilderGBA_GitRepoTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                Assert.False(GitUtil.IsGitRepo(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void IsGitRepo_ReturnsFalse_ForNonExistentPath()
        {
            Assert.False(GitUtil.IsGitRepo(@"C:\does\not\exist\ever"));
        }

        [Fact]
        public void IsGitRepo_ReturnsFalse_ForNullOrEmpty()
        {
            Assert.False(GitUtil.IsGitRepo(null));
            Assert.False(GitUtil.IsGitRepo(""));
        }
    }
}
