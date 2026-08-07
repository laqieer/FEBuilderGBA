// SPDX-License-Identifier: GPL-3.0-or-later
// #1813 — ContentRepoGitService: the generic in-app clone-or-update engine shared by patch2, FE-Repo,
// and FE-Repo-Music. The full clone/backup/restore matrix is already covered by the 12 Patch2GitServiceTests
// (which now exercise this core via the patch2 shim); this file adds the generic-API coverage plus the
// cross-service single-flight exclusion the review board required.
//
// #2036 — the placeholder ("released empty stub") contract is proven here end to end:
//   * an empty stub root is HELD: never deleted, never moved; git clones into it in place, and a clone
//     failure clears the clone-written artifacts (including nested ones whose names collide with stub
//     directories, e.g. FE6/file) and restores the exact empty directory shape;
//   * an absent target stays absent, even when the clone wrote a partial tree before failing;
//   * a nonempty/uncertain target is backed up and then restored;
//   * every filesystem step is postcondition-driven and bounded: move = source absent AND destination
//     present, delete = target absent, a completed-then-threw operation is accepted, and a normal
//     return whose postcondition is unmet is retried and then reported as a failure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Directory.CreateDirectory(TestRequire.DirectoryName(repoDir));
            return repoDir;
        }

        static void Cleanup(string baseDir)
        {
            try { if (Directory.Exists(baseDir)) ClearReadOnly(baseDir); } catch { }
            try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
        }

        static void ClearReadOnly(string path)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0) ClearReadOnly(entry);
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }
        }

        static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
            catch (PlatformNotSupportedException) { return false; }
        }

        // Relative directory shape of a tree, order-independent, so "exact empty stub shape" is an
        // exact set comparison instead of a spot check.
        static string[] RelativeDirectories(string root)
            => Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Select(d => Path.GetRelativePath(root, d))
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToArray();

        static string[] Sorted(params string[] values)
            => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();

        sealed class FakeClone
        {
            public int Called; public bool TargetExistedAtCall; public int ReturnCode; public bool CreateOnSuccess;
            /// <summary>Artifacts a real git clone would have written into the target before failing.</summary>
            public Action<string>? Artifacts;
            public int Op(string g, string u, string t, Action<string>? p, StringBuilder l)
            {
                Called++; TargetExistedAtCall = Directory.Exists(t);
                Artifacts?.Invoke(t);
                if (ReturnCode == 0 && CreateOnSuccess) { Directory.CreateDirectory(t); File.WriteAllText(Path.Combine(t, "cloned.txt"), "ok"); }
                return ReturnCode;
            }
        }
        sealed class FakeUpdate { public int Called; public int ReturnCode; public int Op(string g, string r, Action<string>? p, StringBuilder l, string u) { Called++; return ReturnCode; } }

        sealed class RecordingDirectoryOps : ContentRepoDirectoryOps
        {
            /// <summary>Set so a restore move (destination == repo dir) can be told from a backup move.</summary>
            public string RepoDir = "";
            public int Delays;
            public bool ReturnFalseMove;
            public bool ThrowMoveAfterSuccess;
            /// <summary>Reports success without moving anything — the postcondition must reject it.</summary>
            public bool MoveWithoutEffect;
            public bool DenyEveryMove;
            public int ThrowBackupMoveTimes;
            public int ThrowRestoreMoveTimes;
            public int ThrowDeleteTimes;
            public int ThrowDeleteFileTimes;
            public bool DenyEveryFileDelete;
            public bool ThrowDeleteAfterSuccess;
            public int MoveCalls;
            public bool ThrowOnEnumeration;
            public int EnumerationCalls;
            public bool LastMoveSourceExisted = true;
            public bool LastMoveDestinationExisted;
            public readonly List<string> MoveRequests = new List<string>();
            public readonly List<string> DirectoryDeleteRequests = new List<string>();
            public readonly List<string> FileDeleteRequests = new List<string>();

            public IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                EnumerationCalls++;
                if (ThrowOnEnumeration)
                    throw new IOException("enumeration denied: " + path);
                return Directory.EnumerateFileSystemEntries(path).ToArray();
            }

            public bool MoveDirectory(string source, string destination)
            {
                MoveCalls++;
                MoveRequests.Add(source + " -> " + destination);
                if (ReturnFalseMove) return false;
                if (MoveWithoutEffect) return true;
                if (DenyEveryMove) throw new UnauthorizedAccessException("move denied: " + source);
                bool restore = string.Equals(destination, RepoDir, StringComparison.OrdinalIgnoreCase);
                if (restore)
                {
                    if (ThrowRestoreMoveTimes-- > 0) throw new IOException("transient restore move");
                }
                else if (ThrowBackupMoveTimes-- > 0) throw new IOException("transient backup move");
                Directory.Move(source, destination);
                if (ThrowMoveAfterSuccess)
                {
                    // Completed-then-threw: record exactly what the postcondition will observe.
                    LastMoveSourceExisted = Directory.Exists(source);
                    LastMoveDestinationExisted = Directory.Exists(destination);
                    throw new IOException("move completed");
                }
                return true;
            }
            public bool DeleteDirectory(string path, bool recursive)
            {
                DirectoryDeleteRequests.Add(path);
                if (ThrowDeleteTimes-- > 0) throw new IOException("transient delete");
                Directory.Delete(path, recursive);
                if (ThrowDeleteAfterSuccess) throw new IOException("delete completed");
                return true;
            }
            public bool DeleteFile(string path)
            {
                FileDeleteRequests.Add(path);
                if (DenyEveryFileDelete) throw new UnauthorizedAccessException("file delete denied: " + path);
                if (ThrowDeleteFileTimes-- > 0) throw new IOException("transient file delete");
                File.Delete(path);
                return true;
            }
            public void Delay(int milliseconds) { Delays++; }
        }

        [Fact]
        public void GitNotFound_WhenGitExeNull()
        {
            var r = ContentRepoGitService.InitializeOrUpdateCore(
                "any", null!, "url", _ => false, new FakeClone().Op, new FakeUpdate().Op, null);
            Assert.Equal(Patch2GitResultKind.GitNotFound, r.Kind);
        }

        [Fact]
        public void EmptyResourceDir_ClonesInPlaceWithNoBackup()
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
                Assert.True(clone.TargetExistedAtCall);                  // released empty stubs clone in place
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                // Placeholder roots are preserved; no backup is created for an empty tree.
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
        public void RecursiveEmptyStub_CloneFailure_RestoresExactDirectoryTreeInPlace()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE8U", "SYSTEM"));
                Directory.CreateDirectory(Path.Combine(repoDir, "FE7U"));
                var clone = new FakeClone { ReturnCode = 128 };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.True(clone.TargetExistedAtCall);
                Assert.True(Directory.Exists(Path.Combine(repoDir, "FE8U", "SYSTEM")));
                Assert.True(Directory.Exists(Path.Combine(repoDir, "FE7U")));
                Assert.Empty(Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PlaceholderRollback_EnumerationFailure_LogsRetainedRootWithoutDeletingIt()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                var ops = new RecordingDirectoryOps { RepoDir = repoDir };
                Patch2GitService.CloneOp clone = (g, u, t, p, l) =>
                {
                    File.WriteAllText(Path.Combine(t, "partial.txt"), "partial");
                    ops.ThrowOnEnumeration = true;
                    return 1;
                };

                var result = ContentRepoGitService.InitializeOrUpdateCore(
                    repoDir, "git", "url", _ => false, clone, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.True(Directory.Exists(repoDir));
                Assert.True(File.Exists(Path.Combine(repoDir, "partial.txt")));
                Assert.Contains("enumeration denied", result.Log);
                Assert.Contains("Retained path: " + repoDir, result.Log);
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void FalseMove_RetriesFiveTimes_AndNeverStartsClone()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "content"), "x");
                var ops = new RecordingDirectoryOps { ReturnFalseMove = true };
                var clone = new FakeClone();
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(4, ops.Delays);
                Assert.Equal(0, clone.Called);
                Assert.True(File.Exists(Path.Combine(repoDir, "content")));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void AbsentTarget_CloneFailure_StaysAbsent()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    new FakeClone { ReturnCode = 1 }.Op, new FakeUpdate().Op, null);
                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.False(Directory.Exists(repoDir));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void TransientStubChildDelete_RetriesThenClonesInPlace()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE8U"));
                var ops = new RecordingDirectoryOps { ThrowDeleteTimes = 1 };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.Equal(1, ops.Delays);
                Assert.True(clone.TargetExistedAtCall);
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void CompletedThenThrewBackupMove_IsAcceptedByPostcondition()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "old.txt"), "old");
                var ops = new RecordingDirectoryOps { ThrowMoveAfterSuccess = true };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                Assert.Equal(0, ops.Delays);
            }
            finally { Cleanup(baseDir); }
        }

        // (b) Empty root: the clone happens in place and the root object itself is never deleted or moved.
        [Fact]
        public void EmptyRoot_CloneSuccess_RootIsHeldAndItsAttributesPreserved()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                FileAttributes attributes = File.GetAttributes(repoDir);

                var ops = new RecordingDirectoryOps { RepoDir = repoDir };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.True(clone.TargetExistedAtCall);
                Assert.Empty(ops.MoveRequests);                          // the held root is never moved
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
                Assert.Equal(attributes, File.GetAttributes(repoDir));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (c) Recursively empty stubs (FE6/FE7U/...) are cleared deepest-first and cloned in place.
        [Fact]
        public void RecursivelyEmptyStubs_CloneInPlace_ChildrenClearedDeepestFirst_NoRootMove()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                Directory.CreateDirectory(Path.Combine(repoDir, "FE7U", "SYSTEM"));

                var ops = new RecordingDirectoryOps { RepoDir = repoDir };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var r = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, r.Kind);
                Assert.True(clone.TargetExistedAtCall);
                Assert.Empty(ops.MoveRequests);
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
                int nested = ops.DirectoryDeleteRequests.IndexOf(Path.Combine(repoDir, "FE7U", "SYSTEM"));
                int parent = ops.DirectoryDeleteRequests.IndexOf(Path.Combine(repoDir, "FE7U"));
                Assert.True(nested >= 0 && parent > nested, "stub children must be deleted deepest-first");
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
            }
            finally { Cleanup(baseDir); }
        }

        // (d) A failed clone can leave files nested inside stub-named directories (FE6/file) plus its own
        // .git scaffolding; the exact empty snapshot shape must come back and the root must stay put.
        [Fact]
        public void EmptyStub_CloneFailureWritesNestedArtifacts_RestoresExactShape_RootHeld()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                Directory.CreateDirectory(Path.Combine(repoDir, "FE7U", "SYSTEM"));

                var ops = new RecordingDirectoryOps { RepoDir = repoDir };
                var clone = new FakeClone
                {
                    ReturnCode = 128,
                    Artifacts = target =>
                    {
                        Directory.CreateDirectory(Path.Combine(target, "FE6"));
                        File.WriteAllText(Path.Combine(target, "FE6", "file"), "partial");
                        Directory.CreateDirectory(Path.Combine(target, ".git", "objects"));
                        File.WriteAllText(Path.Combine(target, ".git", "config"), "[core]");
                        File.WriteAllText(Path.Combine(target, "README.md"), "partial");
                    },
                };

                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.True(Directory.Exists(repoDir));
                Assert.Empty(ops.MoveRequests);
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
                Assert.Equal(Sorted("FE6", "FE7U", Path.Combine("FE7U", "SYSTEM")), RelativeDirectories(repoDir));
                Assert.Empty(Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories));
            }
            finally { Cleanup(baseDir); }
        }

        // (a) An absent target whose clone wrote a partial tree before failing must be removed entirely.
        [Fact]
        public void AbsentTarget_PartialCloneWritten_ThenFails_TargetRemovedAndStaysAbsent()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                var clone = new FakeClone
                {
                    ReturnCode = 128,
                    Artifacts = target =>
                    {
                        Directory.CreateDirectory(Path.Combine(target, ".git", "objects"));
                        File.WriteAllText(Path.Combine(target, ".git", "config"), "[core]");
                        File.WriteAllText(Path.Combine(target, "partial.bin"), "half");
                    },
                };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.False(clone.TargetExistedAtCall);
                Assert.False(Directory.Exists(repoDir));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (e) Transient failures on BOTH the stub-child directory delete and the restore-time file delete
        // are retried, and the exact stub shape still comes back.
        [Fact]
        public void TransientChildDirectoryAndFileCleanup_AreRetried_ShapeRestored()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                var ops = new RecordingDirectoryOps
                {
                    RepoDir = repoDir,
                    ThrowDeleteTimes = 1,       // transient stub-child directory delete
                    ThrowDeleteFileTimes = 1,   // transient clone-artifact file delete
                };
                var clone = new FakeClone
                {
                    ReturnCode = 1,
                    Artifacts = target =>
                    {
                        Directory.CreateDirectory(Path.Combine(target, "FE6"));
                        File.WriteAllText(Path.Combine(target, "FE6", "file"), "partial");
                    },
                };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(2, ops.Delays);                  // exactly one delay per transient failure
                Assert.True(Directory.Exists(repoDir));
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
                Assert.Equal(Sorted("FE6"), RelativeDirectories(repoDir));
                Assert.Empty(Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories));
            }
            finally { Cleanup(baseDir); }
        }

        // (f) A transient backup move is retried, and the clone then runs against the freed path.
        [Fact]
        public void TransientNonEmptyBackupMove_RetriesThenClones()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "stray.txt"), "old");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, ThrowBackupMoveTimes = 2 };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.Equal(2, ops.Delays);
                Assert.Equal(1, clone.Called);
                Assert.False(clone.TargetExistedAtCall);      // nonempty originals are moved aside first
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                Assert.False(File.Exists(Path.Combine(repoDir, "stray.txt")));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (g) The accepted completed-then-threw move is exactly the one whose SOURCE is gone and whose
        // DESTINATION exists — a half-applied move must never be accepted.
        [Fact]
        public void CompletedThenThrewMove_IsAcceptedOnlyWithSourceAbsentAndDestinationPresent()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "old.txt"), "old");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, ThrowMoveAfterSuccess = true };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.Equal(1, ops.MoveCalls);                  // accepted on the first attempt
                Assert.False(ops.LastMoveSourceExisted);
                Assert.True(ops.LastMoveDestinationExisted);
                Assert.Equal(0, ops.Delays);
            }
            finally { Cleanup(baseDir); }
        }

        // (h) The success-path backup cleanup delete that completed and then threw is accepted too.
        [Fact]
        public void CompletedThenThrewBackupCleanupDelete_IsAccepted_NoDanglingBackup()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "stray.txt"), "old");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, ThrowDeleteAfterSuccess = true };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.Equal(0, ops.Delays);
                Assert.DoesNotContain("Retained path:", result.Log);
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void SuccessfulClone_BackupCleanupExhaustion_ReturnsSuccessAndLogsRetainedBackup()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "stray.txt"), "old");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, ThrowDeleteTimes = 10 };
                var clone = new FakeClone { ReturnCode = 0, CreateOnSuccess = true };

                var result = ContentRepoGitService.InitializeOrUpdateCore(
                    repoDir, "git", "url", _ => false, clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Success, result.Kind);
                Assert.Contains("Retained path:", result.Log);
                Assert.Equal(4, ops.Delays);
                Assert.True(File.Exists(Path.Combine(repoDir, "cloned.txt")));
                Assert.Single(Directory.GetDirectories(
                    Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (i) A permanently denied backup move is bounded, never starts a clone, and leaves the original.
        [Fact]
        public void PermanentMoveDenial_IsBounded_NoCloneAndOriginalIntact()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "content"), "keep");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, DenyEveryMove = true };
                var clone = new FakeClone();
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(5, ops.MoveCalls);                  // bounded: 1 attempt + 4 retries
                Assert.Equal(4, ops.Delays);
                Assert.Equal(0, clone.Called);
                Assert.Equal("keep", File.ReadAllText(Path.Combine(repoDir, "content")));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (j) A transient restore move is retried until the original content is back in place.
        [Fact]
        public void TransientRestoreMove_IsRetried_OriginalRestored()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "stray.txt"), "old");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, ThrowRestoreMoveTimes = 2 };
                var clone = new FakeClone
                {
                    ReturnCode = 1,
                    Artifacts = target =>
                    {
                        Directory.CreateDirectory(target);
                        File.WriteAllText(Path.Combine(target, "partial.bin"), "half");
                    },
                };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(2, ops.Delays);
                Assert.Equal("old", File.ReadAllText(Path.Combine(repoDir, "stray.txt")));
                Assert.False(File.Exists(Path.Combine(repoDir, "partial.bin")));
                Assert.DoesNotContain("Retained path:", result.Log);
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (k) A permanently undeletable clone artifact is bounded and its path is logged as retained,
        // and the root is still held. The shape is not recreated through retained content.
        [Fact]
        public void PermanentCleanupFailure_IsBounded_LogsRetainedPath_RootHeld()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                string stray = Path.Combine(repoDir, "stray.bin");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, DenyEveryFileDelete = true };
                var clone = new FakeClone
                {
                    ReturnCode = 1,
                    Artifacts = target => File.WriteAllText(Path.Combine(target, "stray.bin"), "partial"),
                };
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(4, ops.Delays);                       // bounded retry, then give up honestly
                Assert.Contains("Retained path: " + stray, result.Log);
                Assert.True(Directory.Exists(repoDir));            // root is still held
                Assert.DoesNotContain(repoDir, ops.DirectoryDeleteRequests);
                Assert.False(Directory.Exists(Path.Combine(repoDir, "FE6"))); // fail closed: no recreation through retained content
                Assert.True(File.Exists(stray));                   // retained, not silently lost
            }
            finally { Cleanup(baseDir); }
        }

        // (l) A move that reports success without moving anything is rejected by the postcondition,
        // retried, and finally reported as a failure — the original target stays untouched.
        [Fact]
        public void MoveReportsSuccessButPostconditionFalse_RetriesThenFails()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                File.WriteAllText(Path.Combine(repoDir, "content"), "x");
                var ops = new RecordingDirectoryOps { RepoDir = repoDir, MoveWithoutEffect = true };
                var clone = new FakeClone();
                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null, ops);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Equal(5, ops.MoveCalls);                  // bounded: 1 attempt + 4 retries
                Assert.Equal(4, ops.Delays);
                Assert.Equal(0, clone.Called);
                Assert.Contains("did not reach its postcondition", result.Log);
                Assert.True(File.Exists(Path.Combine(repoDir, "content")));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "resources"), "_FE-Repo_backup_*"));
            }
            finally { Cleanup(baseDir); }
        }

        // (m) Real filesystem ops: a read-only nested partial clone inside a held stub root is cleaned up
        // (attributes cleared) and the exact empty shape is restored.
        [Fact]
        public void ReadOnlyNestedPartialClone_IsCleanedUp_WithRealFilesystemOps()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                Directory.CreateDirectory(Path.Combine(repoDir, "FE8U", "SYSTEM"));

                var clone = new FakeClone
                {
                    ReturnCode = 128,
                    Artifacts = target =>
                    {
                        string nested = Path.Combine(target, "FE6", "locked");
                        Directory.CreateDirectory(nested);
                        string file = Path.Combine(nested, "pack.idx");
                        File.WriteAllText(file, "partial");
                        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
                    },
                };

                var result = ContentRepoGitService.InitializeOrUpdateCore(repoDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.True(Directory.Exists(repoDir));
                Assert.Equal(Sorted("FE6", "FE8U", Path.Combine("FE8U", "SYSTEM")), RelativeDirectories(repoDir));
                Assert.Empty(Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories));
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void ReparseDirectoryDelete_RemovesLinkWithoutTouchingExternalTarget()
        {
            string repoDir = NewRepoDir(out string baseDir);
            try
            {
                Directory.CreateDirectory(repoDir);
                string external = Path.Combine(baseDir, "external-target");
                Directory.CreateDirectory(external);
                string marker = Path.Combine(external, "marker.txt");
                File.WriteAllText(marker, "keep");
                File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);

                string link = Path.Combine(repoDir, "linked-directory");
                if (!TryCreateDirectorySymbolicLink(link, external)) return;
                var ops = new RealContentRepoDirectoryOps();

                Assert.True(ops.DeleteDirectory(link, recursive: true));
                Assert.False(Directory.Exists(link));
                Assert.True(File.Exists(marker));
                Assert.Equal("keep", File.ReadAllText(marker));
                Assert.True((File.GetAttributes(marker) & FileAttributes.ReadOnly) != 0,
                    "deleting the link must not clear attributes on its external target");
            }
            finally { Cleanup(baseDir); }
        }

        [Fact]
        public void PlaceholderRollback_RootReplacedBySymlink_DoesNotTouchExternalTarget()
        {
            string repoDir = NewRepoDir(out string baseDir);
            string externalBase = Path.Combine(
                Path.GetTempPath(), "fe_crgit_external_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(repoDir, "FE6"));
                Directory.CreateDirectory(externalBase);
                string marker = Path.Combine(externalBase, "marker.txt");
                File.WriteAllText(marker, "keep");
                File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);
                string capabilityLink = Path.Combine(baseDir, "symlink-capability");
                if (!TryCreateDirectorySymbolicLink(capabilityLink, externalBase)) return;
                Directory.Delete(capabilityLink);

                Patch2GitService.CloneOp clone = (g, u, target, p, l) =>
                {
                    Directory.Delete(target);
                    Assert.True(TryCreateDirectorySymbolicLink(target, externalBase));
                    return 1;
                };

                var result = ContentRepoGitService.InitializeOrUpdateCore(
                    repoDir, "git", "url", _ => false, clone, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.Contains("Rollback root is not a normal directory", result.Log);
                Assert.Contains("Retained path: " + repoDir, result.Log);
                Assert.True(File.Exists(marker));
                Assert.Equal("keep", File.ReadAllText(marker));
                Assert.True((File.GetAttributes(marker) & FileAttributes.ReadOnly) != 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(repoDir)
                        && (File.GetAttributes(repoDir) & FileAttributes.ReparsePoint) != 0)
                        Directory.Delete(repoDir);
                }
                catch { }
                Cleanup(baseDir);
                Cleanup(externalBase);
            }
        }

        // (n) The SHIPPED patch2 stub shape (config/patch2/{FE6,FE7J,FE7U,FE8J,FE8U}, all empty) clones in
        // place through the patch2 facade and survives a clone failure unchanged.
        [Fact]
        public void ShippedPatch2StubShape_ClonesInPlace_AndSurvivesCloneFailure_ThroughPatch2Facade()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "fe_crgit_" + Guid.NewGuid().ToString("N"));
            try
            {
                string patch2Dir = Patch2GitService.GetPatch2Dir(baseDir);
                string[] versions = { "FE6", "FE7J", "FE7U", "FE8J", "FE8U" };
                foreach (string v in versions)
                    Directory.CreateDirectory(Path.Combine(patch2Dir, v));

                var clone = new FakeClone
                {
                    ReturnCode = 128,
                    Artifacts = target =>
                    {
                        Directory.CreateDirectory(Path.Combine(target, "FE6"));
                        File.WriteAllText(Path.Combine(target, "FE6", "file"), "partial");
                        Directory.CreateDirectory(Path.Combine(target, ".git"));
                        File.WriteAllText(Path.Combine(target, ".git", "config"), "[core]");
                    },
                };

                var result = Patch2GitService.InitializeOrUpdateCore(baseDir, "git", "url", _ => false,
                    clone.Op, new FakeUpdate().Op, null);

                Assert.Equal(Patch2GitResultKind.Failed, result.Kind);
                Assert.True(clone.TargetExistedAtCall);            // the shipped stub is cloned into in place
                Assert.True(Directory.Exists(patch2Dir));
                Assert.Equal(Sorted(versions), RelativeDirectories(patch2Dir));
                Assert.Empty(Directory.GetFiles(patch2Dir, "*", SearchOption.AllDirectories));
                Assert.Empty(Directory.GetDirectories(Path.Combine(baseDir, "config"), "_patch2_backup_*"));
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
