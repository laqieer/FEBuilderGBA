using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for <c>--export-voicegroup</c> CLI command (#1362):
    /// export a FEBuilder voicegroup (M4A instrument set) as reviewable decomp
    /// source macro asm. READ-ONLY — the preview ROM is never mutated.
    ///
    /// Tests cover: song-id resolution + voicegroup-addr export on a real ROM
    /// (exit 0, .include + voice_ macro present, ROM unchanged on disk), argument
    /// validation (missing/both selectors), and a project-mode negative
    /// root-confinement path (out-of-root --out rejected, exit 2).
    /// ROM-dependent tests are skipped when no ROM is available.
    /// </summary>
    public class ExportVoicegroupE2ETests
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
            string dir = Path.Combine(Path.GetTempPath(), $"export_vg_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [SkippableFact]
        public void ExportVoicegroup_SongId_Rom_ExitsZero_WritesMacroAsm()
        {
            Skip.If(FirstRom == null, "No ROM available for export-voicegroup test");

            string dir = NewTempDir("song");
            string outS = Path.Combine(dir, "voicegroup001.s");
            try
            {
                byte[] romBefore = File.ReadAllBytes(FirstRom!);

                string args = $"--export-voicegroup --rom=\"{FirstRom}\" --song-id=1 --out=\"{outS}\"";
                var (code, stdout, stderr) = Run(args);

                Assert.True(code == 0,
                    $"--export-voicegroup --song-id exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outS), $"Expected .s file at {outS}");

                string content = File.ReadAllText(outS);
                Assert.Contains(".include \"asm/macros/music_voice.inc\"", content);
                Assert.Contains("voicegroup001:", content);
                Assert.Contains("voice_", content);

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
        public void ExportVoicegroup_VoicegroupAddr_FE8U_ExitsZero()
        {
            Skip.If(FE8U == null, "FE8U ROM required for the fixed-address voicegroup test");

            string dir = NewTempDir("addr");
            string outS = Path.Combine(dir, "voicegroup000.s");
            try
            {
                // FE8U song 1's voicegroup base offset (provenance from the song-id path).
                string args = $"--export-voicegroup --rom=\"{FE8U}\" --voicegroup-addr=0x207470 --out=\"{outS}\"";
                var (code, stdout, stderr) = Run(args);

                Assert.True(code == 0,
                    $"--export-voicegroup --voicegroup-addr exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outS));
                string content = File.ReadAllText(outS);
                Assert.Contains("voice_", content);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportVoicegroup_MissingSelector_ExitsNonZero()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("nosel");
            string outS = Path.Combine(dir, "voicegroup.s");
            try
            {
                // Neither --voicegroup-addr nor --song-id.
                string args = $"--export-voicegroup --rom=\"{FirstRom}\" --out=\"{outS}\"";
                var (code, _, stderr) = Run(args);
                Assert.True(code != 0, "Expected nonzero exit when no voicegroup selector is given");
                Assert.False(File.Exists(outS), "No .s should be written when args are invalid");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportVoicegroup_BothSelectors_ExitsNonZero()
        {
            Skip.If(FirstRom == null, "No ROM available");

            string dir = NewTempDir("bothsel");
            string outS = Path.Combine(dir, "voicegroup.s");
            try
            {
                string args = $"--export-voicegroup --rom=\"{FirstRom}\" --song-id=1 --voicegroup-addr=0x207470 --out=\"{outS}\"";
                var (code, _, _) = Run(args);
                Assert.True(code != 0, "Expected nonzero exit when both selectors are given");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [SkippableFact]
        public void ExportVoicegroup_ProjectMode_OutOfRootOut_Rejected()
        {
            Skip.If(FirstRom == null, "No ROM available");

            // Build a VALID decomp project (manifest + a copied built ROM) so
            // LoadProject SUCCEEDS and the export actually reaches the --out
            // containment check — otherwise a bare temp dir fails to load first and
            // the path-escape rejection logic is never exercised (Copilot review).
            string projectDir = NewTempDir("proj");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // --out escapes the project root via ".." -> must be REJECTED (exit 2), no write.
                string args = $"--export-voicegroup --project=\"{projectDir}\" --voicegroup-addr=0x207470 --out=\"..{Path.DirectorySeparatorChar}escape.s\"";
                var (code, _, stderr) = Run(args);

                string escapePath = Path.GetFullPath(Path.Combine(projectDir, "..", "escape.s"));
                // The project loads, then the escaping --out is rejected with exit 2 (path rejected).
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
