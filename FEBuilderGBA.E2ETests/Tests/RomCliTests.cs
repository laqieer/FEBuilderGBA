using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Headless CLI tests that exercise ROM-specific command-line flags
    /// (--lint, --rebuild, --makeups) against each of the 5 real ROM files.
    ///
    /// Tests are automatically skipped when the ROM is not available (no ROMS_DIR
    /// env var set and no local roms/ directory).  In CI, the "Download ROMs" step
    /// populates ROMS_DIR from the ROMS_URL secret.
    /// </summary>
    public class RomCliTests
    {
        private static readonly string ExePath = AppRunner.FindExePath();

        // ------------------------------------------------------------------ --lint

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Lint_CompletesAndProducesOutput(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{romPath}\" --lint", timeoutMs: 120_000);

            string combined = stdout + stderr;
            Assert.True(code >= 0,
                $"{romName}: --lint exited with unexpected code {code}");
            Assert.False(string.IsNullOrWhiteSpace(combined),
                $"{romName}: expected non-empty output from --lint");
        }

        // ------------------------------------------------------------------ --rebuild

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Rebuild_ExitsCleanlyOnTempCopy(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            // Work on a temp copy so the original ROM is never modified
            string tempRom = Path.Combine(
                Path.GetTempPath(),
                $"FEBuilder_rebuild_{romName}_{Guid.NewGuid():N}.gba");
            File.Copy(romPath!, tempRom);
            try
            {
                var (code, _, _) = AppRunner.Run(
                    ExePath, $"--rom \"{tempRom}\" --rebuild", timeoutMs: 300_000);
                Assert.Equal(0, code);
            }
            finally
            {
                try { File.Delete(tempRom); } catch { }
            }
        }

        // ------------------------------------------------------------------ --makeups

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Makeups_ExitsAndCreatesUpsFile(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            string tempRom = Path.Combine(
                Path.GetTempPath(),
                $"FEBuilder_makeups_{romName}_{Guid.NewGuid():N}.gba");
            File.Copy(romPath!, tempRom);
            string expectedUps = Path.ChangeExtension(tempRom, ".ups");
            try
            {
                var (code, _, _) = AppRunner.Run(
                    ExePath, $"--rom \"{tempRom}\" --makeups=\"{expectedUps}\"", timeoutMs: 60_000);
                Assert.Equal(0, code);
                Assert.True(File.Exists(expectedUps),
                    $"{romName}: expected .ups file at {expectedUps}");
            }
            finally
            {
                try { File.Delete(tempRom); } catch { }
                try { File.Delete(expectedUps); } catch { }
            }
        }
    }
}
