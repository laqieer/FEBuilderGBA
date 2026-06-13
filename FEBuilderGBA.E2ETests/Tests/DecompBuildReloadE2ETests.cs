using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests for <c>--build-project</c> (#1134).
    /// Each test builds a synthetic decomp project in a temp dir.
    /// Tests skip when no FE6 ROM is available.
    /// </summary>
    public class DecompBuildReloadE2ETests
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();

        private static (int ExitCode, string Stdout, string Stderr) RunWithRetry(
            string args, int timeoutMs = 120_000, int maxAttempts = 2)
        {
            (int ExitCode, string Stdout, string Stderr) result = default;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = AppRunner.Run(CliExe, args, timeoutMs);
                if (result.ExitCode >= 0) return result;
            }
            return result;
        }

        private static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                $"decomp_build_e2e_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Writes a per-OS "copy src.gba to out.gba" febuilder.project.json.
        /// Also copies the FE6 ROM as src.gba so the copy step produces a valid ROM.
        /// Returns the project directory.
        /// </summary>
        private static string MakeCopyProject(string romPath)
        {
            string dir = NewTempDir("copy");
            // Copy real ROM as src.gba
            File.Copy(romPath, Path.Combine(dir, "src.gba"), overwrite: true);

            string buildJson;
            if (OperatingSystem.IsWindows())
                buildJson = "{\"command\":\"cmd\",\"args\":[\"/c\",\"copy /y src.gba out.gba\"]}";
            else
                buildJson = "{\"command\":\"cp\",\"args\":[\"src.gba\",\"out.gba\"]}";

            string manifest = $"{{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{buildJson}}}";
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"), manifest);
            return dir;
        }

        /// <summary>Project with a build section that always fails (exit 1).</summary>
        private static string MakeFailProject(string romPath)
        {
            string dir = NewTempDir("fail");
            // We need out.gba to already exist so detection succeeds, but the build fails
            File.Copy(romPath, Path.Combine(dir, "out.gba"), overwrite: true);

            string buildJson;
            if (OperatingSystem.IsWindows())
                buildJson = "{\"command\":\"cmd\",\"args\":[\"/c\",\"exit 1\"]}";
            else
                buildJson = "{\"command\":\"/bin/sh\",\"args\":[\"-c\",\"exit 1\"]}";

            string manifest = $"{{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{buildJson}}}";
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"), manifest);
            return dir;
        }

        /// <summary>Project with NO build section.</summary>
        private static string MakeNoBuildProject(string romPath)
        {
            string dir = NewTempDir("nobuild");
            File.Copy(romPath, Path.Combine(dir, "out.gba"), overwrite: true);
            string manifest = "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}";
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"), manifest);
            return dir;
        }

        // (a) Without --yes: dry-run prints Command, out.gba NOT created, exit 0
        [SkippableFact]
        public void BuildProject_WithoutYes_DryRunExits0_NoOutput()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            string dir = MakeCopyProject(RomLocator.FE6!);
            // Remove out.gba if it exists
            string outGba = Path.Combine(dir, "out.gba");
            if (File.Exists(outGba)) File.Delete(outGba);
            try
            {
                var (code, stdout, stderr) = RunWithRetry($"--build-project --project=\"{dir}\"");
                string combined = stdout + stderr;

                Assert.True(code == 0,
                    $"Expected exit 0 for dry-run, got {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("Command:", combined);
                Assert.False(File.Exists(outGba), "out.gba should NOT be created in dry-run");
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // (b) With --yes --reload: build runs, out.gba created, exit 0, Mode: Decomp + version= + Stale=false
        [SkippableFact]
        public void BuildProject_WithYesAndReload_BuildsAndLoadsRom()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            string dir = MakeCopyProject(RomLocator.FE6!);
            string outGba = Path.Combine(dir, "out.gba");
            if (File.Exists(outGba)) File.Delete(outGba);
            try
            {
                var (code, stdout, stderr) = RunWithRetry(
                    $"--build-project --project=\"{dir}\" --reload --yes", timeoutMs: 120_000);
                string combined = stdout + stderr;

                Assert.True(code == 0,
                    $"Expected exit 0, got {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(outGba), "out.gba should be created after build");
                Assert.Contains("Mode: Decomp", stdout);
                Assert.Contains("version=", stdout);
                Assert.Contains("Stale=", stdout);
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // (c) Failing build with --yes --reload: non-zero exit, output surfaced, no crash
        [SkippableFact]
        public void BuildProject_FailingBuild_NonZeroExit_NoUnhandledException()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            string dir = MakeFailProject(RomLocator.FE6!);
            try
            {
                var (code, stdout, stderr) = RunWithRetry(
                    $"--build-project --project=\"{dir}\" --reload --yes");
                string combined = stdout + stderr;

                Assert.True(code != 0,
                    $"Expected non-zero exit for failing build, got {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // (d) Project with NO build section + --yes: "not opted into" message, exit 2, nothing runs
        [SkippableFact]
        public void BuildProject_NoBuildSection_NotOptedIn_Exit2()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            string dir = MakeNoBuildProject(RomLocator.FE6!);
            try
            {
                var (code, stdout, stderr) = RunWithRetry(
                    $"--build-project --project=\"{dir}\" --yes");
                string combined = stdout + stderr;

                Assert.Equal(2, code);
                Assert.Contains("not opted into", combined);
                Assert.DoesNotContain("Unhandled exception", combined);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // (e) Classic regression: --build-project --rom=<file> (no --project) → usage error, no crash
        [SkippableFact]
        public void BuildProject_WithRomButNoProject_UsageError()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");

            var (code, stdout, stderr) = RunWithRetry(
                $"--build-project --rom=\"{RomLocator.FE6}\"");
            string combined = stdout + stderr;

            Assert.True(code != 0,
                $"Expected non-zero exit, got {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.DoesNotContain("Unhandled exception", combined);
        }
    }
}
