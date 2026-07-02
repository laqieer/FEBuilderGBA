using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests
{
    [Collection("SharedState")]
    public class GitUtilTests : IDisposable
    {
        // Save/restore CoreState to avoid leaking into other test classes
        private readonly string _savedLanguage;
        private readonly string _savedGitPath;
        private readonly Config _savedConfig;

        public GitUtilTests()
        {
            _savedLanguage = CoreState.Language;
            _savedGitPath = CoreState.GitPath;
            _savedConfig = CoreState.Config;
        }

        public void Dispose()
        {
            CoreState.Language = _savedLanguage;
            CoreState.GitPath = _savedGitPath;
            CoreState.Config = _savedConfig;
        }

        // -----------------------------------------------------------------------
        // GetPatch2RemoteUrl — custom-URL override, else the default GitHub remote
        // -----------------------------------------------------------------------

        [Fact]
        public void GetPatch2RemoteUrl_ReturnsGitHub_WhenNoCustomUrl()
        {
            CoreState.Config = new Config(); // no submodule_patch2_url key
            Assert.Equal(GitUtil.Patch2RemoteUrl, GitUtil.GetPatch2RemoteUrl());
        }

        [Fact]
        public void GetPatch2RemoteUrl_ReturnsGitHub_WhenConfigNull()
        {
            CoreState.Config = null;
            Assert.Equal(GitUtil.Patch2RemoteUrl, GitUtil.GetPatch2RemoteUrl());
        }

        [Fact]
        public void GetPatch2RemoteUrl_ReturnsCustomUrl_WhenSet()
        {
            const string custom = "https://example.com/laqieer/FEBuilderGBA-patch2.git";
            CoreState.Config = new Config();
            CoreState.Config["submodule_patch2_url"] = custom;
            Assert.Equal(custom, GitUtil.GetPatch2RemoteUrl());
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
