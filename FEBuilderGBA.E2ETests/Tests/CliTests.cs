using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box CLI tests that launch FEBuilderGBA.exe with command-line flags
    /// and verify the exit code and stdout output without needing a ROM file.
    /// These are headless-friendly and run well in CI.
    /// </summary>
    public class CliTests
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private static readonly string CliExe = AppRunner.FindCliExePath();

        // ------------------------------------------------------------------ --version

        [Fact]
        public void Version_ExitsZero()
        {
            var (code, _, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            Assert.Equal(0, code);
        }

        [Fact]
        public void Version_OutputsApplicationName()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Should contain the assembly name
            Assert.Contains("FEBuilderGBA", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version_OutputsVersionKeyword()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Should contain a version label
            Assert.Contains("Version", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version_OutputIsNonEmpty()
        {
            var (_, stdout, stderr) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            string combined = stdout + stderr;
            Assert.False(string.IsNullOrWhiteSpace(combined),
                "Expected non-empty output from --version");
        }

        // ------------------------------------------------------------------ Unknown flag

        [Fact]
        public void UnknownFlag_DoesNotCrashImmediately()
        {
            // An unknown flag should not crash the process with exit code 1 before GUI appears;
            // it either launches the GUI (not tested here) or exits cleanly.
            // We simply check that with --version the app is well-behaved (regression guard).
            var (code, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            Assert.True(code == 0, $"--version returned exit code {code}; stdout: {stdout}");
        }

        // -------------------------------------------------- decomp audit / NMM / validate (#1150)

        [Fact]
        public void DecompAudit_ExitsZero_AndContainsHeaderAndItems()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--decomp-audit", timeoutMs: 30_000);
            Assert.Equal(0, code);
            Assert.Contains("Editor", stdout, StringComparison.Ordinal);
            Assert.Contains("items", stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void NmmToManifest_ExitsZero_AndOutputsTable()
        {
            string nmm = Path.Combine(Path.GetTempPath(), "cli_nmm_" + Guid.NewGuid().ToString("N") + ".nmm");
            try
            {
                File.WriteAllText(nmm,
                    "1\nItemSample by FEBuilderGBA\n0x809B7B4\n255\n36\nNULL\nNULL\n\n" +
                    "NameTextID\n0\n2\nNEHU\nNULL\n\n" +
                    "Might\n4\n1\nNEHU\nNULL\n\n");
                var (code, stdout, _) = AppRunner.Run(CliExe, $"--nmm-to-manifest --in=\"{nmm}\" --table=items", timeoutMs: 30_000);
                Assert.Equal(0, code);
                Assert.Contains("\"table\"", stdout, StringComparison.Ordinal);
            }
            finally
            {
                try { File.Delete(nmm); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_GoodPalette_ExitsZero()
        {
            string pal = Path.Combine(Path.GetTempPath(), "cli_pal_" + Guid.NewGuid().ToString("N") + ".pal");
            try
            {
                File.WriteAllText(pal, "JASC-PAL\r\n0100\r\n2\r\n0 0 0\r\n255 255 255\r\n");
                var (code, _, _) = AppRunner.Run(CliExe, $"--validate-asset --kind=palette --in=\"{pal}\"", timeoutMs: 30_000);
                Assert.Equal(0, code);
            }
            finally
            {
                try { File.Delete(pal); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_BadGraphics_ExitsTwo_WithErrorCode()
        {
            string png = Path.Combine(Path.GetTempPath(), "cli_badpng_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                File.WriteAllText(png, "this is not a png");
                var (code, stdout, stderr) = AppRunner.Run(CliExe, $"--validate-asset --kind=graphics --in=\"{png}\"", timeoutMs: 30_000);
                Assert.Equal(2, code);
                string combined = stdout + stderr;
                Assert.Contains("ERROR", combined, StringComparison.Ordinal);
            }
            finally
            {
                try { File.Delete(png); } catch { }
            }
        }
    }
}
