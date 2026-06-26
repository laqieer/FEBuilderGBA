using System.Text.RegularExpressions;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests that verify EVERY Avalonia GUI editor form (the full list returned by
    /// <c>MainWindow.GetAllEditorFactories()</c>) can be opened without crashing. Uses the
    /// <c>--smoke-test-all</c> flag to iterate through every editor accessible from MainWindow.
    ///
    /// These assertions are deliberately count-agnostic: they validate the smoke runner's own
    /// self-reported numbers for internal consistency instead of pinning a hardcoded editor
    /// count. Adding or removing an editor must NOT require touching a magic literal here
    /// (a stale literal previously broke nightly E2E across all five ROM versions).
    /// </summary>
    public class AvaloniaAllEditorsSmokeTests
    {
        private static readonly string? ExePath = AvaloniaAppRunner.FindExePath();

        // "SMOKE: Testing {editors.Count} editors..."
        private static readonly Regex TestingLine =
            new(@"SMOKE: Testing (\d+) editors", RegexOptions.Compiled);

        // "SMOKE: Results: {passed} passed, {failed} failed out of {editors.Count}"
        private static readonly Regex ResultsLine =
            new(@"SMOKE: Results: (\d+) passed, (\d+) failed out of (\d+)", RegexOptions.Compiled);

        /// <summary>
        /// Opens every editor returned by <c>GetAllEditorFactories()</c> via the
        /// <c>--smoke-test-all</c> flag and verifies none crash on instantiation/show.
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
        /// Validates the smoke test output is internally consistent: it reports a positive
        /// editor count, opens every editor it enumerated, and records zero failures. No fixed
        /// editor count is asserted, so this test stays green when editors are intentionally
        /// added or removed.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_AllEditors_ReportsCorrectCount(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --smoke-test-all", timeoutMs: 300_000);

            string diag = $"{romName}: exitCode={exitCode}\nStdout: {stdout}\nStderr: {stderr}";

            // Process-level success must hold (no crash, no nonzero failure exit).
            Assert.True(exitCode == 0, $"All-editors smoke test exited nonzero.\n{diag}");

            // Parse the "SMOKE: Testing N editors" line.
            Match testing = TestingLine.Match(stdout);
            Assert.True(testing.Success, $"Could not find 'SMOKE: Testing N editors' line.\n{diag}");
            int testedCount = int.Parse(testing.Groups[1].Value);

            // Parse the "SMOKE: Results: P passed, F failed out of T" line.
            Match results = ResultsLine.Match(stdout);
            Assert.True(results.Success, $"Could not find 'SMOKE: Results:' line.\n{diag}");
            int passed = int.Parse(results.Groups[1].Value);
            int failed = int.Parse(results.Groups[2].Value);
            int total = int.Parse(results.Groups[3].Value);

            // Internal consistency — no magic literal:
            //   * at least one editor was exercised (liveness),
            //   * the "Testing N" count matches the "out of T" total,
            //   * every enumerated editor passed,
            //   * nothing failed.
            Assert.True(testedCount > 0, $"Expected at least one editor to be tested.\n{diag}");
            Assert.True(testedCount == total,
                $"'Testing {testedCount}' != 'out of {total}' — runner count mismatch.\n{diag}");
            Assert.True(passed == testedCount,
                $"Only {passed}/{testedCount} editors passed.\n{diag}");
            Assert.True(failed == 0, $"{failed} editor(s) failed.\n{diag}");

            // No per-editor failure list should be emitted.
            Assert.DoesNotContain("SMOKE: Failures:", stdout);
        }
    }
}
