using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests for the Avalonia GUI editor views.
    /// Launches the Avalonia app with --rom and --smoke-test flags to verify
    /// that selecting items in the Unit and Item editor lists does not crash.
    /// </summary>
    public class AvaloniaEditorSmokeTests
    {
        private static readonly string? ExePath = AvaloniaAppRunner.FindExePath();

        // ------------------------------------------------------------------ Unit + Item editor selection

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_EditorListSelection_DoesNotCrash(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            // Launch with --rom and --smoke-test:
            // App loads ROM → opens Unit + Item editors → selects first list item → exits
            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --smoke-test", timeoutMs: 60_000);

            Assert.True(exitCode == 0,
                $"{romName}: Avalonia smoke test failed with exit code {exitCode}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }

        // ------------------------------------------------------------------ ROM load only

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_RomLoad_DoesNotCrash(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            // Just load the ROM and run smoke test
            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!, $"--rom \"{romPath}\" --smoke-test", timeoutMs: 60_000);

            Assert.True(exitCode == 0,
                $"{romName}: Avalonia ROM load + smoke test failed with exit code {exitCode}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }
    }
}
