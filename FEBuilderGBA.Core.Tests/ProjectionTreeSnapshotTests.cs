// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
                beforeFileOpen: null);

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
                        beforeFileOpen: null,
                        disallowedFileIdentity: null));
                Assert.Contains("Hard-linked", error.Message);

                // The additive package policy does not narrow legacy callers.
                Assert.Single(ProjectionTreeSnapshotReader.Capture(
                    tempDirectory,
                    maxEntries: 4,
                    maxBytes: 1024,
                    maxTextFileBytes: 1024,
                    beforeFileOpen: null).Files);
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
                    beforeFileOpen: null,
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
        public void IdentityBoundCleanup_NeverDeletesReplacementAtReservedPath()
        {
            string stage = Path.Combine(tempDirectory, "stage");
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "owned.txt"), "owned");
            FileSystemEntryIdentity identity =
                ProjectionFileSystemSafety
                    .CaptureExistingFileSystemEntryIdentity(stage);
            string replacement = Path.Combine(stage, "replacement.txt");

            Assert.True(
                ProjectionFileSystemSafety
                    .TryDeleteReservedDirectoryIdentityBound(
                        stage,
                        identity,
                        quarantine =>
                        {
                            Assert.False(Directory.Exists(stage));
                            Directory.CreateDirectory(stage);
                            File.WriteAllText(replacement, "do-not-delete");
                        },
                        out string error),
                error);
            Assert.True(File.Exists(replacement));
            Assert.Equal("do-not-delete", File.ReadAllText(replacement));
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
                        beforeFinalIdentityCheck: null,
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
                afterPublishBeforeVerificationForTest: null,
                beforeCleanupFinalIdentityCheckForTest: null,
                deleteStagingForTest: null,
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
                beforePublishForTest: null,
                afterPublishBeforeVerificationForTest: (_, dest) =>
                {
                    File.Move(dest, retained);
                    File.WriteAllText(dest, "foreign");
                },
                beforeCleanupFinalIdentityCheckForTest: null,
                deleteStagingForTest: null,
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
                afterPublishBeforeVerificationForTest: null,
                beforeCleanupFinalIdentityCheckForTest: quarantine =>
                {
                    File.Move(quarantine, retained);
                    foreign = quarantine;
                    File.WriteAllText(foreign, "foreign");
                },
                deleteStagingForTest: null,
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
                beforePublishForTest: null,
                afterPublishBeforeVerificationForTest: null,
                afterPublishedVerificationBeforeReleaseForTest: (_, dest) =>
                {
                    File.Move(dest, retained);
                    File.WriteAllText(dest, "foreign");
                },
                beforeCleanupFinalIdentityCheckForTest: null,
                afterCleanupFinalIdentityCheckForTest: null,
                deleteStagingForTest: null,
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
                afterPublishBeforeVerificationForTest: null,
                afterPublishedVerificationBeforeReleaseForTest: null,
                beforeCleanupFinalIdentityCheckForTest: null,
                afterCleanupFinalIdentityCheckForTest: quarantine =>
                {
                    File.Move(quarantine, retained);
                    foreign = quarantine;
                    File.WriteAllText(foreign, "foreign");
                },
                deleteStagingForTest: null,
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
