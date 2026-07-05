// SPDX-License-Identifier: GPL-3.0-or-later
// #1813 — ContentRepoGitService: the generic in-app clone-or-update engine shared by patch2, FE-Repo,
// and FE-Repo-Midi. The full clone/backup/restore matrix is already covered by the 12 Patch2GitServiceTests
// (which now exercise this core via the patch2 shim); this file adds the generic-API coverage plus the
// cross-service single-flight exclusion the review board required.
using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    // Serialized with Patch2GitServiceTests: both exercise the shared static single-flight guard in
    // ContentRepoGitService, so running them in parallel races. #1839 review-board finding.
    [Collection("ContentRepoGitGuard")]
    public class ContentRepoGitServiceTests
    {
        static string NewRepoDir(out string baseDir)
        {
            baseDir = Path.Combine(Path.GetTempPath(), "fe_crgit_" + Guid.NewGuid().ToString("N"));
            string repoDir = Path.Combine(baseDir, "resources", "FE-Repo");
            Directory.CreateDirectory(Path.GetDirectoryName(repoDir));
            return repoDir;
        }

        static void Cleanup(string baseDir)
        {
            try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
        }

        sealed class FakeClone
        {
            public int Called; public bool TargetExistedAtCall; public int ReturnCode; public bool CreateOnSuccess;
            public int Op(string g, string u, string t, Action<string> p, StringBuilder l)
            {
                Called++; TargetExistedAtCall = Directory.Exists(t);
                if (ReturnCode == 0 && CreateOnSuccess) { Directory.CreateDirectory(t); File.WriteAllText(Path.Combine(t, "cloned.txt"), "ok"); }
                return ReturnCode;
            }
        }
        sealed class FakeUpdate { public int Called; public int ReturnCode; public int Op(string g, string r, Action<string> p, StringBuilder l, string u) { Called++; return ReturnCode; } }

        [Fact]
        public void GitNotFound_WhenGitExeNull()
        {
            var r = ContentRepoGitService.InitializeOrUpdateCore(
                "any", null, "url", _ => false, new FakeClone().Op, new FakeUpdate().Op, null);
            Assert.Equal(Patch2GitResultKind.GitNotFound, r.Kind);
        }

        [Fact]
        public void EmptyResourceDir_ClonesWithNamedBackupThenNone()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir); // empty submodule placeholder
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = ContentRepoGitService.InitializeOrUpdateCore(
                    repoDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.True(r.WasClone);
                Assert.False(clone.TargetExistedAtCall);                 // moved aside before clone
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                // Backup was named after the repo dir (FE-Repo), not "patch2", and removed on success.
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void ValidSubmoduleLink_TakesUpdatePath_NoBackup()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "content.bin"), "existing"); // populated repo content
                var clone = new FakeClone();
                var update = new FakeUpdate { ReturnCode = 0 };
                // isGitRepo => true simulates a valid .git-file submodule link (however large).
                var r = ContentRepoGitService.InitializeOrUpdateCore(
                    repoDir, "git", "url", _ => true, clone.Op, update.Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.False(r.WasClone);
                Assert.Equal(1, update.Called);
                Assert.Equal(0, clone.Called);                            // never cloned
                Assert.True(File.Exists(Path.Combine(repoDir, "content.bin"))); // not backed up / destroyed
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void SingleFlight_ContentRepoGuardHeld_Patch2ReturnsAlreadyRunning()
        {
            Assert.True(ContentRepoGitService.TryEnter());
            try
            {
                var r = Patch2GitService.InitializeOrUpdate("any", null, null);
                Assert.Equal(Patch2GitResultKind.AlreadyRunning, r.Kind); // one shared guard across services
            }
            finally { ContentRepoGitService.Exit(); }
        }

        [Fact]
        public void SingleFlight_Patch2GuardHeld_ContentRepoReturnsAlreadyRunning()
        {
            Assert.True(Patch2GitService.TryEnter());
            try
            {
                var r = ContentRepoGitService.InitializeOrUpdate("any-dir", "url", null);
                Assert.Equal(Patch2GitResultKind.AlreadyRunning, r.Kind);
            }
            finally { Patch2GitService.Exit(); }
        }
    }
}
