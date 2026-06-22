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

        // ---- Regression: --export-asset --project must NOT be swallowed by the
        //      bare --project rom-info fallthrough (dispatch-order bug, #1133) ----

        [SkippableFact]
        public void ExportAsset_Palette_Project_ExitsZero_WritesUnderProjectRoot()
        {
            // Use FE8U specifically (the task-specified ROM); the synthetic project
            // manifest forces version FE8U so detection + load is deterministic.
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available for --export-asset --project test");

            string projectDir = NewTempDir("proj_pal");
            try
            {
                // febuilder.project.json: schemaVersion 1, builtRom → copied real ROM, forceVersion FE8U
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\", \"forceVersion\": \"FE8U\" }");
                File.Copy(RomLocator.FE8U!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Project-relative out path: gfx/sample.pal under the project root.
                string outRel = "gfx/sample.pal";
                string args = $"--export-asset --project=\"{projectDir}\" --kind=palette --addr=0x5524 --colors=16 --out={outRel}";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--export-asset --project exited with {code}\nStdout: {stdout}\nStderr: {stderr}");

                // REGRESSION GUARD: the command must route to RunExportAsset (prints "Wrote:"),
                // NOT the rom-info reporter (which prints "Mode: Decomp"/"Symbols:" and exports nothing).
                Assert.Contains("Wrote:", stdout);
                Assert.DoesNotContain("Symbols:", stdout);

                // The asset file must exist under the project root.
                string expectedPal = Path.Combine(projectDir, "gfx", "sample.pal");
                Assert.True(File.Exists(expectedPal),
                    $"Expected exported .pal at {expectedPal}\nStdout: {stdout}");

                string content = File.ReadAllText(expectedPal);
                Assert.StartsWith("JASC-PAL", content);
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

        // ---- --import-asset / --roundtrip-asset (.mar map layout, #1148) ----

        // Build a synthetic .mar body of (rawTile<<3) LE entries + matching sidecar.
        static void WriteSyntheticMar(string marPath, int w, int h)
        {
            byte[] body = new byte[w * h * 2];
            for (int i = 0; i < w * h; i++)
            {
                ushort rawTile = (ushort)(i == w * h - 1 ? 0x1FFF : i); // all < 0x2000
                ushort marTile = (ushort)(rawTile << 3);
                body[i * 2 + 0] = (byte)(marTile & 0xFF);
                body[i * 2 + 1] = (byte)(marTile >> 8);
            }
            File.WriteAllBytes(marPath, body);
            File.WriteAllText(marPath + ".json",
                $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"srcAddr\": \"0x100\",\n  \"format\": \"febuilder-mar-u16-shl3\"\n}}\n");
        }

        [Fact]
        public void ImportAsset_Map_ExitsZero_WritesRawBlob()
        {
            string dir = NewTempDir("import_mar");
            try
            {
                int w = 4, h = 3;
                string marPath = Path.Combine(dir, "chapter.mar");
                WriteSyntheticMar(marPath, w, h);

                string outBin = Path.Combine(dir, "chapter.tmap_raw.bin");
                string args = $"--import-asset --kind=map --in=\"{marPath}\" --out=\"{outBin}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--import-asset exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outBin), $"Expected raw blob at {outBin}");

                // Raw blob = [w][h] + w*h raw u16 LE.
                byte[] raw = File.ReadAllBytes(outBin);
                Assert.Equal(2 + w * h * 2, raw.Length);
                Assert.Equal((byte)w, raw[0]);
                Assert.Equal((byte)h, raw[1]);
                for (int i = 0; i < w * h; i++)
                {
                    ushort expected = (ushort)(i == w * h - 1 ? 0x1FFF : i);
                    ushort actual = (ushort)(raw[2 + i * 2] | (raw[2 + i * 2 + 1] << 8));
                    Assert.Equal(expected, actual);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void RoundtripAsset_Map_CleanMar_ExitsZero()
        {
            string dir = NewTempDir("rt_ok");
            try
            {
                string marPath = Path.Combine(dir, "chapter.mar");
                WriteSyntheticMar(marPath, 4, 3);

                string args = $"--roundtrip-asset --kind=map --in=\"{marPath}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--roundtrip-asset exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("Round-trip OK", stdout);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void RoundtripAsset_Map_CorruptMar_ExitsTwo()
        {
            string dir = NewTempDir("rt_bad");
            try
            {
                int w = 2, h = 2;
                string marPath = Path.Combine(dir, "chapter.mar");
                WriteSyntheticMar(marPath, w, h);

                // Corrupt: set a low bit so the <<3 invariant is broken.
                byte[] body = File.ReadAllBytes(marPath);
                body[0] |= 1;
                File.WriteAllBytes(marPath, body);

                string args = $"--roundtrip-asset --kind=map --in=\"{marPath}\"";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ImportAsset_NonMapKind_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--import-asset --kind=palette --in=x.pal --out=x.bin");
            Assert.NotEqual(0, code);
        }

        // ---- Path rejection: --import-asset --out must stay inside the project root (#1148).
        //      ROM-FREE: --project containment uses DecompProjectDetector.Detect (no ROM load). ----

        [Fact]
        public void ImportAsset_Map_OutEscapesProject_ExitsTwo()
        {
            string projectDir = NewTempDir("import_escape");
            try
            {
                // Valid decomp manifest → Detect accepts it (no built ROM needed); containment applies.
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");

                string marPath = Path.Combine(projectDir, "chapter.mar");
                WriteSyntheticMar(marPath, 2, 2);

                // ..-escaping --out must be rejected (exit 2), NO blob written outside the tree.
                string escapingOut = "../outside.tmap_raw.bin";
                string args = $"--import-asset --kind=map --project=\"{projectDir}\" --in=\"{marPath}\" --out={escapingOut}";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.False(File.Exists(Path.Combine(projectDir, "..", "outside.tmap_raw.bin")),
                    "no blob must be written outside the project root");
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- Containment safety: an unbuilt-but-VALID decomp project still enforces --out
        //      containment (#1148 Copilot finding: it must NOT fall back to cwd resolution and
        //      let --out escape the tree). Detect accepts a valid manifest even with no built ROM. ----

        [Fact]
        public void ImportAsset_Map_UnbuiltProject_OutEscapes_ExitsTwo_NoEscapeWrite()
        {
            // The manifest points at a builtRom that does NOT exist — the project is unbuilt but
            // still a VALID decomp root. Containment must still apply: an escaping --out → exit 2.
            string projectDir = NewTempDir("import_unbuilt");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"does_not_exist.gba\" }");

                string marPath = Path.Combine(projectDir, "chapter.mar");
                WriteSyntheticMar(marPath, 2, 2);

                string escapingOut = "../escaped_unbuilt.tmap_raw.bin";
                string args = $"--import-asset --kind=map --project=\"{projectDir}\" --in=\"{marPath}\" --out={escapingOut}";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.False(File.Exists(Path.Combine(projectDir, "..", "escaped_unbuilt.tmap_raw.bin")),
                    "an unbuilt-but-valid project must NOT let --out escape the project root");
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- An unbuilt-but-VALID project still writes a project-relative --out successfully
        //      (ROM-free import does not require a built ROM). ----

        [Fact]
        public void ImportAsset_Map_UnbuiltProject_RelativeOut_ExitsZero_WritesUnderProjectRoot()
        {
            string projectDir = NewTempDir("import_unbuilt_ok");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"does_not_exist.gba\" }");

                string marPath = Path.Combine(projectDir, "chapter.mar");
                WriteSyntheticMar(marPath, 4, 3);

                string outRel = "map/chapter.tmap_raw.bin";
                string args = $"--import-asset --kind=map --project=\"{projectDir}\" --in=\"{marPath}\" --out={outRel}";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"ROM-free import of an unbuilt project should succeed (exit {code})\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(Path.Combine(projectDir, "map", "chapter.tmap_raw.bin")),
                    "raw blob must be written under the project root");
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- Not a decomp project at all → exit 2, no containment to enforce, no write. ----

        [Fact]
        public void ImportAsset_Map_NonProjectDir_ExitsTwo()
        {
            string dir = NewTempDir("import_nonproject");
            try
            {
                // No manifest and nothing decomp-like → Detect returns null → exit 2.
                string marPath = Path.Combine(dir, "chapter.mar");
                WriteSyntheticMar(marPath, 2, 2);

                string args = $"--import-asset --kind=map --project=\"{dir}\" --in=\"{marPath}\" --out=out.bin";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Dispatch order: --import-asset --project must NOT be swallowed by the bare
        //      --project rom-info fallthrough (#1148, same hazard as --export-asset #1133). ----

        [Fact]
        public void ImportAsset_Map_Project_NotSwallowedByRomInfo_WritesUnderProjectRoot()
        {
            string projectDir = NewTempDir("import_dispatch");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");

                string marPath = Path.Combine(projectDir, "chapter.mar");
                WriteSyntheticMar(marPath, 4, 3);

                // Project-relative out path under the project root.
                string outRel = "map/chapter.tmap_raw.bin";
                string args = $"--import-asset --kind=map --project=\"{projectDir}\" --in=\"{marPath}\" --out={outRel}";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--import-asset --project was swallowed or failed (exit {code})\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(Path.Combine(projectDir, "map", "chapter.tmap_raw.bin")),
                    "raw blob must be written under the project root");
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }
    }
}
