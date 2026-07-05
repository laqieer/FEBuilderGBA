// SPDX-License-Identifier: GPL-3.0-or-later
// #1817 — Patch2GitService: cross-platform in-app patch2 initialize (clone) / update (fetch+reset).
// Deterministic and offline — the git clone/update operations are injected fakes, and each test uses
// an isolated temp baseDir (GUID) so the REAL backup file-move logic is exercised without network,
// without a real git, and without cross-test races.
using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class Patch2GitServiceTests
    {
        static string NewBaseDir()
        {
            string b = Path.Combine(Path.GetTempPath(), "fe_p2git_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(b, "config"));
            return b;
        }

        static void Cleanup(string baseDir)
        {
            try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
        }

        static int BackupCount(string baseDir)
            => Directory.GetDirectories(Path.Combine(baseDir, "config"), "_patch2_backup_*").Length;

        // Fake clone: records whether the target existed at call time and, on "success", creates a
        // populated target directory the way a real git clone would.
        sealed class FakeClone
        {
            public int Called;
            public string LastTarget;
            public bool TargetExistedAtCall;
            public int ReturnCode;
            public bool CreateOnSuccess;

            public int Op(string gitExe, string url, string targetPath, Action<string> progress, StringBuilder log)
            {
                Called++;
                LastTarget = targetPath;
                TargetExistedAtCall = Directory.Exists(targetPath);
                if (ReturnCode == 0 && CreateOnSuccess)
                {
                    Directory.CreateDirectory(targetPath);
                    File.WriteAllText(Path.Combine(targetPath, "cloned.txt"), "ok");
                }
                return ReturnCode;
            }
        }

        sealed class FakeUpdate
        {
            public int Called;
            public int ReturnCode;
            public int Op(string gitExe, string repoPath, Action<string> progress, StringBuilder log, string remoteUrl)
            {
                Called++;
                return ReturnCode;
            }
        }

        [Fact]
        public void GetPatch2Dir_ComposesConfigPatch2()
        {
            Assert.Equal(Path.Combine("X", "config", "patch2"), Patch2GitService.GetPatch2Dir("X"));
        }

        [Fact]
        public void GitNotFound_WhenGitExeNull()
        {
            var r = Patch2GitService.InitializeOrUpdateCore(
                "any", null, "url", _ => false, new FakeClone().Op, new FakeUpdate().Op, null);
            Assert.Equal(Patch2GitResultKind.GitNotFound, r.Kind);
        }

        [Fact]
        public void ExistingRepo_TakesUpdatePath_NoClone()
        {
            string baseDir = NewBaseDir();
            try
            {
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var update = new FakeUpdate { ReturnCode = 0 };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => true, clone.Op, update.Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.False(r.WasClone);
                Assert.Equal(1, update.Called);
                Assert.Equal(0, clone.Called);
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void ExistingRepo_UpdateFailure_ReturnsFailed()
        {
            string baseDir = NewBaseDir();
            try
            {
                var clone = new FakeClone();
                var update = new FakeUpdate { ReturnCode = 128 };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => true, clone.Op, update.Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, r.Kind);
                Assert.Equal(128, r.ExitCode);
                Assert.False(r.WasClone);
                Assert.Equal(0, clone.Called);
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void NonExistentDir_ClonesWithNoBackup()
        {
            string baseDir = NewBaseDir();
            try
            {
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.True(r.WasClone);
                Assert.Equal(1, clone.Called);
                Assert.False(clone.TargetExistedAtCall);   // clone target must not exist when clone runs
                Assert.Equal(Patch2GitService.GetPatch2Dir(baseDir), clone.LastTarget);
                Assert.True(Directory.Exists(Patch2GitService.GetPatch2Dir(baseDir)));
                Assert.Equal(0, BackupCount(baseDir));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PreExistingEmptyDir_MovedAsideBeforeClone_Success()
        {
            string baseDir = NewBaseDir();
            try
            {
                string patchDir = Patch2GitService.GetPatch2Dir(baseDir);
                Directory.CreateDirectory(patchDir);   // empty submodule placeholder (zero entries)
                Assert.Empty(Directory.GetFileSystemEntries(patchDir));

                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.True(r.WasClone);
                Assert.Equal(1, clone.Called);
                Assert.False(clone.TargetExistedAtCall);   // the empty dir was moved aside first
                Assert.True(File.Exists(Path.Combine(patchDir, "cloned.txt")));
                Assert.Equal(0, BackupCount(baseDir));      // backup removed on success
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PreExistingEmptyDir_CloneFails_RestoresAndNoBackupLeft()
        {
            string baseDir = NewBaseDir();
            try
            {
                string patchDir = Patch2GitService.GetPatch2Dir(baseDir);
                Directory.CreateDirectory(patchDir);   // empty placeholder

                var clone = new FakeClone { ReturnCode = 128, CreateOnSuccess = false };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, r.Kind);
                Assert.True(r.WasClone);
                Assert.True(Directory.Exists(patchDir));   // original empty dir restored
                Assert.Equal(0, BackupCount(baseDir));      // no dangling backup
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PreExistingNonRepoFiles_CloneSuccess_ReplacesAndNoBackup()
        {
            string baseDir = NewBaseDir();
            try
            {
                string patchDir = Patch2GitService.GetPatch2Dir(baseDir);
                Directory.CreateDirectory(patchDir);
                File.WriteAllText(Path.Combine(patchDir, "stray.txt"), "old");

                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.False(clone.TargetExistedAtCall);
                Assert.False(File.Exists(Path.Combine(patchDir, "stray.txt")));  // replaced by clone
                Assert.True(File.Exists(Path.Combine(patchDir, "cloned.txt")));
                Assert.Equal(0, BackupCount(baseDir));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PreExistingNonRepoFiles_CloneFails_RestoresMarker()
        {
            string baseDir = NewBaseDir();
            try
            {
                string patchDir = Patch2GitService.GetPatch2Dir(baseDir);
                Directory.CreateDirectory(patchDir);
                File.WriteAllText(Path.Combine(patchDir, "stray.txt"), "old");

                var clone = new FakeClone { ReturnCode = 1, CreateOnSuccess = false };
                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, r.Kind);
                Assert.True(File.Exists(Path.Combine(patchDir, "stray.txt")));  // restored
                Assert.Equal("old", File.ReadAllText(Path.Combine(patchDir, "stray.txt")));
                Assert.Equal(0, BackupCount(baseDir));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PreExistingDir_CloneThrows_RestoresBackupAndReturnsFailed()
        {
            string baseDir = NewBaseDir();
            try
            {
                string patchDir = Patch2GitService.GetPatch2Dir(baseDir);
                Directory.CreateDirectory(patchDir);
                File.WriteAllText(Path.Combine(patchDir, "stray.txt"), "old");

                Patch2GitService.CloneOp throwingClone =
                    (g, u, t, p, l) => throw new InvalidOperationException("boom");

                var r = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => false, throwingClone, new FakeUpdate().Op, null);

                // The clone threw after the move, but no exception escapes and the original dir is restored.
                Assert.Equal(Patch2GitResultKind.Failed, r.Kind);
                Assert.True(r.WasClone);
                Assert.Contains("boom", r.Log);
                Assert.True(File.Exists(Path.Combine(patchDir, "stray.txt")));
                Assert.Equal("old", File.ReadAllText(Path.Combine(patchDir, "stray.txt")));
                Assert.Equal(0, BackupCount(baseDir));   // no dangling backup left behind
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void ExistingRepo_UpdateThrows_ReturnsFailedNoThrow()
        {
            string baseDir = NewBaseDir();
            try
            {
                Patch2GitService.UpdateOp throwingUpdate =
                    (g, r, p, l, u) => throw new InvalidOperationException("kaboom");

                var res = Patch2GitService.InitializeOrUpdateCore(
                    baseDir, "git", "url", _ => true, new FakeClone().Op, throwingUpdate, null);

                Assert.Equal(Patch2GitResultKind.Failed, res.Kind);
                Assert.False(res.WasClone);
                Assert.Contains("kaboom", res.Log);
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void SingleFlight_SecondConcurrentCall_ReturnsAlreadyRunning()
        {
            Assert.True(Patch2GitService.TryEnter());   // simulate an in-progress operation
            try
            {
                var r = Patch2GitService.InitializeOrUpdate("any", null, null);
                Assert.Equal(Patch2GitResultKind.AlreadyRunning, r.Kind);
            }
            finally
            {
                Patch2GitService.Exit();
            }
        }
    }
}
