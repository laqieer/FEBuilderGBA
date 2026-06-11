// SPDX-License-Identifier: GPL-3.0-or-later
// #1031 — DownloadInstallCore tests (Init Wizard auto-download/install).
//
// Every case injects the download/installer steps so NOTHING hits the live
// network or launches a real installer in CI. Synthetic archives are built
// in-test with System.IO.Compression (.zip). Temp dirs are cleaned up.
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DownloadInstallCoreTests : IDisposable
    {
        readonly string _baseDir;

        public DownloadInstallCoreTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(),
                "febgba_dlcore_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true); }
            catch { /* best-effort */ }
        }

        // ---- Injectable download steps ------------------------------------

        // Produces a fake .exe (single-file) or fake archive at destPath.
        static DownloadInstallCore.DownloadStep FakeExeWriter(byte[] content)
        {
            return (string url, string dest, out string error, string referer) =>
            {
                error = "";
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, content);
                return true;
            };
        }

        // A download step that builds a .zip containing one nested entry whose
        // filename matches `entryRelPath`, with `entryBytes` content.
        static DownloadInstallCore.DownloadStep FakeZipWriter(string entryRelPath, byte[] entryBytes)
        {
            return (string url, string dest, out string error, string referer) =>
            {
                error = "";
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = zip.CreateEntry(entryRelPath);
                    using var es = entry.Open();
                    es.Write(entryBytes, 0, entryBytes.Length);
                }
                return true;
            };
        }

        // A download step that always fails (simulates a network error).
        static DownloadInstallCore.DownloadStep FailingDownload(string message)
        {
            return (string url, string dest, out string error, string referer) =>
            {
                error = message;
                return false;
            };
        }

        // A download step that writes a corrupt (non-archive) file so extraction
        // fails.
        static DownloadInstallCore.DownloadStep CorruptArchiveWriter()
        {
            return (string url, string dest, out string error, string referer) =>
            {
                error = "";
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });
                return true;
            };
        }

        // ---- 1) Direct .exe placement -------------------------------------

        [Fact]
        public void Download_SingleExe_PlacesAndResolves()
        {
            // arm-none-eabi-as is a single-file .exe resource.
            byte[] fakeExe = new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00 };
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string error, FakeExeWriter(fakeExe));

            Assert.NotNull(resolved);
            Assert.Equal("", error);
            Assert.True(File.Exists(resolved));
            Assert.EndsWith("arm-none-eabi-as.exe", resolved, StringComparison.OrdinalIgnoreCase);
            // Placed under {baseDir}/app/asm.
            Assert.Contains(Path.Combine("app", "asm"), resolved);
            Assert.Equal(fakeExe, File.ReadAllBytes(resolved));
        }

        // ---- 2) Archive extract + nested-exe discovery --------------------

        [Fact]
        public void Download_Archive_ExtractsAndDiscoversNestedExe()
        {
            // mGBA is an archive resource whose match-glob is "mGBA.exe".
            byte[] nested = new byte[] { 1, 2, 3, 4, 5 };
            // Nest the exe in a subfolder to prove recursive discovery.
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FakeZipWriter("mGBA-0.6.1/mGBA.exe", nested));

            Assert.NotNull(resolved);
            Assert.Equal("", error);
            Assert.True(File.Exists(resolved));
            Assert.EndsWith("mGBA.exe", resolved, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("app", "mVBA"), resolved);
            Assert.Equal(nested, File.ReadAllBytes(resolved));
        }

        // ---- 3) Download-failure cleanup ----------------------------------

        [Fact]
        public void Download_DownloadFailure_ReturnsErrorAndNoPartialFiles()
        {
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FailingDownload("simulated network error"));

            Assert.Null(resolved);
            Assert.False(string.IsNullOrEmpty(error));
            // Final tool dir must NOT have been created (no partial install).
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "mVBA")));
            // No leftover staging dirs under temp for this test's base.
            Assert.Empty(Directory.GetDirectories(_baseDir));
        }

        // ---- 4) Extraction-failure cleanup --------------------------------

        [Fact]
        public void Download_ExtractionFailure_ReturnsErrorAndNoInstall()
        {
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, CorruptArchiveWriter());

            Assert.Null(resolved);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "mVBA")));
        }

        // ---- 5) Missing-expected-exe-after-extract ------------------------

        [Fact]
        public void Download_ArchiveWithoutMatchingExe_ReturnsErrorAndNoInstall()
        {
            // Archive extracts fine but contains no "mGBA.exe".
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FakeZipWriter("readme.txt", new byte[] { 9, 9, 9 }));

            Assert.Null(resolved);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "mVBA")));
        }

        // ---- Atomicity: existing install preserved on failed replacement --

        [Fact]
        public void Download_Failure_PreservesPreExistingInstall()
        {
            // Seed a prior working install in the final tool dir.
            string finalDir = Path.Combine(_baseDir, "app", "mVBA");
            Directory.CreateDirectory(finalDir);
            string priorExe = Path.Combine(finalDir, "mGBA.exe");
            File.WriteAllBytes(priorExe, new byte[] { 0xAA, 0xBB });

            // A new download that FAILS must not touch the prior install.
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FailingDownload("boom"));

            Assert.Null(resolved);
            Assert.True(File.Exists(priorExe));
            Assert.Equal(new byte[] { 0xAA, 0xBB }, File.ReadAllBytes(priorExe));
        }

        // ---- Single-file producing no exe -> error ------------------------

        [Fact]
        public void Download_SingleFile_DownloadFails_NoInstall()
        {
            string resolved = DownloadInstallCore.Download(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string error, FailingDownload("net down"));

            Assert.Null(resolved);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "asm")));
        }

        // ---- Spec catalog sanity (URLs + globs copied verbatim) -----------

        [Theory]
        [InlineData(DownloadInstallCore.ResourceId.VbaM, "VisualBoyAdvance*.exe", "VBA-M")]
        [InlineData(DownloadInstallCore.ResourceId.MGba, "mGBA.exe", "mVBA")]
        [InlineData(DownloadInstallCore.ResourceId.EA, "Core.exe", "Event Assembler")]
        [InlineData(DownloadInstallCore.ResourceId.Lyn, "lyn.exe", null)]
        [InlineData(DownloadInstallCore.ResourceId.Sappy, "sappy.exe", "sappy")]
        [InlineData(DownloadInstallCore.ResourceId.GbaMusicStudio, "VG Music Studio.exe", "GBAMusicStdio")]
        [InlineData(DownloadInstallCore.ResourceId.NoGba, "NO$GBA.EXE", "no$gba")]
        [InlineData(DownloadInstallCore.ResourceId.ArmAs, "arm-none-eabi-as.exe", "asm")]
        [InlineData(DownloadInstallCore.ResourceId.GbaMusRiper, "song_riper.exe", "gba_mus_riper")]
        [InlineData(DownloadInstallCore.ResourceId.Sox, "sox.exe", "sox")]
        [InlineData(DownloadInstallCore.ResourceId.Midfix4agb, "midfix4agb.exe", "midfix4agb")]
        public void GetSpec_ReturnsExpectedGlobAndUrl(
            DownloadInstallCore.ResourceId id, string glob, string subDir)
        {
            var spec = DownloadInstallCore.GetSpec(id);
            Assert.Equal(glob, spec.MatchGlob);
            Assert.False(string.IsNullOrEmpty(spec.Url));
            Assert.StartsWith("http", spec.Url);
            if (subDir != null)
                Assert.Contains(subDir, spec.AppSubDir);
        }

        [Fact]
        public void GetSpec_SingleFileResources_AreFlagged()
        {
            Assert.True(DownloadInstallCore.GetSpec(DownloadInstallCore.ResourceId.Lyn).IsSingleFile);
            Assert.True(DownloadInstallCore.GetSpec(DownloadInstallCore.ResourceId.ArmAs).IsSingleFile);
            Assert.False(DownloadInstallCore.GetSpec(DownloadInstallCore.ResourceId.MGba).IsSingleFile);
        }

        // ---- Two-phase Stage/Commit (bundle all-or-none) ------------------

        [Fact]
        public void Stage_DoesNotTouchFinalDir_UntilCommit()
        {
            // Stage downloads + validates into temp ONLY; the final app dir
            // must NOT exist until Commit is called.
            var staged = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FakeZipWriter("mGBA.exe", new byte[] { 1, 2, 3 }));

            Assert.NotNull(staged);
            Assert.Equal("", error);
            Assert.True(File.Exists(staged.StagedExe));
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "mVBA")));

            string placed = DownloadInstallCore.Commit(staged, ref error);
            staged.Dispose();

            Assert.NotNull(placed);
            Assert.True(File.Exists(placed));
            Assert.False(Directory.Exists(staged.StagingDir)); // disposed
        }

        [Fact]
        public void Stage_Failure_ReturnsNull_AndCleansUp()
        {
            var staged = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.MGba, _baseDir, null,
                out string error, FailingDownload("boom"));

            Assert.Null(staged);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(Directory.Exists(Path.Combine(_baseDir, "app", "mVBA")));
        }

        [Fact]
        public void Bundle_StageAll_ThenCommitAll_IsAtomic_OnLaterFailure()
        {
            // Simulate the Avalonia bundle's stage-all-then-commit-all flow:
            // member 1 stages fine, member 2 FAILS. Because nothing is committed
            // until both staged, member 1 must leave NO partial install.
            string finalDir1 = Path.Combine(_baseDir, "app", "no$gba");

            var s1 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.NoGba, _baseDir, null,
                out string e1, FakeZipWriter("NO$GBA.EXE", new byte[] { 9 }));
            Assert.NotNull(s1);

            var s2 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string e2, FailingDownload("net down"));
            Assert.Null(s2); // second member failed

            // Bundle aborts WITHOUT committing s1 -> dispose only.
            s1.Dispose();

            Assert.False(Directory.Exists(finalDir1),
                "Bundle must not place member 1 when a later member fails.");
        }

        [Fact]
        public void CommitBundle_LateCommitFailure_RollsBackEarlierMembers()
        {
            // Both members stage OK, but member 2's STAGED exe is deleted so its
            // Commit (PlaceFile copy) FAILS. CommitBundle must then roll back
            // member 1 — restoring its prior install and leaving NO new install.
            string finalDir1 = Path.Combine(_baseDir, "app", "no$gba");
            Directory.CreateDirectory(finalDir1);
            string prior1 = Path.Combine(finalDir1, "NO$GBA.EXE");
            File.WriteAllBytes(prior1, new byte[] { 0xAA });

            var s1 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.NoGba, _baseDir, null,
                out string e1, FakeZipWriter("NO$GBA.EXE", new byte[] { 0xBB }));
            Assert.NotNull(s1);

            var s2 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string e2, FakeExeWriter(new byte[] { 0xCC }));
            Assert.NotNull(s2);

            // Sabotage member 2 so its Commit fails after member 1 commits.
            File.Delete(s2.StagedExe);
            string finalDir2 = Path.Combine(_baseDir, "app", "asm");

            string error = "";
            string[] results = DownloadInstallCore.CommitBundle(new[] { s1, s2 }, ref error);
            s1.Dispose();
            s2.Dispose();

            Assert.Null(results);
            Assert.False(string.IsNullOrEmpty(error));
            // Member 1 rolled back: prior install restored byte-identical.
            Assert.True(File.Exists(prior1));
            Assert.Equal(new byte[] { 0xAA }, File.ReadAllBytes(prior1));
            // Member 2 never installed.
            Assert.False(Directory.Exists(finalDir2));
            // No leftover .new-/.old- staging siblings.
            Assert.Empty(Directory.GetDirectories(Path.Combine(_baseDir, "app"), "*.new-*"));
            Assert.Empty(Directory.GetDirectories(Path.Combine(_baseDir, "app"), "*.old-*"));
        }

        [Fact]
        public void CommitBundle_AllSucceed_PlacesEveryMember_AndCleansBackups()
        {
            var s1 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.NoGba, _baseDir, null,
                out string e1, FakeZipWriter("NO$GBA.EXE", new byte[] { 1 }));
            var s2 = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string e2, FakeExeWriter(new byte[] { 2 }));
            Assert.NotNull(s1);
            Assert.NotNull(s2);

            string error = "";
            string[] results = DownloadInstallCore.CommitBundle(new[] { s1, s2 }, ref error);
            s1.Dispose();
            s2.Dispose();

            Assert.NotNull(results);
            Assert.Equal(2, results.Length);
            Assert.True(File.Exists(results[0]));
            Assert.True(File.Exists(results[1]));
            Assert.Empty(Directory.GetDirectories(Path.Combine(_baseDir, "app"), "*.old-*"));
        }

        // ---- Crash-safe PlaceFile preserves prior install -----------------

        [Fact]
        public void Commit_PlaceFailure_RestoresPriorInstall()
        {
            // Seed a prior working install.
            string finalDir = Path.Combine(_baseDir, "app", "asm");
            Directory.CreateDirectory(finalDir);
            string priorExe = Path.Combine(finalDir, "arm-none-eabi-as.exe");
            File.WriteAllBytes(priorExe, new byte[] { 0xCA, 0xFE });

            // Stage a new single-file download successfully.
            var staged = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string error, FakeExeWriter(new byte[] { 0x11 }));
            Assert.NotNull(staged);

            // Force the swap-in to fail by holding the prior dir open (a locked
            // file makes Directory.Move(finalDir, backup) throw on Windows). We
            // emulate the "swap fails" branch by deleting the staged source so
            // PlaceFile's copy throws BEFORE the swap, leaving the prior intact.
            File.Delete(staged.StagedExe);

            string placed = DownloadInstallCore.Commit(staged, ref error);
            staged.Dispose();

            Assert.Null(placed);
            Assert.False(string.IsNullOrEmpty(error));
            // Prior install must be byte-identical (never clobbered/emptied).
            Assert.True(File.Exists(priorExe));
            Assert.Equal(new byte[] { 0xCA, 0xFE }, File.ReadAllBytes(priorExe));
        }

        [Fact]
        public void Commit_Success_ReplacesPriorInstall()
        {
            string finalDir = Path.Combine(_baseDir, "app", "asm");
            Directory.CreateDirectory(finalDir);
            File.WriteAllBytes(Path.Combine(finalDir, "arm-none-eabi-as.exe"), new byte[] { 0x00 });

            var staged = DownloadInstallCore.Stage(
                DownloadInstallCore.ResourceId.ArmAs, _baseDir, null,
                out string error, FakeExeWriter(new byte[] { 0x42 }));
            Assert.NotNull(staged);

            string placed = DownloadInstallCore.Commit(staged, ref error);
            staged.Dispose();

            Assert.NotNull(placed);
            Assert.Equal(new byte[] { 0x42 }, File.ReadAllBytes(placed));
        }

        // ---- Git plumbing (all steps injected) ----------------------------

        [Fact]
        public async Task DownloadGit_HappyPath_CallsAllStepsAndReturnsPath()
        {
            int getUrlCalls = 0, downloadCalls = 0, runCalls = 0, findCalls = 0;
            string installerSeen = null;

            var git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => { getUrlCalls++; return "https://example/git-64-bit.exe"; },
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    downloadCalls++;
                    installerSeen = url;
                    error = "";
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.WriteAllBytes(dest, new byte[] { 1 });
                    return true;
                },
                runInstaller: _ => { runCalls++; return Task.FromResult(true); },
                findGit: () => { findCalls++; return "C:/git/bin/git.exe"; });

            Assert.True(git.Success);
            Assert.Equal("C:/git/bin/git.exe", git.Path);
            Assert.Equal("", git.Error);
            Assert.Equal(1, getUrlCalls);
            Assert.Equal(1, downloadCalls);
            Assert.Equal(1, runCalls);
            Assert.Equal(1, findCalls);
            Assert.Equal("https://example/git-64-bit.exe", installerSeen);
        }

        [Fact]
        public async Task DownloadGit_NoInstallerUrl_FailsWithoutDownloading()
        {
            bool downloaded = false;
            var git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => null,
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    downloaded = true; error = ""; return true;
                },
                runInstaller: _ => Task.FromResult(true),
                findGit: () => "git.exe");

            Assert.Null(git.Path);
            Assert.False(git.Success);
            Assert.False(downloaded);
            Assert.False(string.IsNullOrEmpty(git.Error));
        }

        [Fact]
        public async Task DownloadGit_DownloadFails_DoesNotRunInstaller()
        {
            bool ran = false;
            var git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => "https://example/git.exe",
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    error = "download blew up"; return false;
                },
                runInstaller: _ => { ran = true; return Task.FromResult(true); },
                findGit: () => "git.exe");

            Assert.Null(git.Path);
            Assert.False(ran);
            Assert.False(string.IsNullOrEmpty(git.Error));
        }

        [Fact]
        public async Task DownloadGit_InstallerFails_DoesNotResolveGit()
        {
            bool found = false;
            var git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => "https://example/git.exe",
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    error = "";
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.WriteAllBytes(dest, new byte[] { 1 });
                    return true;
                },
                runInstaller: _ => Task.FromResult(false),
                findGit: () => { found = true; return "git.exe"; });

            Assert.Null(git.Path);
            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(git.Error));
        }

        [Fact]
        public async Task DownloadGit_InstallSucceedsButGitNotFound_Fails()
        {
            var git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => "https://example/git.exe",
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    error = "";
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.WriteAllBytes(dest, new byte[] { 1 });
                    return true;
                },
                runInstaller: _ => Task.FromResult(true),
                findGit: () => "");

            Assert.Null(git.Path);
            Assert.False(string.IsNullOrEmpty(git.Error));
        }

        // ---- U.HttpDownloadFile failure contract (no live network) --------

        [Fact]
        public void HttpDownloadFile_InvalidUrl_FailsAndLeavesNoPartialFile()
        {
            string dest = Path.Combine(_baseDir, "out.bin");
            // A malformed/unreachable URL fails fast without writing the dest.
            bool ok = U.HttpDownloadFile("not-a-valid-url://x", dest, out string error);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(File.Exists(dest));
        }
    }
}
