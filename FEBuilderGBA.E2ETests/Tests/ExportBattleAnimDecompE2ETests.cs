using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for <c>--export-battle-anim-decomp</c> CLI command (#1363):
    /// export a FEBuilder/FEditor-decoded battle animation as reviewable decomp source
    /// (banim_&lt;TAG&gt;_motion.s) + .pal/.json sidecars. READ-ONLY — the preview ROM is
    /// never mutated.
    ///
    /// Tests cover: --animation-id + --banim-addr export on a real ROM (exit 0,
    /// banim macros + mode table + manifest present, ROM unchanged on disk), argument
    /// validation (missing/both selectors), and a project-mode negative root-confinement
    /// path (out-of-root --out rejected, exit 2). ROM-dependent tests are skipped when
    /// no ROM is available.
    /// </summary>
    public class ExportBattleAnimDecompE2ETests
    {
        static readonly string CliExe = AppRunner.FindCliExePath();

        static string? FE8U => RomLocator.FE8U;
        static string? FirstRom =>
            RomLocator.FE8U ?? RomLocator.FE8J ?? RomLocator.FE7U ?? RomLocator.FE7J ?? RomLocator.FE6;

        static (int ExitCode, string Stdout, string Stderr) Run(string args, int timeoutMs = 60_000)
        {
            (int ExitCode, string Stdout, string Stderr) result = default;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                result = AppRunner.Run(CliExe, args, timeoutMs);
                if (result.ExitCode >= 0) return result;
            }
            return result;
        }

        static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"export_banim_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [SkippableFact]
        public void ExportBanim_AnimationId_Rom_ExitsZero_WritesMacroAsm_AndManifest()
        {
            Skip.If(FirstRom == null, "No ROM available for export-battle-anim-decomp test");

            string dir = NewTempDir("id");
            string outS = Path.Combine(dir, "banim_test_motion.s");
            try
            {
                byte[] romBefore = File.ReadAllBytes(FirstRom!);

                string args = $"--export-battle-anim-decomp --rom=\"{FirstRom}\" --animation-id=1 --out=\"{outS}\"";
                var (code, stdout, stderr) = Run(args);

                Assert.True(code == 0,
                    $"--export-battle-anim-decomp --animation-id exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outS), $"Expected .s file at {outS}");

                string content = File.ReadAllText(outS);
                Assert.Contains(".include \"banim_code.inc\"", content);
                Assert.Contains("_script:", content);
                Assert.Contains("banim_code_end_mode", content);
                Assert.Contains(".data.modes", content);

                // The JSON manifest sidecar is written and lists the manual checklist.
                string manifest = Path.Combine(dir, "banim_test_motion.json");
                Assert.True(File.Exists(manifest), $"Expected manifest at {manifest}");
                string manifestText = File.ReadAllText(manifest);
                Assert.Contains("banim_data[]", manifestText);
                Assert.Contains("\"modeTableWords\": 24", manifestText);

                // READ-ONLY invariant: the source ROM file is unchanged on disk.
                byte[] romAfter = File.ReadAllBytes(FirstRom!);
                Assert.Equal(romBefore.Length, romAfter.Length);
                Assert.Equal(romBefore, romAfter);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportBanim_BanimAddr_FE8U_ExitsZero()
        {
            Skip.If(FE8U == null, "FE8U ROM required for the fixed-address battle-anim test");

            string dir = NewTempDir("addr");
            string outS = Path.Combine(dir, "banim_addr_motion.s");
            try
            {
                // FE8U animation id 0's record base offset (provenance from the id path).
                string args = $"--export-battle-anim-decomp --rom=\"{FE8U}\" --banim-addr=0xC00028 --out=\"{outS}\"";
                var (code, stdout, stderr) = Run(args);

                Assert.True(code == 0,
                    $"--export-battle-anim-decomp --banim-addr exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outS));
                string content = File.ReadAllText(outS);
                Assert.Contains("banim_code_frame", content);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportBanim_MissingSelector_ExitsNonZero()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("nosel");
            string outS = Path.Combine(dir, "banim.s");
            try
            {
                // Neither --animation-id nor --banim-addr.
                string args = $"--export-battle-anim-decomp --rom=\"{FirstRom}\" --out=\"{outS}\"";
                var (code, _, _) = Run(args);
                Assert.True(code != 0, "Expected nonzero exit when no animation selector is given");
                Assert.False(File.Exists(outS), "No .s should be written when args are invalid");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportBanim_BothSelectors_ExitsNonZero()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("bothsel");
            string outS = Path.Combine(dir, "banim.s");
            try
            {
                string args = $"--export-battle-anim-decomp --rom=\"{FirstRom}\" --animation-id=1 --banim-addr=0xC00028 --out=\"{outS}\"";
                var (code, _, _) = Run(args);
                Assert.True(code != 0, "Expected nonzero exit when both selectors are given");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportBanim_ProjectMode_OutOfRootOut_Rejected()
        {
            Skip.If(FirstRom == null, "No ROM available");

            // Build a VALID decomp project so LoadProject SUCCEEDS and the export
            // reaches the --out containment check (Copilot review pattern).
            string projectDir = NewTempDir("proj");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // --out escapes the project root via ".." -> must be REJECTED (exit 2), no write.
                string args = $"--export-battle-anim-decomp --project=\"{projectDir}\" --banim-addr=0xC00028 --out=\"..{Path.DirectorySeparatorChar}escape.s\"";
                var (code, _, _) = Run(args);

                string escapePath = Path.GetFullPath(Path.Combine(projectDir, "..", "escape.s"));
                Assert.Equal(2, code);
                Assert.False(File.Exists(escapePath), "An escaping --out must not write outside the project root");
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }
    }
}
