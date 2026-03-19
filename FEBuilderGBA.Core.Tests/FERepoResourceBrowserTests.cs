using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class FERepoResourceBrowserTests
    {
        [Fact]
        public void FindRepoRoot_ReturnsNull_WhenNotInRepo()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                string result = FERepoResourceBrowser.FindRepoRoot(tempDir);
                Assert.Null(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindRepoRoot_FindsRepoDir()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo");
            Directory.CreateDirectory(repoDir);
            try
            {
                string result = FERepoResourceBrowser.FindRepoRoot(tempDir);
                Assert.Equal(repoDir, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetCategories_ReturnsEmpty_WhenMissing()
        {
            string[] cats = FERepoResourceBrowser.GetCategories("/nonexistent/path");
            Assert.Empty(cats);
        }

        [Fact]
        public void GetCategories_ReturnsDirectories()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(tempDir, "Battle Animations"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Portrait Repository"));
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
            Directory.CreateDirectory(Path.Combine(tempDir, "repo-tools"));
            try
            {
                string[] cats = FERepoResourceBrowser.GetCategories(tempDir);
                Assert.Equal(2, cats.Length);
                Assert.Contains("Battle Animations", cats);
                Assert.Contains("Portrait Repository", cats);
                Assert.DoesNotContain(".git", cats);
                Assert.DoesNotContain("repo-tools", cats);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetSubCategories_ReturnsSubDirs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string catDir = Path.Combine(tempDir, "Portrait Repository");
            Directory.CreateDirectory(Path.Combine(catDir, "FE06, 07 Mugs"));
            Directory.CreateDirectory(Path.Combine(catDir, "FE08 Mugs"));
            try
            {
                string[] subs = FERepoResourceBrowser.GetSubCategories(tempDir, "Portrait Repository");
                Assert.Equal(2, subs.Length);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetResourceFiles_ReturnsPNGFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string catDir = Path.Combine(tempDir, "Portrait Repository", "FE08 Mugs");
            Directory.CreateDirectory(catDir);
            File.WriteAllText(Path.Combine(catDir, "Eirika.png"), "mock");
            File.WriteAllText(Path.Combine(catDir, "Ephraim.png"), "mock");
            File.WriteAllText(Path.Combine(catDir, "readme.txt"), "not an image");
            try
            {
                var files = FERepoResourceBrowser.GetResourceFiles(tempDir, "Portrait Repository", "FE08 Mugs");
                Assert.Equal(2, files.Length);
                Assert.All(files, f => Assert.EndsWith(".png", f.FileName));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetResourceFiles_ReturnsEmpty_ForMissingCategory()
        {
            var files = FERepoResourceBrowser.GetResourceFiles("/nonexistent", "Missing Category");
            Assert.Empty(files);
        }

        [Fact]
        public void GetCategories_WithActualSubmodule()
        {
            // Integration test: verify actual FE-Repo submodule if present and initialized
            string repoRoot = FERepoResourceBrowser.FindRepoRoot(
                CoreState.BaseDirectory ?? System.AppDomain.CurrentDomain.BaseDirectory);
            if (repoRoot == null)
            {
                // Submodule directory not found — skip
                return;
            }

            string[] cats = FERepoResourceBrowser.GetCategories(repoRoot);
            if (cats.Length == 0)
            {
                // Submodule directory exists but not initialized (CI) — skip
                return;
            }

            Assert.Contains("Portrait Repository", cats);
        }
    }
}
