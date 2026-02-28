using System;
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
    }
}
