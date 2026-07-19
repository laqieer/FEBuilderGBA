// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public sealed class CliRandomMapGeneratorE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private static readonly string FakeFEMapCreatorDll = FindFakeFEMapCreatorDll();
        private readonly string _root;
        private readonly List<string> _filesToDelete = new();

        public CliRandomMapGeneratorE2ETests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "febuildergba-random-map-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            foreach (string file in _filesToDelete)
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [Fact]
        public void Help_ListsGenerateRandomMap()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("--generate-random-map", stdout);
            Assert.Contains("executable native file on Unix", stdout);
        }

        [Fact]
        public void MissingFemapcreator_ErrorsAndDoesNotCreateOutput()
        {
            string outPath = TempFile("missing-femapcreator.csv");
            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --tileset=Grassland --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--femapcreator", stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void MissingTileset_ErrorsAndDoesNotCreateOutput()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = TempFile("missing-tileset.csv");
            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{femapCreatorPath}\" --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--tileset", stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void MissingWidth_ErrorsAndDoesNotCreateOutput()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = TempFile("missing-width.csv");
            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{femapCreatorPath}\" --tileset=Grassland --height=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--width", stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void MissingHeight_ErrorsAndDoesNotCreateOutput()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = TempFile("missing-height.csv");
            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{femapCreatorPath}\" --tileset=Grassland --width=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--height", stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void MissingOut_Errors()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{femapCreatorPath}\" --tileset=Grassland --width=2 --height=2",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--out", stderr);
        }

        [Theory]
        [InlineData("--width", "0")]
        [InlineData("--width", "-1")]
        [InlineData("--width", "65")]
        [InlineData("--height", "0")]
        [InlineData("--height", "-1")]
        [InlineData("--height", "65")]
        public void InvalidDimensions_AreRejectedBeforeLaunch(string flag, string value)
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = TempFile("invalid-dimension.csv");
            string width = flag == "--width" ? value : "2";
            string height = flag == "--height" ? value : "2";

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{femapCreatorPath}\" --tileset=Grassland --width={width} --height={height} --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains(flag, stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void NonexistentFemapcreator_UsesCorePathValidation()
        {
            string missingPath = Path.Combine(_root, "missing", "FEMapCreator.exe");
            string outPath = TempFile("missing-binary.csv");

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{missingPath}\" --tileset=Grassland --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("does not exist", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void RelativeFemapcreator_IsRejectedBeforeLaunch()
        {
            string outPath = TempFile("relative-binary.csv");

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=FEMapCreator.exe --tileset=Grassland --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("fully qualified and absolute", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void OutputCollidingWithProgram_IsRejectedWithoutModifyingProgram()
        {
            string programCopy = TempFile("FakeFEMapCreator.dll");
            File.Copy(FakeFEMapCreatorDll, programCopy);
            byte[] original = File.ReadAllBytes(programCopy);

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{programCopy}\" --tileset=\"E2E Tileset\" --width=2 --height=2 --out=\"{programCopy}\"",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("FEMapCreator program", stderr);
            Assert.Equal(original, File.ReadAllBytes(programCopy));
        }

        [Fact]
        public void OutputCollidingWithDiscoveredImage_IsRejectedBeforeGeneration()
        {
            string assetsDir = Path.Combine(_root, "assets");
            Directory.CreateDirectory(assetsDir);
            string imagePath = Path.Combine(assetsDir, "e2e.png");

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{FakeFEMapCreatorDll}\" --assets-dir=\"{assetsDir}\" --tileset=\"E2E Tileset\" --width=2 --height=2 --out=\"{imagePath}\"",
                timeoutMs: 30_000,
                environment: FakeEnvironment("success", TempFile("collision-working-dir.txt")));

            Assert.NotEqual(0, code);
            Assert.Contains("selected tileset image asset", stderr);
            Assert.True(File.Exists(imagePath));
            Assert.Equal(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                File.ReadAllBytes(imagePath)[..8]);
        }

        [Fact]
        public void JsonError_GoesToStdoutAndDoesNotCreateOutput()
        {
            string outPath = TempFile("json-error.csv");
            var (code, stdout, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --tileset=Grassland --width=2 --height=2 --out=\"{outPath}\" --json",
                timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Equal("", stderr);
            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement root = document.RootElement;
            Assert.Equal("generate-random-map", root.GetProperty("command").GetString());
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Contains("--femapcreator", root.GetProperty("error").GetString());
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void Success_RealProcessBoundary_WritesDeterministicCsvAndCleansTemp()
        {
            string outPath = TempFile("success.csv");
            string recordPath = TempFile("success-working-dir.txt");
            var environment = FakeEnvironment("success", recordPath);

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{FakeFEMapCreatorDll}\" --tileset=\"E2E Tileset\" --width=2 --height=2 --seed=42 --out=\"{outPath}\"",
                timeoutMs: 30_000,
                environment: environment);

            Assert.Equal(0, code);
            Assert.Equal("", stderr);
            Assert.Contains("seed=42", stdout);
            Assert.True(File.Exists(outPath));
            string[] lines = File.ReadAllLines(outPath);
            Assert.Equal("# FEBuilderGBA Map Export: width=2, height=2", lines[0]);
            Assert.Equal("0,4", lines[1]);
            Assert.Equal("8,12", lines[2]);
            AssertRecordedTempDirectoryWasRemoved(recordPath);
        }

        [Fact]
        public void ExternalNonZeroExit_RealProcessBoundary_LeavesNoOutputAndCleansTemp()
        {
            string outPath = TempFile("nonzero.csv");
            string recordPath = TempFile("nonzero-working-dir.txt");
            var environment = FakeEnvironment("nonzero", recordPath);

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{FakeFEMapCreatorDll}\" --tileset=\"E2E Tileset\" --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 30_000,
                environment: environment);

            Assert.NotEqual(0, code);
            Assert.Contains("exited with code 7", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intentional external failure", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outPath));
            AssertRecordedTempDirectoryWasRemoved(recordPath);
        }

        [Fact]
        public void MalformedMar_RealProcessBoundary_LeavesNoOutputAndCleansTemp()
        {
            string outPath = TempFile("malformed.csv");
            string recordPath = TempFile("malformed-working-dir.txt");
            var environment = FakeEnvironment("malformed", recordPath);

            var (code, _, stderr) = AppRunner.Run(
                CliExe,
                $"--generate-random-map --femapcreator=\"{FakeFEMapCreatorDll}\" --tileset=\"E2E Tileset\" --width=2 --height=2 --out=\"{outPath}\"",
                timeoutMs: 30_000,
                environment: environment);

            Assert.NotEqual(0, code);
            Assert.Contains("not divisible by 32", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outPath));
            AssertRecordedTempDirectoryWasRemoved(recordPath);
        }

        private static IReadOnlyDictionary<string, string?> FakeEnvironment(
            string mode,
            string recordPath)
        {
            return new Dictionary<string, string?>
            {
                ["FEBUILDERGBA_FAKE_FEMAPCREATOR_MODE"] = mode,
                ["FEBUILDERGBA_FAKE_FEMAPCREATOR_RECORD"] = recordPath,
            };
        }

        private static void AssertRecordedTempDirectoryWasRemoved(string recordPath)
        {
            Assert.True(File.Exists(recordPath));
            string tempDirectory = File.ReadAllText(recordPath).Trim();
            Assert.Contains("FEBuilderGBA-mapgen-", Path.GetFileName(tempDirectory));
            Assert.False(Directory.Exists(tempDirectory));
        }

        private static string FindFakeFEMapCreatorDll()
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null
                && !File.Exists(Path.Combine(directory, "FEBuilderGBA.sln")))
            {
                directory = Path.GetDirectoryName(directory);
            }
            if (directory == null)
                throw new InvalidOperationException("Could not locate FEBuilderGBA.sln.");

            string fakeRoot = Path.Combine(
                directory,
                "FEBuilderGBA.E2ETests",
                "FakeFEMapCreator");
            string[] candidates = Directory.GetFiles(
                fakeRoot,
                "FakeFEMapCreator.dll",
                SearchOption.AllDirectories);
            if (candidates.Length == 0)
                throw new FileNotFoundException("FakeFEMapCreator.dll was not built.");
            Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
            string binSegment =
                Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
            for (int i = candidates.Length - 1; i >= 0; i--)
            {
                string candidate = candidates[i];
                if (candidate.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(
                        Path.GetDirectoryName(candidate)!,
                        "FakeFEMapCreator.runtimeconfig.json")))
                {
                    return candidate;
                }
            }
            throw new FileNotFoundException(
                "Runnable FakeFEMapCreator.dll output was not built.");
        }

        private string TempFile(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            _filesToDelete.Add(path);
            return path;
        }

        private string CreateEmptyFile(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            File.WriteAllBytes(path, Array.Empty<byte>());
            _filesToDelete.Add(path);
            return path;
        }
    }
}
