// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FEBuilderGBA;
using FEBuilderGBA.CLI;
using CliProgram = FEBuilderGBA.CLI.Program;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    public sealed class CliRandomMapGeneratorTests : IDisposable
    {
        private readonly string _root;

        public CliRandomMapGeneratorTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "febuildergba-random-map-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [Fact]
        public void Success_WritesExpectedCsv_AndReportsGeneratedSeedAndDefaultAlgorithm()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "map.csv");
            IReadOnlyList<string> capturedArguments = Array.Empty<string>();

            var operations = new RandomMapGeneratorCliOperations
            {
                GenerateSeed = () => 31415926,
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                    "--json",
                }),
                operations,
                (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                {
                    capturedArguments = new List<string>(args);
                    File.WriteAllBytes(
                        FindArgumentValue(capturedArguments, "--output"),
                        new byte[] { 0, 0, 32, 0, 64, 0, 96, 0 });
                    return new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = "ok",
                        Stderr = "",
                    };
                });

            Assert.Equal(0, result.Code);
            Assert.Equal("", result.Stderr);
            using JsonDocument document = JsonDocument.Parse(result.Stdout.Trim());
            JsonElement root = document.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("generate-random-map", root.GetProperty("command").GetString());
            Assert.Equal(femapCreatorPath, root.GetProperty("femapcreator").GetString());
            Assert.Equal("Grassland", root.GetProperty("tileset").GetString());
            Assert.Equal(RandomMapGeneratorAlgorithms.Default, root.GetProperty("algorithm").GetString());
            Assert.Equal(2, root.GetProperty("width").GetInt32());
            Assert.Equal(2, root.GetProperty("height").GetInt32());
            Assert.Equal(31415926, root.GetProperty("seed").GetInt32());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("assetsDir").ValueKind);
            Assert.Equal(outPath, root.GetProperty("out").GetString());

            Assert.Equal(
                RandomMapGeneratorAlgorithms.Default,
                FindArgumentValue(capturedArguments, "--algorithm"));
            Assert.Equal("31415926", FindArgumentValue(capturedArguments, "--seed"));

            byte[] expectedMapData = { 2, 2, 0, 0, 4, 0, 8, 0, 12, 0 };
            string expectedCsv = MapExportCsv.Serialize(expectedMapData);
            Assert.Equal(expectedCsv, File.ReadAllText(outPath));
        }

        [Fact]
        public void ValidationFailure_DoesNotGenerateOrWrite()
        {
            int generateCalls = 0;
            int writeCalls = 0;
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "missing-width.csv");

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
                Generate = (request, runner) =>
                {
                    generateCalls++;
                    return FailResult("should not run");
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("--width", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("", result.Stdout);
            Assert.Equal(0, generateCalls);
            Assert.Equal(0, writeCalls);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void OutputAliasOfProgram_IsRejectedBeforeDiscoveryOrWrite()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, ".", "FEMapCreator.exe");
            Assert.NotEqual(femapCreatorPath, outPath);
            int discoveryCalls = 0;
            int generateCalls = 0;
            int writeCalls = 0;

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                {
                    discoveryCalls++;
                    return UsableDiscovery("Grassland");
                },
                Generate = (request, runner) =>
                {
                    generateCalls++;
                    return FailResult("must not run");
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("FEMapCreator program", result.Stderr);
            Assert.Equal(0, discoveryCalls);
            Assert.Equal(0, generateCalls);
            Assert.Equal(0, writeCalls);
            Assert.True(File.Exists(femapCreatorPath));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OutputCollidingWithSelectedAsset_IsRejectedBeforeGenerationOrWrite(
            bool collideWithImage)
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string imagePath = CreateEmptyFile("grassland.png");
            string generationDataPath = CreateEmptyFile("grassland.json");
            string outPath = collideWithImage ? imagePath : generationDataPath;
            int generateCalls = 0;
            int writeCalls = 0;

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland", imagePath, generationDataPath),
                Generate = (request, runner) =>
                {
                    generateCalls++;
                    return FailResult("must not run");
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("selected tileset", result.Stderr);
            Assert.Equal(0, generateCalls);
            Assert.Equal(0, writeCalls);
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void UnsupportedAlgorithm_DoesNotGenerateOrWrite()
        {
            int generateCalls = 0;
            int writeCalls = 0;
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "bad-algorithm.csv");

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
                Generate = (request, runner) =>
                {
                    generateCalls++;
                    return FailResult("should not run");
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--algorithm=cellular",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("experimental", result.Stderr);
            Assert.Equal(0, generateCalls);
            Assert.Equal(0, writeCalls);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void GenerationFailure_DoesNotWriteOutput()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "failed.csv");
            int writeCalls = 0;

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
                Generate = (request, runner) => new RandomMapGenerationResult
                {
                    Success = false,
                    ErrorCategory = RandomMapGeneratorErrorCategory.NonZeroExit,
                    ErrorMessage = "FEMapCreator random-map generation exited with code 7.",
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("exited with code 7", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, writeCalls);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void JsonWriteFailure_MapsToErrorObject()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "write-error.csv");

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
                Generate = (request, runner) => new RandomMapGenerationResult
                {
                    Success = true,
                    ErrorCategory = RandomMapGeneratorErrorCategory.None,
                    ErrorMessage = "ok",
                    Mars = new ushort[] { 0, 4, 8, 12 },
                },
                WriteOutputs = outputs => throw new IOException("disk full"),
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                    "--json",
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Equal("", result.Stderr);
            using JsonDocument document = JsonDocument.Parse(result.Stdout.Trim());
            JsonElement root = document.RootElement;
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("generate-random-map", root.GetProperty("command").GetString());
            Assert.Contains("disk full", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void TextWriteFailure_ReturnsErrorWithoutThrowing()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "write-error-text.csv");

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Grassland"),
                Generate = (request, runner) => new RandomMapGenerationResult
                {
                    Success = true,
                    ErrorCategory = RandomMapGeneratorErrorCategory.None,
                    ErrorMessage = "ok",
                    Mars = new ushort[] { 0, 4, 8, 12 },
                },
                WriteOutputs = outputs => throw new IOException("disk full"),
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("disk full", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("", result.Stdout);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void IncompatibleTileset_DoesNotGenerateOrWrite()
        {
            string femapCreatorPath = CreateEmptyFile("FEMapCreator.exe");
            string outPath = Path.Combine(_root, "incompatible.csv");
            int generateCalls = 0;
            int writeCalls = 0;

            var operations = new RandomMapGeneratorCliOperations
            {
                DiscoverTilesets = (path, assetsDir, runner) =>
                    UsableDiscovery("Different Tileset"),
                Generate = (request, runner) =>
                {
                    generateCalls++;
                    return FailResult("should not run");
                },
                WriteOutputs = outputs => writeCalls++,
            };

            var result = Run(
                CliProgram.ParseArgs(new[]
                {
                    "--generate-random-map",
                    "--femapcreator=" + femapCreatorPath,
                    "--tileset=Grassland",
                    "--width=2",
                    "--height=2",
                    "--out=" + outPath,
                }),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("compatible 32-column", result.Stderr);
            Assert.Equal(0, generateCalls);
            Assert.Equal(0, writeCalls);
            Assert.False(File.Exists(outPath));
        }

        private (int Code, string Stdout, string Stderr) Run(
            Dictionary<string, string> argsDic,
            RandomMapGeneratorCliOperations operations,
            ProcessRunnerDelegate runner = null)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = CliProgram.RunGenerateRandomMap(argsDic, operations, runner, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }

        private string CreateEmptyFile(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }

        private static string FindArgumentValue(IEnumerable<string> args, string name)
        {
            string previous = "";
            foreach (string arg in args)
            {
                if (previous == name)
                    return arg;
                previous = arg;
            }
            return "";
        }

        private static RandomMapGenerationResult FailResult(string error)
        {
            return new RandomMapGenerationResult
            {
                Success = false,
                ErrorCategory = RandomMapGeneratorErrorCategory.ParseFailed,
                ErrorMessage = error,
            };
        }

        private static FEMapCreatorTilesetDiscoveryResult UsableDiscovery(
            string name,
            string resolvedImagePath = "",
            string resolvedGenerationDataPath = "")
        {
            var result = new FEMapCreatorTilesetDiscoveryResult
            {
                Success = true,
                ErrorCategory = RandomMapGeneratorErrorCategory.None,
            };
            result.UsableTilesets.Add(new FEMapCreatorTilesetInfo
            {
                Name = name,
                HasImage = true,
                HasGenerationData = true,
                IsCompatible = true,
                ResolvedImagePath = resolvedImagePath,
                ResolvedGenerationDataPath = resolvedGenerationDataPath,
            });
            return result;
        }
    }
}
