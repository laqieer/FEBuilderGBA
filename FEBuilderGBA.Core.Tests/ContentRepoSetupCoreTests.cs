using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ContentRepoSetupCoreTests : IDisposable
    {
        readonly string _baseDir;
        readonly Config _cfg;

        public ContentRepoSetupCoreTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_ContentRepoSetupCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _cfg = new Config();
            _cfg.Load(Path.Combine(_baseDir, "config.xml"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        static ContentRepoDescriptor Repo(string id) => ContentRepoSetupCore.Repos.Single(r => r.Id == id);

        [Fact]
        public void ResolveUrl_UsesConfiguredValueOrDefault()
        {
            var patch2 = Repo("patch2");
            Assert.Equal(GitUtil.Patch2RemoteUrl, ContentRepoSetupCore.ResolveUrl(patch2, _cfg));
            _cfg[patch2.ConfigKey] = "https://example.invalid/patch2.git";
            Assert.Equal("https://example.invalid/patch2.git", ContentRepoSetupCore.ResolveUrl(patch2, _cfg));
        }

        [Fact]
        public void ResolveDir_UsesCanonicalHelpers()
        {
            Assert.Equal(Patch2GitService.GetPatch2Dir(_baseDir), ContentRepoSetupCore.ResolveDir(Repo("patch2"), _baseDir));
            Assert.Equal(GitUtil.GetFERepoDir(_baseDir), ContentRepoSetupCore.ResolveDir(Repo("fe-repo"), _baseDir));
            Assert.Equal(GitUtil.GetFERepoMusicDir(_baseDir), ContentRepoSetupCore.ResolveDir(Repo("fe-repo-music"), _baseDir));
        }

        [Fact]
        public void Patch2_EmptyVersionSubdirs_AreNotReady()
        {
            string patch2 = ContentRepoSetupCore.ResolveDir(Repo("patch2"), _baseDir);
            foreach (string v in new[] { "FE6", "FE7J", "FE7U", "FE8J", "FE8U" })
                Directory.CreateDirectory(Path.Combine(patch2, v));

            Assert.False(ContentRepoSetupCore.IsRepoReady(Repo("patch2"), _baseDir));
        }

        [Fact]
        public void Patch2_WithRealPatchFile_IsReady()
        {
            string dir = Path.Combine(ContentRepoSetupCore.ResolveDir(Repo("patch2"), _baseDir), "FE8U", "SYSTEM");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "PATCH_TEST.txt"), "NAME=Test");

            Assert.True(ContentRepoSetupCore.IsRepoReady(Repo("patch2"), _baseDir));
        }

        [Fact]
        public void FERepo_EmptyDirectoryIsNotReady_ButManualPopulatedDirectoryIsReady()
        {
            var repo = Repo("fe-repo");
            string dir = ContentRepoSetupCore.ResolveDir(repo, _baseDir);
            Directory.CreateDirectory(dir);
            Assert.False(ContentRepoSetupCore.IsRepoReady(repo, _baseDir));

            File.WriteAllText(Path.Combine(dir, "readme.txt"), "manual download");
            Assert.True(ContentRepoSetupCore.IsRepoReady(repo, _baseDir));
        }

        [Fact]
        public void FERepoMusic_ManualPopulatedDirectoryWithoutGitIsReady()
        {
            var repo = Repo("fe-repo-music");
            string dir = ContentRepoSetupCore.ResolveDir(repo, _baseDir);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Music"));
            File.WriteAllText(Path.Combine(dir, "Music", "song.mid"), "x");

            Assert.True(ContentRepoSetupCore.IsRepoReady(repo, _baseDir));
        }

        [Fact]
        public void NeedsSetup_ReflectsAnyMissingRepo()
        {
            CreateReadyPatch2();
            CreateReadyFile(Repo("fe-repo"));
            Assert.True(ContentRepoSetupCore.NeedsSetup(_baseDir, _cfg));

            CreateReadyFile(Repo("fe-repo-music"));
            Assert.False(ContentRepoSetupCore.NeedsSetup(_baseDir, _cfg));
        }

        [Fact]
        public void ShouldAutoShow_ReShowsWhileEmpty_UnlessExplicitOptOut()
        {
            Assert.True(ContentRepoSetupCore.ShouldAutoShow(_baseDir, _cfg));
            Assert.Equal("0", _cfg.at(ContentRepoSetupCore.OptOutConfigKey, "0"));

            // Skip/Close intentionally does not mutate config; with empty repos it shows again.
            Assert.True(ContentRepoSetupCore.ShouldAutoShow(_baseDir, _cfg));

            ContentRepoSetupCore.SetOptOut(_cfg);
            Assert.Equal("1", _cfg.at(ContentRepoSetupCore.OptOutConfigKey, "0"));
            Assert.False(ContentRepoSetupCore.ShouldAutoShow(_baseDir, _cfg));
        }

        [Fact]
        public void ReadinessNeverThrowsOnBadPath()
        {
            string badBase = Path.Combine(_baseDir, "file-as-base");
            File.WriteAllText(badBase, "not a directory");
            Assert.False(ContentRepoSetupCore.IsRepoReady(Repo("fe-repo"), badBase));
            Assert.True(ContentRepoSetupCore.NeedsSetup(badBase, _cfg));
        }

        [Fact]
        public void FeRepo_WithOnlyGitLink_IsNotReady()
        {
            // An UNINITIALIZED git submodule working tree contains only a ".git" gitdir-link
            // file — that must NOT count as "ready" (else the wizard never offers to clone it).
            var feRepo = Repo("fe-repo");
            string dir = ContentRepoSetupCore.ResolveDir(feRepo, _baseDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: ../../.git/modules/resources/FE-Repo");
            Assert.False(ContentRepoSetupCore.IsRepoReady(feRepo, _baseDir));

            // Real content alongside (or without) a .git -> ready (manual ZIP extract works too).
            File.WriteAllText(Path.Combine(dir, "portrait.png"), "x");
            Assert.True(ContentRepoSetupCore.IsRepoReady(feRepo, _baseDir));
        }

        void CreateReadyPatch2()
        {
            string dir = Path.Combine(ContentRepoSetupCore.ResolveDir(Repo("patch2"), _baseDir), "FE8U");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "PATCH_READY.txt"), "NAME=Ready");
        }

        void CreateReadyFile(ContentRepoDescriptor repo)
        {
            string dir = ContentRepoSetupCore.ResolveDir(repo, _baseDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "content.txt"), "ready");
        }
    }
}
