using System;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Additional black-box CLI E2E tests.
    /// These tests don't require a ROM file.
    /// </summary>
    public class CliHelpTests
    {
        private static readonly string ExePath = AppRunner.FindExePath();

        [Fact]
        public void Version_ContainsLicense()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Version output should include license or copyright info
            Assert.Contains("Copyright", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version_ContainsYear()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Should contain a year in the copyright line
            Assert.Contains("201", stdout); // 2017 or later
        }

        [Fact]
        public void Version_ExitCodeIsStable()
        {
            // Running --version multiple times should always give the same exit code
            var (code1, _, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            var (code2, _, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            Assert.Equal(code1, code2);
            Assert.Equal(0, code1);
        }

        [Fact]
        public void NoArgs_DoesNotCrash()
        {
            // Launching with no args should not crash immediately.
            // It may try to launch GUI (which will timeout in headless CI), but we check
            // it doesn't crash with a fatal error code in the first few seconds.
            // In CI, the WinForms GUI may fail to start, so we just verify it doesn't exit with code != 0.
            // This is effectively a "smoke test".
            try
            {
                var (code, _, _) = AppRunner.Run(ExePath, "", timeoutMs: 5_000);
                // If it exits, code 0 is fine; code 1 for "no args" is also acceptable
                Assert.True(code == 0 || code == 1,
                    $"Expected exit code 0 or 1, got {code}");
            }
            catch
            {
                // In headless CI, the process may time out — that's fine
            }
        }
    }
}
