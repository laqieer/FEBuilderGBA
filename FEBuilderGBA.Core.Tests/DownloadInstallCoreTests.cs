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

        // ---- Git plumbing (all steps injected) ----------------------------

        [Fact]
        public async Task DownloadGit_HappyPath_CallsAllStepsAndReturnsPath()
        {
            int getUrlCalls = 0, downloadCalls = 0, runCalls = 0, findCalls = 0;
            string installerSeen = null;

            string git = await DownloadInstallCore.DownloadGitAsync(
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

            Assert.Equal("C:/git/bin/git.exe", git);
            Assert.Equal("", DownloadInstallCore.LastGitError);
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
            string git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => null,
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    downloaded = true; error = ""; return true;
                },
                runInstaller: _ => Task.FromResult(true),
                findGit: () => "git.exe");

            Assert.Null(git);
            Assert.False(downloaded);
            Assert.False(string.IsNullOrEmpty(DownloadInstallCore.LastGitError));
        }

        [Fact]
        public async Task DownloadGit_DownloadFails_DoesNotRunInstaller()
        {
            bool ran = false;
            string git = await DownloadInstallCore.DownloadGitAsync(
                progress: null,
                getInstallerUrl: () => "https://example/git.exe",
                downloadStep: (string url, string dest, out string error, string referer) =>
                {
                    error = "download blew up"; return false;
                },
                runInstaller: _ => { ran = true; return Task.FromResult(true); },
                findGit: () => "git.exe");

            Assert.Null(git);
            Assert.False(ran);
            Assert.False(string.IsNullOrEmpty(DownloadInstallCore.LastGitError));
        }

        [Fact]
        public async Task DownloadGit_InstallerFails_DoesNotResolveGit()
        {
            bool found = false;
            string git = await DownloadInstallCore.DownloadGitAsync(
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

            Assert.Null(git);
            Assert.False(found);
            Assert.False(string.IsNullOrEmpty(DownloadInstallCore.LastGitError));
        }

        [Fact]
        public async Task DownloadGit_InstallSucceedsButGitNotFound_Fails()
        {
            string git = await DownloadInstallCore.DownloadGitAsync(
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

            Assert.Null(git);
            Assert.False(string.IsNullOrEmpty(DownloadInstallCore.LastGitError));
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
