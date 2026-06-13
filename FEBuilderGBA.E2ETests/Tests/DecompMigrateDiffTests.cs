using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests for the decomp diff-to-source migration assistant CLI (#1131):
    ///   FEBuilderGBA.CLI --migrate-diff --project=&lt;dir&gt; --rom2=&lt;editedRom&gt; [--out=report.tsv]
    ///
    /// Each test builds a synthetic project copying a REAL FE6 ROM as the built ROM
    /// (so LoadProject -> InitFull succeeds), plus an edited copy mutated at a known
    /// offset where a .map places a covering symbol. Tests skip when no FE6 ROM is
    /// available. ADVISORY + READ-ONLY: the command never writes the ROM or source.
    /// </summary>
    public class DecompMigrateDiffTests
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();

        private static (int ExitCode, string Stdout, string Stderr) RunWithRetry(
            string args, int timeoutMs = 60_000, int maxAttempts = 2)
        {
            (int ExitCode, string Stdout, string Stderr) result = default;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = AppRunner.Run(CliExe, args, timeoutMs);
                if (result.ExitCode >= 0)
                    return result;
            }
            return result;
        }

        private static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"migdiff_e2e_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Build a BUILT project (real ROM as synth.gba + a .map with a covering
        // symbol). Returns (dir, builtRomPath, knownOffset, symbolName).
        private static (string Dir, string BuiltRom, int Offset, string Symbol) MakeProject(string romPath)
        {
            string dir = NewTempDir("built");
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"),
                "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
            File.WriteAllText(Path.Combine(dir, "Makefile"), "ROM := synth.gba\n");
            File.WriteAllText(Path.Combine(dir, "synth.sha1"), "00\n");
            string built = Path.Combine(dir, "synth.gba");
            File.Copy(romPath, built, overwrite: true);

            // Symbol gMigrateData @ 0x08010000 (file off 0x10000) covering a 0x40 span.
            File.WriteAllText(Path.Combine(dir, "synth.map"), string.Join("\n", new[]
            {
                " .rodata        0x08010000       0x40 build/src/migrate.o",
                "                0x08010000                gMigrateData",
            }));
            return (dir, built, 0x10000, "gMigrateData");
        }

        // Make an edited copy of a ROM with `len` bytes flipped at `offset`.
        private static string MakeEditedCopy(string builtRom, int offset, int len)
        {
            string edited = builtRom + ".edited.gba";
            byte[] data = File.ReadAllBytes(builtRom);
            for (int i = 0; i < len && offset + i < data.Length; i++)
                data[offset + i] ^= 0xFF;
            File.WriteAllBytes(edited, data);
            return edited;
        }

        [SkippableFact]
        public void MigrateDiff_ClassifiesChangedRangeToSymbol()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            var (dir, built, off, sym) = MakeProject(RomLocator.FE6!);
            string edited = MakeEditedCopy(built, off, 4);
            try
            {
                var (code, stdout, stderr) = RunWithRetry($"--migrate-diff --project=\"{dir}\" --rom2=\"{edited}\"");
                string combined = stdout + stderr;

                Assert.True(code == 0,
                    $"--migrate-diff exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                // The changed range is attributed to the covering decomp symbol.
                Assert.Contains(sym, stdout);
                Assert.Contains("changed", stdout, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally
            {
                try { File.Delete(edited); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void MigrateDiff_WritesTsvReport()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            var (dir, built, off, sym) = MakeProject(RomLocator.FE6!);
            string edited = MakeEditedCopy(built, off, 4);
            string outTsv = Path.Combine(dir, "report.tsv");
            try
            {
                var (code, stdout, stderr) = RunWithRetry(
                    $"--migrate-diff --project=\"{dir}\" --rom2=\"{edited}\" --out=\"{outTsv}\"");

                Assert.True(code == 0,
                    $"--migrate-diff --out exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outTsv), "TSV report file was not written");
                string tsv = File.ReadAllText(outTsv);
                Assert.Contains("StartAddr", tsv);          // header present
                Assert.Contains(sym, tsv);                  // symbol classified in TSV
            }
            finally
            {
                try { File.Delete(edited); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void MigrateDiff_IdenticalRoms_ReportsNoMigrationNeeded()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            var (dir, built, _, _) = MakeProject(RomLocator.FE6!);
            // rom2 == the built ROM itself → identical.
            try
            {
                var (code, stdout, stderr) = RunWithRetry($"--migrate-diff --project=\"{dir}\" --rom2=\"{built}\"");

                Assert.True(code == 0,
                    $"--migrate-diff identical exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("identical", stdout, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // The closest first-class ROM available (FE8U preferred, else FE6), or null.
        private static string? AnyRom => RomLocator.FE8U ?? RomLocator.FE6;

        // Build a project whose built ROM is a COPY of a real FE ROM (so
        // LoadProject -> InitFull, incl. the Huffman-tree build, succeeds even in a
        // Debug CI build) driven through the manifest `forceVersion` path. CI
        // provides the ROMs via ROMS_DIR, so this positive --migrate-diff path runs
        // in CI (Copilot PR #1139 finding 3). The .map places a covering symbol at a
        // known offset. Returns (dir, builtRom, knownOffset, symbol).
        private static (string Dir, string BuiltRom, int Offset, string Symbol) MakeForceVersionProject(string romPath)
        {
            bool isFe8u = string.Equals(romPath, RomLocator.FE8U, StringComparison.OrdinalIgnoreCase);
            string force = isFe8u ? "FE8U" : "FE6";
            string dir = NewTempDir("forcever");
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"),
                $"{{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"forceVersion\": \"{force}\" }}");
            string built = Path.Combine(dir, "synth.gba");
            File.Copy(romPath, built, overwrite: true);
            // gMigrateData @ 0x08010000 (file off 0x10000) covering a 0x40 span.
            File.WriteAllText(Path.Combine(dir, "synth.map"), string.Join("\n", new[]
            {
                " .rodata        0x08010000       0x40 build/src/migrate.o",
                "                0x08010000                gMigrateData",
            }));
            return (dir, built, 0x10000, "gMigrateData");
        }

        [SkippableFact]
        public void MigrateDiff_ForceVersionProject_ClassifiesRange()
        {
            Skip.If(AnyRom == null, "No FE ROM available");

            var (dir, built, off, sym) = MakeForceVersionProject(AnyRom!);
            string edited = MakeEditedCopy(built, off, 4);
            string outTsv = Path.Combine(dir, "report.tsv");
            try
            {
                var (code, stdout, stderr) = RunWithRetry(
                    $"--migrate-diff --project=\"{dir}\" --rom2=\"{edited}\" --out=\"{outTsv}\"");
                string combined = stdout + stderr;

                Assert.True(code == 0,
                    $"--migrate-diff forceVersion exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                // The real load/analyze/TSV path executed via the forceVersion manifest.
                Assert.Contains(sym, stdout);
                Assert.True(File.Exists(outTsv), "TSV report was not written");
                Assert.Contains(sym, File.ReadAllText(outTsv));
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally
            {
                try { File.Delete(edited); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void MigrateDiff_OutWriteFails_ReturnsNonZero()
        {
            Skip.If(AnyRom == null, "No FE ROM available");

            // Requesting --out to an unwritable path (a directory) must fail with a
            // non-zero exit (Copilot PR #1139 finding 4), not a silent success.
            var (dir, built, off, _) = MakeForceVersionProject(AnyRom!);
            string edited = MakeEditedCopy(built, off, 4);
            try
            {
                // --out points at the project DIRECTORY itself → write throws.
                var (code, stdout, stderr) = RunWithRetry(
                    $"--migrate-diff --project=\"{dir}\" --rom2=\"{edited}\" --out=\"{dir}\"");
                string combined = stdout + stderr;

                Assert.True(code != 0,
                    $"expected non-zero exit on unwritable --out, got {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("could not write report", combined);
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally
            {
                try { File.Delete(edited); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void MigrateDiff_NoProject_FailsGracefully()
        {
            // No --project → usage error, exit 1, no crash. No ROM needed.
            var (code, stdout, stderr) = RunWithRetry("--migrate-diff --rom2=does-not-matter.gba");
            string combined = stdout + stderr;

            Assert.True(code != 0, $"expected non-zero exit, got {code}");
            Assert.Contains("--project", combined);
            Assert.DoesNotContain("Unhandled exception", combined);
        }

        [Fact]
        public void MigrateDiff_MissingRom2_FailsGracefully()
        {
            string dir = NewTempDir("noeromtwo");
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"),
                "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
            try
            {
                var (code, stdout, stderr) = RunWithRetry($"--migrate-diff --project=\"{dir}\"");
                string combined = stdout + stderr;

                Assert.True(code != 0, $"expected non-zero exit, got {code}");
                Assert.Contains("--rom2", combined);
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
