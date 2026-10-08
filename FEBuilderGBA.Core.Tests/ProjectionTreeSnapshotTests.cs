// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class ProjectionTreeSnapshotTests : IDisposable
    {
        readonly string tempDirectory;

        public ProjectionTreeSnapshotTests()
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "febuilder-projection-snapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }

        [Fact]
        public void Capture_ReadsNestedRegularFiles()
        {
            string nested = Path.Combine(tempDirectory, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(
                Path.Combine(tempDirectory, "root.txt"),
                "root",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(nested, "caf\u00E9.txt"),
                "nested",
                new UTF8Encoding(false));

            ProjectionTreeSnapshot snapshot = ProjectionTreeSnapshotReader.Capture(
                tempDirectory,
                maxEntries: 10,
                maxBytes: 1024,
                maxTextFileBytes: 1024,
                beforeFileOpen: null!);

            Assert.Equal(new[] { "nested" }, snapshot.Directories);
            Assert.Equal(
                new[] { "nested/caf\u00E9.txt", "root.txt" },
                snapshot.Files.Select(file => file.RelativePath));
            Assert.Equal(
                new[] { "nested", "root" },
                snapshot.Files.Select(file => Encoding.UTF8.GetString(file.Data)));
        }

        [Fact]
        public void IdentityBoundCapture_RejectsHardLinkedPackageFile()
        {
            string packageFile = Path.Combine(tempDirectory, "payload.bin");
            string alias = Path.Combine(
                Path.GetDirectoryName(tempDirectory)!,
                "projection-hardlink-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(packageFile, new byte[] { 1, 2, 3 });
            Assert.True(CreateHardLink(alias, packageFile));
            try
            {
                IOException error = Assert.Throws<IOException>(() =>
                    ProjectionTreeSnapshotReader.CaptureIdentityBound(
                        tempDirectory,
                        maxEntries: 4,
                        maxBytes: 1024,
                        maxTextFileBytes: 1024,
                        beforeFileOpen: null!,
                        disallowedFileIdentity: null));
                Assert.Contains("Hard-linked", error.Message);

                // The additive package policy does not narrow legacy callers.
                Assert.Single(ProjectionTreeSnapshotReader.Capture(
                    tempDirectory,
                    maxEntries: 4,
                    maxBytes: 1024,
                    maxTextFileBytes: 1024,
                    beforeFileOpen: null!).Files);
            }
            finally
            {
                try { File.Delete(alias); }
                catch { }
            }
        }

        [Fact]
        public void IdentityBoundCapture_RejectsExternalReportFileAlias()
        {
            string packageFile =
                Path.Combine(tempDirectory, "package-report.json");
            File.WriteAllText(packageFile, "{}\n");
            using FileStream held =
                ProjectionFileSystemSafety.OpenRegularFileForRead(packageFile);
            _ = ProjectionFileSystemSafety
                .ReadOpenedRegularFileBoundedStable(
                    held,
                    maxBytes: 1024,
                    label: "external report",
                    rejectHardLinks: true,
                    out ProjectionFileSystemSafety
                        .OpenedRegularFileState reportIdentity);

            IOException error = Assert.Throws<IOException>(() =>
                ProjectionTreeSnapshotReader.CaptureIdentityBound(
                    tempDirectory,
                    maxEntries: 4,
                    maxBytes: 1024,
                    maxTextFileBytes: 1024,
                    beforeFileOpen: null!,
                    disallowedFileIdentity: reportIdentity));
            Assert.Contains("aliases the external report", error.Message);
        }

        [Fact]
        public void StableOpenedRead_UsesHeldIdentityAcrossPathReplacement()
        {
            string path = Path.Combine(tempDirectory, "report.json");
            string moved = Path.Combine(tempDirectory, "opened-report.json");
            byte[] original = Encoding.UTF8.GetBytes("original");
            File.WriteAllBytes(path, original);
            using FileStream stream =
                ProjectionFileSystemSafety.OpenRegularFileForRead(path);

            if (OperatingSystem.IsWindows())
            {
                // Windows denies delete sharing on the production handle, so
                // replacement itself must fail while that exact report is held.
                Assert.ThrowsAny<IOException>(() =>
                    ProjectionFileSystemSafety
                        .ReadOpenedRegularFileBoundedStable(
                            stream,
                            maxBytes: 1024,
                            label: "external report",
                            rejectHardLinks: true,
                            out _,
                            afterInspection: () =>
                                File.Move(path, moved)));
                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.False(File.Exists(moved));
                return;
            }

            byte[] read =
                ProjectionFileSystemSafety
                    .ReadOpenedRegularFileBoundedStable(
                        stream,
                        maxBytes: 1024,
                        label: "external report",
                        rejectHardLinks: true,
                        out _,
                        afterInspection: () =>
                        {
                            File.Move(path, moved);
                            File.WriteAllText(
                                path,
                                "replacement",
                                new UTF8Encoding(false));
                        });

            Assert.Equal(original, read);
            Assert.Equal("replacement", File.ReadAllText(path));
        }

        [Fact]
        public void IdentityBoundCleanup_ReportsReplacementAtReservedPath()
        {
            string stage = Path.Combine(tempDirectory, "stage");
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "owned.txt"), "owned");
            FileSystemEntryIdentity identity =
                ProjectionFileSystemSafety
                    .CaptureExistingFileSystemEntryIdentity(stage);
            string replacement = Path.Combine(stage, "replacement.txt");

            bool cleaned = ProjectionFileSystemSafety
                .TryDeleteReservedDirectoryIdentityBound(
                    stage,
                    identity,
                    quarantine =>
                    {
                        Assert.False(Directory.Exists(stage));
                        Directory.CreateDirectory(stage);
                        File.WriteAllText(replacement, "do-not-delete");
                    },
                    out string error);

            Assert.False(cleaned);
            Assert.Contains("recreated", error);
            Assert.True(File.Exists(replacement));
            Assert.Equal("do-not-delete", File.ReadAllText(replacement));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        [InlineData(7, false)]
        public void StableRead_SnapshotPolicyOmitsOnlyChangeTime(
            int changedField,
            bool unchangedWhenDisplaced)
        {
            var identity = new FileSystemEntryIdentity(
                FileSystemEntryIdentityKind.Unix, 1, 2, 0);
            var before = new ProjectionFileSystemSafety.OpenedRegularFileState(
                identity, 4, 10, 20, 30, 40, 1, 0x8000);
            var after = new ProjectionFileSystemSafety.OpenedRegularFileState(
                changedField == 0
                    ? new FileSystemEntryIdentity(
                        FileSystemEntryIdentityKind.Unix, 1, 3, 0)
                    : identity,
                changedField == 1 ? 5 : 4,
                changedField == 2 ? 11UL : 10UL,
                changedField == 3 ? 21UL : 20UL,
                changedField == 4 ? 31UL : 30UL,
                changedField == 5 ? 41UL : 40UL,
                changedField == 6 ? 2U : 1U,
                changedField == 7 ? 0x8001U : 0x8000U);

            Assert.True(before.SameSnapshot(before));
            Assert.True(before.SameSnapshotIgnoringChangeTime(before));
            Assert.False(before.SameSnapshot(after));
            Assert.Equal(
                unchangedWhenDisplaced,
                before.SameSnapshotIgnoringChangeTime(after));
            foreach (var relation in new[]
            {
                ProjectionFileSystemSafety.OriginalPathRelation.Unknown,
                ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile,
                ProjectionFileSystemSafety.OriginalPathRelation.Displaced,
            })
            {
                Assert.Equal(
                    relation == ProjectionFileSystemSafety.OriginalPathRelation.Displaced
                        && unchangedWhenDisplaced,
                    ProjectionFileSystemSafety.IsSnapshotUnchanged(before, after, relation));
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public void StableRead_RelationPolicyRequiresProofToOmitChangeTime(
            int relation, bool expected)
        {
            var identity = new FileSystemEntryIdentity(
                FileSystemEntryIdentityKind.Unix, 1, 2, 0);
            var before = new ProjectionFileSystemSafety.OpenedRegularFileState(
                identity, 4, 10, 20, 30, 40, 1, 0x8000);
            var after = new ProjectionFileSystemSafety.OpenedRegularFileState(
                identity, 4, 10, 20, 31, 40, 1, 0x8000);
            Assert.Equal(expected, ProjectionFileSystemSafety.IsSnapshotUnchanged(
                before, after, (ProjectionFileSystemSafety.OriginalPathRelation)relation));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ProjectionFileSystemSafety.IsSnapshotUnchanged(
                    before, after, (ProjectionFileSystemSafety.OriginalPathRelation)99));
        }

        [Fact]
        public void StableRead_FactoryProvenanceNormalizesPathAndDisposesHandle()
        {
            string path = Path.Combine(tempDirectory, "factory.txt");
            File.WriteAllBytes(path, new byte[] { 1 });
            string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
            var stream = ProjectionFileSystemSafety.OpenRegularFileForRead(relative);
            var handle = stream.SafeFileHandle;
            var state = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                handle, "factory", rejectHardLinks: true);
            try
            {
                Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile,
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, inspectedPath =>
                        {
                            Assert.True(inspectedPath == Path.GetFullPath(relative),
                                "Factory must freeze the full original pathname before open.");
                            return state.Identity;
                        }));
                Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.Displaced,
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, _ => throw new FileNotFoundException()));
                Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.Displaced,
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, _ => throw new DirectoryNotFoundException()));
                Assert.Throws<IOException>(() =>
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, _ => throw new IOException("inspection fault")));
                Assert.Throws<UnauthorizedAccessException>(() =>
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, _ => throw new UnauthorizedAccessException()));
                Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.Displaced,
                    ProjectionFileSystemSafety.GetOriginalPathRelation(
                        stream, state.Identity, _ => default));
            }
            finally
            {
                stream.Dispose();
            }
            Assert.True(handle.IsClosed);
        }

        [Fact]
        public void StableRead_UnknownProvenanceNeverProbesStreamName()
        {
            string path = Path.Combine(tempDirectory, "unknown-probe.txt");
            File.WriteAllBytes(path, new byte[] { 1 });
            using var handle = File.OpenHandle(path);
            using var stream = new FileStream(handle, FileAccess.Read);
            var state = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                handle, "unknown probe", rejectHardLinks: true);
            Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.Unknown,
                ProjectionFileSystemSafety.GetOriginalPathRelation(
                    stream, state.Identity, _ => throw new InvalidOperationException(
                        "Unknown streams must not probe any pathname.")));
        }

        [Fact]
        public void StableRead_ExclusiveStageOwnsProvenanceAndHandle()
        {
            string path = Path.Combine(tempDirectory, "exclusive-stage.tmp");
            var stream = ProjectionFileSystemSafety.CreatePublicationStagingFile(path);
            var handle = stream.SafeFileHandle;
            try
            {
                stream.WriteByte(7);
                stream.Flush(flushToDisk: true);
                var state = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                    handle, "exclusive stage", rejectHardLinks: true);
                Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile,
                    ProjectionFileSystemSafety.GetOriginalPathRelation(stream, state.Identity));
                Assert.True(new byte[] { 7 }.SequenceEqual(
                    ProjectionFileSystemSafety.ReadOpenedRegularFileBoundedStable(
                        stream, 1, "exclusive stage", rejectHardLinks: true, out _)));
                if (OperatingSystem.IsWindows())
                    Assert.Throws<IOException>(() => File.OpenRead(path));
            }
            finally
            {
                stream.Dispose();
            }
            Assert.True(handle.IsClosed);
            Assert.Throws<IOException>(() =>
                ProjectionFileSystemSafety.CreatePublicationStagingFile(path));
        }

        [SkippableFact]
        public void StableRead_WindowsExtendedPathRetainsFactoryProvenance()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Requires Windows extended paths.");
            string path = Path.Combine(tempDirectory, "extended-path.txt");
            File.WriteAllBytes(path, new byte[] { 1 });
            using var stream = ProjectionFileSystemSafety.OpenRegularFileForRead(
                BuildfilePathSafety.ToWindowsExtendedPath(path));
            var state = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                stream.SafeFileHandle, "extended path", rejectHardLinks: true);
            Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile,
                ProjectionFileSystemSafety.GetOriginalPathRelation(stream, state.Identity));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StableRead_VerifierRequiresChangeTimeUnlessDisplaced(
            bool originalPathStillReferencesOpenedFile)
        {
            string path = Path.Combine(tempDirectory, "verification-state.txt");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            using FileStream stream =
                ProjectionFileSystemSafety.OpenRegularFileForRead(path);
            var current = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                stream.SafeFileHandle, "verification state", rejectHardLinks: true);
            var before = new ProjectionFileSystemSafety.OpenedRegularFileState(
                current.Identity,
                current.Length,
                current.ChangeTimeA,
                current.ChangeTimeB,
                current.ChangeTimeC ^ 1UL,
                current.ChangeTimeD,
                current.LinkCount,
                current.TypeBits);

            if (originalPathStillReferencesOpenedFile)
            {
                Assert.Throws<IOException>(() =>
                    ProjectionFileSystemSafety.VerifyOpenedRegularFileUnchanged(
                        stream.SafeFileHandle,
                        "verification state",
                        rejectHardLinks: true,
                        before));
                Assert.Throws<IOException>(() =>
                    ProjectionFileSystemSafety.VerifyOpenedRegularFileUnchanged(
                        stream.SafeFileHandle,
                        "verification state",
                        rejectHardLinks: true,
                        before,
                        ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile));
            }
            else
            {
                ProjectionFileSystemSafety.VerifyOpenedRegularFileUnchanged(
                    stream.SafeFileHandle,
                    "verification state",
                    rejectHardLinks: true,
                    before,
                    ProjectionFileSystemSafety.OriginalPathRelation.Displaced);
            }
        }

        [Fact]
        public void StableRead_UnassociatedNativeHandleReadsUnchangedReport()
        {
            string path = Path.Combine(tempDirectory, "unassociated-report.json");
            byte[] original = GenerationReportBytes('a');
            File.WriteAllBytes(path, original);
            using var handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var stream = new FileStream(handle, FileAccess.Read);

            byte[] read = ProjectionFileSystemSafety.ReadOpenedRegularFileBoundedStable(
                stream,
                FontLibraryReportFormatter.MaxReportBytes,
                "external generation report",
                rejectHardLinks: true,
                out _);

            Assert.True(original.SequenceEqual(read));
            Assert.True(FontLibraryReportFormatter.TryParse(read, out var report, out _));
            Assert.True(report.FullTreeSha256 == new string('a', 64));
        }

        [SkippableFact]
        public void StableRead_AssociatedReportRewriteWithRestoredMtimeRejectsBeforeParsing()
            => AssertReportRewriteWithRestoredMtimeRejected(useSafeOpener: true);

        [SkippableFact]
        public void StableRead_UnknownProvenanceReportRewriteWithRestoredMtimeRejectsBeforeParsing()
            => AssertReportRewriteWithRestoredMtimeRejected(useSafeOpener: false);

        void AssertReportRewriteWithRestoredMtimeRejected(bool useSafeOpener)
        {
            Skip.If(OperatingSystem.IsWindows(), "Requires Unix ctime.");
            string path = Path.Combine(tempDirectory, "rewritten-report.json");
            byte[] original = GenerationReportBytes('a');
            byte[] replacement = GenerationReportBytes('b');
            Assert.Equal(original.Length, replacement.Length);
            File.WriteAllBytes(path, original);
            DateTime mtime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, mtime);
            using FileStream stream = useSafeOpener
                ? ProjectionFileSystemSafety.OpenRegularFileForRead(path)
                : new FileStream(
                    File.OpenHandle(
                        path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete),
                    FileAccess.Read);
            var before = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                stream.SafeFileHandle, "report rewrite", rejectHardLinks: true);
            var expectedRelation = useSafeOpener
                ? ProjectionFileSystemSafety.OriginalPathRelation.SameOpenedFile
                : ProjectionFileSystemSafety.OriginalPathRelation.Unknown;
            Assert.Equal(expectedRelation,
                ProjectionFileSystemSafety.GetOriginalPathRelation(stream, before.Identity));
            bool mutationStateVerified = false;
            bool parserReached = false;

            IOException error = Assert.Throws<IOException>(() =>
            {
                byte[] read = ProjectionFileSystemSafety.ReadOpenedRegularFileBoundedStable(
                    stream,
                    FontLibraryReportFormatter.MaxReportBytes,
                    "external generation report",
                    rejectHardLinks: true,
                    out _,
                    afterInspection: () =>
                    {
                        // Cross coarse ctime boundaries before the rewrite.
                        Thread.Sleep(1100);
                        File.WriteAllBytes(path, replacement);
                        File.SetLastWriteTimeUtc(path, mtime);
                        var after = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                            stream.SafeFileHandle, "report rewrite", rejectHardLinks: true);
                        AssertOnlyChangeTimeChanged(before, after);
                        Assert.Equal(expectedRelation,
                            ProjectionFileSystemSafety.GetOriginalPathRelation(stream, before.Identity));
                        Assert.True(
                            before.Identity.Equals(
                                ProjectionFileSystemSafety.CaptureExistingFileSystemEntryIdentity(path)));
                        mutationStateVerified = true;
                    });
                parserReached = true;
                Assert.True(FontLibraryReportFormatter.TryParse(read, out _, out _));
            });

            Assert.True(mutationStateVerified);
            Assert.False(parserReached);
            Assert.True(error.Message ==
                "Opened regular file changed while it was read: external generation report",
                "Expected the held-file snapshot mutation rejection, not an unrelated I/O failure.");
        }

        [SkippableTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void StableRead_AssociatedReportRenameParsesHeldBytes(
            bool createReplacement)
        {
            Skip.If(OperatingSystem.IsWindows(), "Production Windows handle denies rename.");
            string path = Path.Combine(tempDirectory, "renamed-report.json");
            string moved = Path.Combine(tempDirectory, "held-report.json");
            byte[] original = GenerationReportBytes('a');
            byte[] replacement = GenerationReportBytes('b');
            File.WriteAllBytes(path, original);
            using FileStream stream = ProjectionFileSystemSafety.OpenRegularFileForRead(path);

            byte[] read = ProjectionFileSystemSafety.ReadOpenedRegularFileBoundedStable(
                stream,
                FontLibraryReportFormatter.MaxReportBytes,
                "external generation report",
                rejectHardLinks: true,
                out var held,
                afterInspection: () =>
                {
                    File.Move(path, moved);
                    if (createReplacement)
                        File.WriteAllBytes(path, replacement);
                });

            Assert.True(original.SequenceEqual(read));
            Assert.Equal(ProjectionFileSystemSafety.OriginalPathRelation.Displaced,
                ProjectionFileSystemSafety.GetOriginalPathRelation(stream, held.Identity));
            Assert.True(FontLibraryReportFormatter.TryParse(read, out var report, out _));
            Assert.True(report.FullTreeSha256 == new string('a', 64));
            Assert.True(original.SequenceEqual(File.ReadAllBytes(moved)));
            if (createReplacement)
                Assert.True(replacement.SequenceEqual(File.ReadAllBytes(path)));
            else
                Assert.False(File.Exists(path));
        }

        [SkippableFact]
        public void StableRead_UnassociatedNativeHandleRenameDoesNotProveDisplacement()
        {
            Skip.If(OperatingSystem.IsWindows(), "Requires Unix ctime.");
            string path = Path.Combine(tempDirectory, "unknown-origin.json");
            string moved = Path.Combine(tempDirectory, "unknown-held.json");
            File.WriteAllBytes(path, GenerationReportBytes('a'));
            using var handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var stream = new FileStream(handle, FileAccess.Read);
            var before = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                stream.SafeFileHandle, "unknown origin", rejectHardLinks: true);
            bool renameStateVerified = false;

            IOException error = Assert.Throws<IOException>(() =>
                ProjectionFileSystemSafety.ReadOpenedRegularFileBoundedStable(
                    stream,
                    FontLibraryReportFormatter.MaxReportBytes,
                    "external generation report",
                    rejectHardLinks: true,
                    out _,
                    afterInspection: () =>
                    {
                        // Cross coarse ctime boundaries before the rename.
                        Thread.Sleep(1100);
                        File.Move(path, moved);
                        File.WriteAllBytes(path, GenerationReportBytes('b'));
                        var after = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                            stream.SafeFileHandle, "unknown origin", rejectHardLinks: true);
                        AssertOnlyChangeTimeChanged(before, after);
                        Assert.False(
                            before.Identity.Equals(
                                ProjectionFileSystemSafety.CaptureExistingFileSystemEntryIdentity(path)));
                        renameStateVerified = true;
                    }));

            Assert.True(renameStateVerified);
            Assert.True(error.Message ==
                "Opened regular file changed while it was read: external generation report",
                "Expected Unknown-provenance full snapshot rejection, not an unrelated I/O failure.");
        }

        static byte[] GenerationReportBytes(char hashDigit)
            => Encoding.UTF8.GetBytes(FontLibraryReportFormatter.Format(
                new FontLibraryReport
                {
                    Mode = "generate",
                    Oracle = "generation",
                    ManifestSha256 = new string('c', 64),
                    PayloadTreeSha256 = new string('d', 64),
                    FullTreeSha256 = new string(hashDigit, 64),
                }));

        static void AssertOnlyChangeTimeChanged(
            ProjectionFileSystemSafety.OpenedRegularFileState before,
            ProjectionFileSystemSafety.OpenedRegularFileState after)
        {
            Assert.True(before.SameSnapshotIgnoringChangeTime(after),
                "Mutation prerequisite: identity/length/mtime/links/type equal.");
            Assert.False(before.SameSnapshot(after),
                "Mutation prerequisite: held-file ctime changed.");
        }

        [SkippableFact]
        public void StableRead_RejectsSameLengthRewriteWithRestoredMtime()
        {
            Skip.If(
                OperatingSystem.IsWindows(),
                "Unix ctime behavior is covered by this test.");
            string path = Path.Combine(
                tempDirectory, "ctime-rewrite.txt");
            File.WriteAllText(
                path, "AAAA", new UTF8Encoding(false));
            DateTime mtime = File.GetLastWriteTimeUtc(path);
            using FileStream stream =
                ProjectionFileSystemSafety
                    .OpenRegularFileForRead(path);

            IOException error = Assert.Throws<IOException>(() =>
                ProjectionFileSystemSafety
                    .ReadOpenedRegularFileBoundedStable(
                        stream,
                        maxBytes: 1024,
                        label: "ctime rewrite",
                        rejectHardLinks: true,
                        out _,
                        afterInspection: () =>
                        {
                            // Some CI filesystems expose ctime at one-second
                            // resolution. Cross that boundary so the test
                            // deterministically isolates ctime from restored
                            // length/mtime metadata.
                            Thread.Sleep(1100);
                            File.WriteAllText(
                                path,
                                "BBBB",
                                new UTF8Encoding(false));
                            File.SetLastWriteTimeUtc(path, mtime);
                        }));

            Assert.Contains("changed while it was read", error.Message);
        }

        [Fact]
        public void IdentityBoundCleanup_RefusesReplacedQuarantine()
        {
            string stage = Path.Combine(tempDirectory, "stage-race");
            string retained = Path.Combine(tempDirectory, "retained-owned-stage");
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "owned.txt"), "owned");
            FileSystemEntryIdentity identity =
                ProjectionFileSystemSafety
                    .CaptureExistingFileSystemEntryIdentity(stage);
            string replacementSentinel = "";

            bool cleaned = ProjectionFileSystemSafety
                .TryDeleteReservedDirectoryIdentityBound(
                    stage,
                    identity,
                    quarantine =>
                    {
                        Directory.Move(quarantine, retained);
                        Directory.CreateDirectory(quarantine);
                        replacementSentinel =
                            Path.Combine(quarantine, "replacement.txt");
                        File.WriteAllText(
                            replacementSentinel, "do-not-delete");
                    },
                    out string error);

            Assert.False(cleaned);
            Assert.Contains("replaced", error);
            Assert.True(File.Exists(
                Path.Combine(retained, "owned.txt")));
            Assert.True(File.Exists(replacementSentinel));
        }

        [Fact]
        public void PublicationIdentityCheck_RejectsReplacedStagingDirectory()
        {
            string stage = Path.Combine(tempDirectory, "stage-before-publish");
            string retained = Path.Combine(tempDirectory, "retained-stage");
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "owned.txt"), "owned");
            FileSystemEntryIdentity identity =
                ProjectionFileSystemSafety
                    .CaptureExistingFileSystemEntryIdentity(stage);

            Directory.Move(stage, retained);
            Directory.CreateDirectory(stage);
            string replacement = Path.Combine(stage, "replacement.txt");
            File.WriteAllText(replacement, "do-not-delete");

            IOException publishError = Assert.Throws<IOException>(() =>
                ProjectionFileSystemSafety.VerifyPlainDirectoryIdentity(
                    stage, identity));
            Assert.Contains("replaced", publishError.Message);
            Assert.False(
                ProjectionFileSystemSafety
                    .TryDeleteReservedDirectoryIdentityBound(
                        stage,
                        identity,
                        beforeFinalIdentityCheck: null!,
                        out string cleanupError));
            Assert.Contains("replaced", cleanupError);
            Assert.True(File.Exists(replacement));
            Assert.True(File.Exists(
                Path.Combine(retained, "owned.txt")));
        }

        [Fact]
        public void AtomicNoReplacePublicationFault_LeavesNoPartialOutput()
        {
            string stage = Path.Combine(tempDirectory, "publish-stage");
            string output = Path.Combine(tempDirectory, "publish-output");
            Directory.CreateDirectory(stage);
            File.WriteAllText(
                Path.Combine(stage, "complete.txt"), "complete");

            Assert.Throws<PlatformNotSupportedException>(() =>
                BuildfileExportCore.PublishDirectoryNoReplace(
                    stage, output, isBrowser: true));

            Assert.False(File.Exists(output));
            Assert.False(Directory.Exists(output));
            Assert.Equal(
                "complete",
                File.ReadAllText(Path.Combine(stage, "complete.txt")));
        }

        [Fact]
        public void ReportPublicationFault_DoesNotRemovePublishedPackage()
        {
            string stage = Path.Combine(tempDirectory, "report-fault-stage");
            string output = Path.Combine(tempDirectory, "report-fault-output");
            string report = Path.Combine(tempDirectory, "report.json");
            Directory.CreateDirectory(stage);
            File.WriteAllText(
                Path.Combine(stage, "package-report.json"), "complete");
            BuildfileExportCore.PublishDirectoryNoReplace(stage, output);
            File.WriteAllText(report, "racing-report");

            Assert.False(BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("new-report"),
                report,
                out string error));
            Assert.NotEmpty(error);
            Assert.Equal(
                "complete",
                File.ReadAllText(Path.Combine(
                    output, "package-report.json")));
            Assert.Equal("racing-report", File.ReadAllText(report));
        }

        [Fact]
        public void ReportPublication_StageReplacementBeforeMoveRetainsForeignFile()
        {
            string destination = Path.Combine(
                tempDirectory, "report-stage-replaced.json");
            string retained = Path.Combine(
                tempDirectory, "retained-report-stage");
            string foreign = "";

            bool success = BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("expected"),
                destination,
                (stage, _) =>
                {
                    File.Move(stage, retained);
                    foreign = stage;
                    File.WriteAllText(foreign, "foreign");
                },
                afterPublishBeforeVerificationForTest: null!,
                beforeCleanupFinalIdentityCheckForTest: null!,
                deleteStagingForTest: null!,
                out string error);

            Assert.False(success);
            Assert.Contains("replaced", error);
            Assert.False(File.Exists(destination));
            Assert.Equal("foreign", File.ReadAllText(foreign));
            Assert.Equal("expected", File.ReadAllText(retained));
        }

        [Fact]
        public void ReportPublication_DestinationReplacementAfterMoveRetainsBothFiles()
        {
            string destination = Path.Combine(
                tempDirectory, "report-destination-replaced.json");
            string retained = Path.Combine(
                tempDirectory, "retained-report-destination");

            bool success = BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("expected"),
                destination,
                beforePublishForTest: null!,
                afterPublishBeforeVerificationForTest: (_, dest) =>
                {
                    File.Move(dest, retained);
                    File.WriteAllText(dest, "foreign");
                },
                beforeCleanupFinalIdentityCheckForTest: null!,
                deleteStagingForTest: null!,
                out string error);

            Assert.False(success);
            Assert.Contains("destination retained", error);
            Assert.Equal("foreign", File.ReadAllText(destination));
            Assert.Equal("expected", File.ReadAllText(retained));
        }

        [Fact]
        public void ReportCleanup_ReplacementBeforeFinalCheckIsRetained()
        {
            string destination = Path.Combine(
                tempDirectory, "report-cleanup-race.json");
            string retained = Path.Combine(
                tempDirectory, "retained-report-cleanup");
            string foreign = "";

            bool success = BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("expected"),
                destination,
                (_, _) => throw new IOException("forced pre-move failure"),
                afterPublishBeforeVerificationForTest: null!,
                beforeCleanupFinalIdentityCheckForTest: quarantine =>
                {
                    File.Move(quarantine, retained);
                    foreign = quarantine;
                    File.WriteAllText(foreign, "foreign");
                },
                deleteStagingForTest: null!,
                out string error);

            Assert.False(success);
            Assert.Contains("Cleanup incomplete", error);
            Assert.Equal("foreign", File.ReadAllText(foreign));
            Assert.Equal("expected", File.ReadAllText(retained));
            Assert.False(File.Exists(destination));
        }

        [Fact]
        public void ReportPublication_ReplacementAfterVerificationFailsBeforeSuccess()
        {
            string destination = Path.Combine(
                tempDirectory, "report-release-race.json");
            string retained = Path.Combine(
                tempDirectory, "retained-report-release");

            bool success = BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("expected"),
                destination,
                beforePublishForTest: null!,
                afterPublishBeforeVerificationForTest: null!,
                afterPublishedVerificationBeforeReleaseForTest: (_, dest) =>
                {
                    File.Move(dest, retained);
                    File.WriteAllText(dest, "foreign");
                },
                beforeCleanupFinalIdentityCheckForTest: null!,
                afterCleanupFinalIdentityCheckForTest: null!,
                deleteStagingForTest: null!,
                out string error);

            Assert.False(success);
            Assert.Contains("destination retained", error);
            Assert.Equal("foreign", File.ReadAllText(destination));
            Assert.Equal("expected", File.ReadAllText(retained));
        }

        [Fact]
        public void ReportCleanup_ReplacementAfterFinalCheckIsRetained()
        {
            string destination = Path.Combine(
                tempDirectory, "report-cleanup-final-race.json");
            string retained = Path.Combine(
                tempDirectory, "retained-report-final-cleanup");
            string foreign = "";

            bool success = BuildfileBuildCore.PublishBytesNoReplace(
                Encoding.UTF8.GetBytes("expected"),
                destination,
                (_, _) => throw new IOException("forced pre-move failure"),
                afterPublishBeforeVerificationForTest: null!,
                afterPublishedVerificationBeforeReleaseForTest: null!,
                beforeCleanupFinalIdentityCheckForTest: null!,
                afterCleanupFinalIdentityCheckForTest: quarantine =>
                {
                    File.Move(quarantine, retained);
                    foreign = quarantine;
                    File.WriteAllText(foreign, "foreign");
                },
                deleteStagingForTest: null!,
                out string error);

            Assert.False(success);
            Assert.Contains("Cleanup incomplete", error);
            Assert.Equal("foreign", File.ReadAllText(foreign));
            Assert.Equal("expected", File.ReadAllText(retained));
            Assert.False(File.Exists(destination));
        }

        static bool CreateHardLink(string linkPath, string existingPath)
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkWindows(
                    linkPath, existingPath, IntPtr.Zero);
            return CreateHardLinkUnix(existingPath, linkPath) == 0;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true,
            EntryPoint = "CreateHardLinkW")]
        static extern bool CreateHardLinkWindows(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);

        [DllImport(
            "libc",
            CharSet = CharSet.Ansi,
            SetLastError = true,
            EntryPoint = "link")]
        static extern int CreateHardLinkUnix(
            string existingPath,
            string linkPath);
    }
}
