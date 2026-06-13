using System;
using System.IO;
using System.Text;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for <c>--export-asset</c> CLI command (#1133).
    ///
    /// Tests cover:
    /// - palette export via --rom override (exit 0, JASC header, file written)
    /// - path rejection returning exit 2
    /// - regression: classic --export-palette still works unchanged
    ///
    /// ROM-dependent tests are skipped when no ROM is available.
    /// </summary>
    public class ExportAssetE2ETests
    {
        static readonly string CliExe = AppRunner.FindCliExePath();

        /// <summary>Return the first available ROM, or null if none.</summary>
        static string? FirstRom =>
            RomLocator.FE6 ?? RomLocator.FE7J ?? RomLocator.FE7U ?? RomLocator.FE8J ?? RomLocator.FE8U;

        static (int ExitCode, string Stdout, string Stderr) RunWithRetry(
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

        static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"export_asset_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---- --export-asset --kind=palette via --rom ----

        [SkippableFact]
        public void ExportAsset_Palette_Rom_ExitsZero_WritesJascHeader()
        {
            Skip.If(FirstRom == null, "No ROM available for export-asset palette test");

            string dir = NewTempDir("pal");
            string outPal = Path.Combine(dir, "palette.pal");
            try
            {
                // Use a known-safe palette address in the GBA ROM: 0x5524 is the start
                // of a standard palette region in FE ROMs (or adapt as needed).
                // The test only checks structure, not exact colors.
                string args = $"--export-asset --kind=palette --rom=\"{FirstRom}\" --addr=0x5524 --colors=16 --out=\"{outPal}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--export-asset --kind=palette exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outPal),
                    $"Expected .pal file at {outPal}");

                string content = File.ReadAllText(outPal);
                Assert.StartsWith("JASC-PAL", content);
                Assert.Contains("16", content); // color count line
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportAsset_Palette_PrintsWrotePrefix()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("pal2");
            string outPal = Path.Combine(dir, "palette.pal");
            try
            {
                string args = $"--export-asset --kind=palette --rom=\"{FirstRom}\" --addr=0x5524 --colors=16 --out=\"{outPal}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"exit code {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("Wrote:", stdout);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Map export ----

        [SkippableFact]
        public void ExportAsset_Map_Rom_InvalidAddr_ExitsNonZero()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("map");
            string outMar = Path.Combine(dir, "chapter.mar");
            try
            {
                // Address 0x1 is not a valid LZ77 tilemap — should fail gracefully
                string args = $"--export-asset --kind=map --rom=\"{FirstRom}\" --addr=0x1 --out=\"{outMar}\"";
                var (code, _, _) = RunWithRetry(args);
                Assert.NotEqual(0, code);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Path rejection: exit 2 ----

        [SkippableFact]
        public void ExportAsset_PathRejection_OutsideProject_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string projectDir = NewTempDir("proj");
            try
            {
                // Set up a minimal project
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Use a ..‑escaping out path to trigger path rejection
                string escapingOut = "../outside.pal";
                string args = $"--export-asset --kind=palette --project=\"{projectDir}\" --addr=0x5524 --colors=16 --out={escapingOut}";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- Regression: classic --export-palette still works ----

        [SkippableFact]
        public void ExportPalette_Classic_StillWorks_AfterAddingExportAsset()
        {
            Skip.If(FirstRom == null, "No ROM available for --export-palette regression test");

            string dir = NewTempDir("classic_pal");
            string outPal = Path.Combine(dir, "classic.pal");
            try
            {
                string args = $"--export-palette --rom=\"{FirstRom}\" --addr=0x5524 --colors=16 --out=\"{outPal}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--export-palette exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outPal),
                    $"--export-palette did not write file: {outPal}");

                string content = File.ReadAllText(outPal);
                Assert.StartsWith("JASC-PAL", content);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Missing --kind or --out ----

        [Fact]
        public void ExportAsset_MissingKind_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--export-asset --rom=fake.gba --out=x.pal");
            Assert.NotEqual(0, code);
        }

        [Fact]
        public void ExportAsset_MissingOut_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--export-asset --kind=palette --rom=fake.gba");
            Assert.NotEqual(0, code);
        }
    }
}
