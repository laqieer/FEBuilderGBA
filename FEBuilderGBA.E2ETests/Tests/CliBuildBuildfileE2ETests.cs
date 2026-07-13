// SPDX-License-Identifier: GPL-3.0-or-later
// E2E coverage for #1936 — the independent buildfile CONSUMER verbs on FEBuilderGBA.CLI:
//   --build-buildfile --clean=<clean> --project=<recipe> --out=<new-rom>
//   --buildfile-roundtrip --rom=<modified> --clean=<clean> [--force-version]
//
// Argument-validation and help/dispatch scenarios need no ROM and run as plain facts. The
// reconstruction/round-trip scenarios are ROM-gated on a temporary copy of roms/FE8U.gba via
// RomLocator and SkippableFact (the same pattern as CliExportBuildfileE2ETests), so they run in
// the scheduled/manual E2E: FE8U workflow when the private ROM secret is present and skip in
// public/no-ROM jobs. No ROM or secret is added to the repository.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public class CliBuildBuildfileE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempPaths = new();

        public void Dispose()
        {
            foreach (var p in _tempPaths)
            {
                try
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) File.Delete(p);
                }
                catch { }
            }
        }

        private string TempPath(string suffix)
        {
            string p = Path.Combine(Path.GetTempPath(), $"febuilder_bfb_{Guid.NewGuid():N}{suffix}");
            _tempPaths.Add(p);
            return p;
        }

        private string CopyFE8U()
        {
            string tempRom = TempPath(".gba");
            File.Copy(RomLocator.FE8U!, tempRom);
            return tempRom;
        }

        // Build a modded FE8U copy: known in-clean edits + a modest sparse extension. Keeps the
        // header/game-code region intact so it still detects as FE8U.
        private static byte[] MakeModded(byte[] clean)
            => BuildfileRomFixture.CreateModdedCopy(clean);

        private string ExportProject(string moddedPath, string cleanPath)
        {
            string outDir = TempPath("_proj");
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedPath}\" --clean=\"{cleanPath}\" --out=\"{outDir}\"",
                timeoutMs: 120_000);
            Assert.True(code == 0, $"--export-buildfile exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            return outDir;
        }

        private static string FirstPayloadFile(string projectDir)
            => Directory.GetFiles(Path.Combine(projectDir, "data"))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .First();

        // ------------------------------------------------------- argument validation (no ROM)

        [Fact]
        public void BuildBuildfile_MissingClean_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--build-buildfile --project=proj --out=out.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--clean", stderr);
        }

        [Fact]
        public void BuildBuildfile_MissingProject_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--build-buildfile --clean=clean.gba --out=out.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--project", stderr);
        }

        [Fact]
        public void BuildBuildfile_MissingOut_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--build-buildfile --clean=clean.gba --project=proj", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--out", stderr);
        }

        [Fact]
        public void BuildBuildfile_UnknownFlag_Returns1_NoOutput()
        {
            string outFile = TempPath(".gba");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=clean.gba --project=proj --out=\"{outFile}\" --force-version=FE8U",
                timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("unknown option", stderr);
            Assert.False(File.Exists(outFile));
        }

        [Fact]
        public void BuildBuildfile_ParentTraversalClean_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--build-buildfile --clean=../evil.gba --project=proj --out=out.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("parent-directory", stderr);
        }

        [Fact]
        public void BuildBuildfile_NonexistentClean_Returns1()
        {
            string outFile = TempPath(".gba");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{Path.Combine(Path.GetTempPath(), "nope.gba")}\" --project=\"{Path.GetTempPath()}\" --out=\"{outFile}\"",
                timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.False(File.Exists(outFile));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BuildBuildfile_OutputAtOrInsideData_Returns1_WithoutMutatingProject(
            bool nested)
        {
            string parent = TempPath("_output_guard");
            string projectDir = Path.Combine(parent, "project");
            string dataDir = Path.Combine(projectDir, "data");
            Directory.CreateDirectory(dataDir);
            string cleanPath = Path.Combine(parent, "clean.gba");
            File.WriteAllBytes(cleanPath, new byte[] { 0 });
            string outParent = nested ? Path.Combine(dataDir, "nested") : dataDir;
            Directory.CreateDirectory(outParent);
            string outFile = Path.Combine(outParent, "rebuilt.gba");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("authoritative project data directory", stderr);
            Assert.False(File.Exists(outFile));
            Assert.Empty(Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void BuildfileRoundtrip_MissingRom_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--buildfile-roundtrip --clean=clean.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void BuildfileRoundtrip_MissingClean_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--buildfile-roundtrip --rom=modified.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--clean", stderr);
        }

        [Fact]
        public void BuildfileRoundtrip_UnknownFlag_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--buildfile-roundtrip --rom=modified.gba --clean=clean.gba --project=x", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("unknown option", stderr);
        }

        [Fact]
        public void BuildfileRoundtrip_SameCleanAndModified_Returns1()
        {
            string shared = TempPath(".gba");
            File.WriteAllBytes(shared, new byte[64]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--buildfile-roundtrip --rom=\"{shared}\" --clean=\"{shared}\"", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("same file", stderr);
        }

        [Fact]
        public void Help_DocumentsBuildVerbs()
        {
            var (_, stdout, _) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            Assert.Contains("--build-buildfile", stdout);
            Assert.Contains("--buildfile-roundtrip", stdout);
        }

        [Fact]
        public void BuildVerbs_GlobalHelpAndVersion_TakePrecedence()
        {
            var (hCode, hOut, _) = AppRunner.Run(CliExe, "--build-buildfile --help", timeoutMs: 15_000);
            Assert.Equal(0, hCode);
            Assert.Contains("--build-buildfile", hOut);

            var (rCode, rOut, _) = AppRunner.Run(CliExe, "--buildfile-roundtrip --version", timeoutMs: 15_000);
            Assert.Equal(0, rCode);
            Assert.Contains("Version", rOut, StringComparison.OrdinalIgnoreCase);
        }

        // --------------------------------------------------------------- ROM-gated scenarios

        [SkippableFact]
        public void BuildBuildfile_ReconstructsModdedRomExactly()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);

            string projectDir = ExportProject(moddedPath, cleanPath);
            string outFile = TempPath(".gba");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.True(code == 0, $"--build-buildfile exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outFile), "rebuilt ROM missing");
            Assert.True(File.ReadAllBytes(outFile).SequenceEqual(modded),
                "independently rebuilt ROM did not match the modded ROM exactly");
        }

        [SkippableFact]
        public void BuildBuildfile_PreexistingOut_Returns1_NoOverwrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);
            string projectDir = ExportProject(moddedPath, cleanPath);

            string outFile = TempPath(".gba");
            File.WriteAllText(outFile, "precious");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.Equal(1, code);
            Assert.Equal("precious", File.ReadAllText(outFile));
        }

        [SkippableFact]
        public void BuildBuildfile_TamperedManifest_Returns1_NoOutput()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);
            string projectDir = ExportProject(moddedPath, cleanPath);

            // Corrupt the authoritative manifest.
            File.WriteAllText(Path.Combine(projectDir, "buildfile.json"), "{ not a valid recipe ");
            string outFile = TempPath(".gba");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.Equal(1, code);
            Assert.False(File.Exists(outFile));
        }

        [SkippableFact]
        public void BuildBuildfile_TamperedPayload_Returns1_NoOutput()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);
            string projectDir = ExportProject(moddedPath, cleanPath);

            string payload = FirstPayloadFile(projectDir);
            byte[] bytes = File.ReadAllBytes(payload);
            bytes[0] ^= 0xFF;
            File.WriteAllBytes(payload, bytes);
            string outFile = TempPath(".gba");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.Equal(1, code);
            Assert.False(File.Exists(outFile));
        }

        [SkippableFact]
        public void BuildBuildfile_DeclaredTargetIdentityDrift_Returns1_NoOutput()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);
            string projectDir = ExportProject(moddedPath, cleanPath);

            string manifestPath = Path.Combine(projectDir, "buildfile.json");
            JsonObject root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            root["target"]!.AsObject()["sha256"] = new string('0', 64);
            File.WriteAllText(manifestPath, root.ToJsonString());
            string outFile = TempPath(".gba");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{cleanPath}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.Equal(1, code);
            Assert.Contains("declared target identity", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outFile));
        }

        [SkippableFact]
        public void BuildBuildfile_WrongCleanRom_Returns1_NoOutput()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);
            string projectDir = ExportProject(moddedPath, cleanPath);

            // A DIFFERENT clean ROM (one byte changed) must not satisfy the recipe's identity.
            string wrongClean = TempPath(".gba");
            byte[] wrong = (byte[])clean.Clone();
            wrong[0x50000] ^= 0x5A;
            File.WriteAllBytes(wrongClean, wrong);
            string outFile = TempPath(".gba");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--build-buildfile --clean=\"{wrongClean}\" --project=\"{projectDir}\" --out=\"{outFile}\"",
                timeoutMs: 120_000);

            Assert.Equal(1, code);
            Assert.False(File.Exists(outFile));
        }

        [SkippableFact]
        public void BuildfileRoundtrip_ExactReproduction_Returns0()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--buildfile-roundtrip --rom=\"{moddedPath}\" --clean=\"{cleanPath}\"",
                timeoutMs: 180_000);

            Assert.True(code == 0, $"--buildfile-roundtrip exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.Contains("Round-trip OK", stdout);
        }

        [SkippableFact]
        public void BuildfileRoundtrip_ExactRun_LeavesInputsUnmodified_AndVerifiesScratchCleanup()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);
            byte[] modded = MakeModded(clean);
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);

            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--buildfile-roundtrip --rom=\"{moddedPath}\" --clean=\"{cleanPath}\"",
                timeoutMs: 180_000);

            // Exit 0 is only returned after the handler removed AND verified its private scratch
            // tree, so a successful run proves no project/output was left behind.
            Assert.Equal(0, code);
            Assert.Contains("Round-trip OK", stdout);
            // Neither input ROM is mutated by the round-trip.
            Assert.True(File.ReadAllBytes(cleanPath).SequenceEqual(clean), "clean ROM was modified");
            Assert.True(File.ReadAllBytes(moddedPath).SequenceEqual(modded), "modified ROM was modified");
        }
    }
}
