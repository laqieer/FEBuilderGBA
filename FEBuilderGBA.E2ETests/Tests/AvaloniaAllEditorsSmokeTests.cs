using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests that verify ALL 323 Avalonia GUI editor forms can be opened
    /// without crashing. Uses --smoke-test-all flag to iterate through every
    /// editor accessible from MainWindow.
    /// </summary>
    public class AvaloniaAllEditorsSmokeTests
    {
        private static readonly string? ExePath = AvaloniaAppRunner.FindExePath();

        /// <summary>
        /// Opens every editor from MainWindow with --smoke-test-all flag.
        /// Verifies all 323 editors can be instantiated and shown without crashing.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_AllEditors_OpenWithoutCrash(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --smoke-test-all", timeoutMs: 300_000);

            // Verify the test ran and completed
            Assert.Contains("SMOKE: Testing", stdout);
            Assert.Contains("SMOKE: Results:", stdout);

            Assert.True(exitCode == 0,
                $"{romName}: All-editors smoke test failed with exit code {exitCode}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }

        /// <summary>
        /// Validates the smoke test output reports the expected number of editors.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_AllEditors_ReportsCorrectCount(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --smoke-test-all", timeoutMs: 300_000);

            // Should report testing 323 editors
            Assert.Contains("SMOKE: Testing 323 editors", stdout);

            // Should report results line
            Assert.Contains("passed", stdout);

            // Verify 0 failures
            Assert.DoesNotContain("SMOKE: Failures:", stdout);
        }
    }
}
