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
            // #1380 Part A: a populated repo dir (>=1 child directory) is a valid root.
            Directory.CreateDirectory(Path.Combine(repoDir, "Portrait Repository"));
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
        public void FindRepoRoot_ReturnsNull_WhenEmptyPlaceholder()
        {
            // #1380 Part A: an uninitialized submodule leaves an empty-but-existing
            // placeholder dir. It must be treated as not-found so the actionable
            // "run git submodule update" message surfaces.
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo");
            Directory.CreateDirectory(repoDir); // empty placeholder, no children
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
        public void FindMusicRepoRoot_ReturnsNull_WhenEmptyPlaceholder()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo-Music-No-Preview");
            Directory.CreateDirectory(repoDir); // empty placeholder
            try
            {
                Assert.Null(FERepoResourceBrowser.FindMusicRepoRoot(tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindMusicRepoRoot_FindsRepoDir_WhenPopulated()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo-Music-No-Preview");
            Directory.CreateDirectory(Path.Combine(repoDir, "Some Category"));
            try
            {
                Assert.Equal(repoDir, FERepoResourceBrowser.FindMusicRepoRoot(tempDir));
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

        // -----------------------------------------------------------------
        // #1380 Part B — editor-kind -> FE-Repo folder resolver
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.UnitWaitIcon, "Map Sprites", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.UnitMoveIcon, "Map Sprites", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.ItemIcon, "Item Icons", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.GenericEnemyPortrait, "Item Icons", "Special - Generic Minimugs")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.BackgroundImage, "BGs, Interface Elements", "CG Images")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.Portrait, "Portrait Repository", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.ClassCard, "Class Cards", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.BattleAnimation, "Battle Animations", null)]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.SkillIcon, "Item Icons", "Special - Skill Icons")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.SpellAnimation, "Spells n Skills", "7. Spells")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.BattleBackground, "BGs, Interface Elements", "Battle Frames & Backgrounds")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.CGImage, "BGs, Interface Elements", "CG Images")]
        [InlineData(FERepoResourceBrowser.FERepoEditorKind.Tileset, "Tilesets", null)]
        public void GetFERepoFolderForEditor_ReturnsExpectedFolder(
            FERepoResourceBrowser.FERepoEditorKind kind, string category, string subCategory)
        {
            var result = FERepoResourceBrowser.GetFERepoFolderForEditor(kind);
            Assert.True(result.Supported);
            Assert.Equal(category, result.Category);
            Assert.Equal(subCategory, result.SubCategory);
        }

        [Fact]
        public void GetFERepoFolderForEditor_UnknownKind_ReturnsUnsupported()
        {
            // An undefined enum value (cast from an out-of-range int) must map to
            // Unsupported so a caller hides the FE-Repo button rather than seeding
            // a wrong/empty path.
            var result = FERepoResourceBrowser.GetFERepoFolderForEditor(
                (FERepoResourceBrowser.FERepoEditorKind)9999);
            Assert.False(result.Supported);
            Assert.Null(result.Category);
            Assert.Null(result.SubCategory);
        }

        [Fact]
        public void GetFERepoFolderForEditor_EveryDefinedKind_IsSupported()
        {
            // All enum values defined today are real, wired-or-deferred editors
            // with a known folder — none should be Unsupported. Guards against a
            // new enum value being added without a mapping.
            foreach (FERepoResourceBrowser.FERepoEditorKind kind in
                System.Enum.GetValues(typeof(FERepoResourceBrowser.FERepoEditorKind)))
            {
                var result = FERepoResourceBrowser.GetFERepoFolderForEditor(kind);
                Assert.True(result.Supported, $"{kind} should resolve to a folder");
                Assert.False(string.IsNullOrEmpty(result.Category), $"{kind} should have a category");
            }
        }

        // -----------------------------------------------------------------
        // #1383 — IsMusicRepoAvailable: the shared existence guard the Song
        // editors use to show/hide the "FE-Repo-Music" button.
        // -----------------------------------------------------------------

        [Fact]
        public void IsMusicRepoAvailable_True_WhenPopulated()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo-Music-No-Preview");
            Directory.CreateDirectory(Path.Combine(repoDir, "Battle Themes"));
            try
            {
                Assert.True(FERepoResourceBrowser.IsMusicRepoAvailable(tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsMusicRepoAvailable_False_WhenEmptyPlaceholder()
        {
            // #1380 reuse: an uninitialized music submodule leaves an empty
            // placeholder dir that must be treated as not-available so the
            // button stays hidden.
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string repoDir = Path.Combine(tempDir, "resources", "FE-Repo-Music-No-Preview");
            Directory.CreateDirectory(repoDir); // empty placeholder
            try
            {
                Assert.False(FERepoResourceBrowser.IsMusicRepoAvailable(tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsMusicRepoAvailable_False_WhenAbsent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir); // no resources/ at all
            try
            {
                Assert.False(FERepoResourceBrowser.IsMusicRepoAvailable(tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsMusicRepoAvailable_False_WhenBaseDirNull()
        {
            Assert.False(FERepoResourceBrowser.IsMusicRepoAvailable(null));
        }
    }
}
