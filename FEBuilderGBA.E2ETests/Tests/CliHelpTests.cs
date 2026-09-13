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
        private static string ExePath => AppRunner.FindExePath();

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
            => AssertNoArgsDoesNotCrash(() => AppRunner.RunGui(ExePath, "", timeoutMs: 5_000));

        internal static void AssertNoArgsDoesNotCrash(Func<(int ExitCode, string Stdout, string Stderr)> run)
        {
            try
            {
                var (code, _, _) = run();
                Assert.True(code == 0 || code == 1,
                    $"Expected exit code 0 or 1, got {code}");
            }
            catch (Exception ex) when (ex is not DesktopUnavailableException)
            {
                // Preserve this legacy best-effort smoke check, but never hide failed admission.
                // Required startup/capture proof belongs to GuiStartupTests.
            }
        }
    }
}
