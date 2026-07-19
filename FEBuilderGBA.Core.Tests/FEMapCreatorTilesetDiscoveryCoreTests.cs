using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class FEMapCreatorTilesetDiscoveryCoreTests
    {
        static readonly object EnvLock = new object();

        [Fact]
        public void DiscoverTilesets_UsesDllViaDotnetHostPath()
        {
            string tempRoot = CreateTempDirectory();
            lock (EnvLock)
            {
                string? originalHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                bool hadOriginalHostPath = originalHostPath != null;
                string previousHostPath = originalHostPath ?? "";
                try
                {
                    string femapCreatorDll = CreateEmptyFile(tempRoot, "FEMapCreator.dll");
                    string hostPath = CreateEmptyFile(tempRoot, "dotnet-host.exe");
                    string assetsRoot = Path.Combine(tempRoot, "assets");
                    Directory.CreateDirectory(assetsRoot);
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", hostPath);

                    string observedCommand = "";
                    var observedArgs = new List<string>();
                    FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                        femapCreatorDll,
                        runner: (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                        {
                            observedCommand = command;
                            observedArgs = new List<string>(args);
                            return new ProcessRunResult
                            {
                                Started = true,
                                ExitCode = 0,
                                Stdout = JsonSerializer.Serialize(new
                                {
                                    assetsRoot = assetsRoot,
                                    tilesets = Array.Empty<object>(),
                                }),
                                Stderr = "",
                            };
                        });

                    Assert.True(result.Success, result.ErrorMessage);
                    Assert.Equal(hostPath, observedCommand);
                    Assert.Equal(new[] { femapCreatorDll, "tilesets", "list", "--json" }, observedArgs);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", hadOriginalHostPath ? previousHostPath : null);
                    DeleteDirectoryIfPresent(tempRoot);
                }
            }
        }

        [Fact]
        public void DiscoverTilesets_UsesDllViaBareDotnetFallback()
        {
            string tempRoot = CreateTempDirectory();
            lock (EnvLock)
            {
                string? originalHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                bool hadOriginalHostPath = originalHostPath != null;
                string previousHostPath = originalHostPath ?? "";
                try
                {
                    Environment.SetEnvironmentVariable(
                        "DOTNET_HOST_PATH",
                        Path.Combine(tempRoot, "missing-dotnet-host.exe"));
                    string femapCreatorDll = CreateEmptyFile(tempRoot, "FEMapCreator.dll");
                    string assetsRoot = Path.Combine(tempRoot, "assets");
                    Directory.CreateDirectory(assetsRoot);

                    string observedCommand = "";
                    var observedArgs = new List<string>();
                    FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                        femapCreatorDll,
                        runner: (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                        {
                            observedCommand = command;
                            observedArgs = new List<string>(args);
                            return new ProcessRunResult
                            {
                                Started = true,
                                ExitCode = 0,
                                Stdout = JsonSerializer.Serialize(new
                                {
                                    assetsRoot = assetsRoot,
                                    tilesets = Array.Empty<object>(),
                                }),
                                Stderr = "",
                            };
                        });

                    Assert.True(result.Success, result.ErrorMessage);
                    Assert.Equal("dotnet", observedCommand);
                    Assert.Equal(new[] { femapCreatorDll, "tilesets", "list", "--json" }, observedArgs);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", hadOriginalHostPath ? previousHostPath : null);
                    DeleteDirectoryIfPresent(tempRoot);
                }
            }
        }

        [Fact]
        public void DiscoverTilesets_ReturnsHostUnavailableWhenManagedHostCannotStart()
        {
            string tempRoot = CreateTempDirectory();
            lock (EnvLock)
            {
                string? originalHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                bool hadOriginalHostPath = originalHostPath != null;
                string previousHostPath = originalHostPath ?? "";
                try
                {
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", null);
                    string femapCreatorDll = CreateEmptyFile(tempRoot, "FEMapCreator.dll");

                    FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                        femapCreatorDll,
                        runner: (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                            ProcessRunResult.NotStarted("dotnet missing"));

                    Assert.False(result.Success);
                    Assert.Equal(RandomMapGeneratorErrorCategory.HostUnavailable, result.ErrorCategory);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", hadOriginalHostPath ? previousHostPath : null);
                    DeleteDirectoryIfPresent(tempRoot);
                }
            }
        }

        [Fact]
        public void DiscoverTilesets_ParsesCompleteIncompleteAndEscapedEntries()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorExe = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);

                string goodImage = Path.Combine(assetsRoot, "good.png");
                string goodData = Path.Combine(assetsRoot, "good.json");
                File.WriteAllBytes(goodImage, CreateIndexedPng(width: 512, height: 16));
                File.WriteAllText(goodData, "{}");

                string elsewhereRoot = Path.Combine(tempRoot, "elsewhere");
                Directory.CreateDirectory(elsewhereRoot);
                string outsideImage = Path.Combine(elsewhereRoot, "outside.png");
                File.WriteAllBytes(outsideImage, CreateIndexedPng(width: 512, height: 16));
                string outsideData = Path.Combine(elsewhereRoot, "outside.json");
                File.WriteAllText(outsideData, "{}");

                string json = JsonSerializer.Serialize(new
                {
                    assetsRoot = assetsRoot,
                    tilesets = new object[]
                    {
                        new
                        {
                            name = "Complete",
                            imagePath = "good.png",
                            generationDataPath = "good.json",
                            hasImage = true,
                            hasGenerationData = true,
                            diagnostic = "",
                        },
                        new
                        {
                            name = "Incomplete",
                            imagePath = "good.png",
                            generationDataPath = "missing.json",
                            hasImage = true,
                            hasGenerationData = false,
                            diagnostic = "",
                        },
                        new
                        {
                            name = "EscapeRelative",
                            imagePath = "../outside.png",
                            generationDataPath = "good.json",
                            hasImage = true,
                            hasGenerationData = true,
                            diagnostic = "",
                        },
                        new
                        {
                            name = "EscapeAbsolute",
                            imagePath = outsideImage,
                            generationDataPath = outsideData,
                            hasImage = true,
                            hasGenerationData = true,
                            diagnostic = "",
                        },
                    },
                });

                FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                    femapCreatorExe,
                    runner: (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = json,
                        Stderr = "",
                    });

                Assert.True(result.Success, result.ErrorMessage);
                Assert.Equal(4, result.Tilesets.Count);
                Assert.Single(result.UsableTilesets);
                Assert.Equal("Complete", result.UsableTilesets[0].Name);
                Assert.Contains("no generation-data asset", result.Tilesets.Single(x => x.Name == "Incomplete").Diagnostic, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("escapes the sanctioned asset root", result.Tilesets.Single(x => x.Name == "EscapeRelative").Diagnostic, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("escapes the sanctioned asset root", result.Tilesets.Single(x => x.Name == "EscapeAbsolute").Diagnostic, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void DiscoverTilesets_FlagsPngCompatibility()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorExe = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);

                File.WriteAllBytes(Path.Combine(assetsRoot, "compatible.png"), CreatePngHeader(512, 16));
                File.WriteAllText(Path.Combine(assetsRoot, "compatible.json"), "{}");
                File.WriteAllBytes(Path.Combine(assetsRoot, "incompatible.png"), CreatePngHeader(256, 16));
                File.WriteAllText(Path.Combine(assetsRoot, "incompatible.json"), "{}");

                string json = JsonSerializer.Serialize(new
                {
                    assetsRoot = assetsRoot,
                    tilesets = new object[]
                    {
                        new
                        {
                            name = "Compatible",
                            imagePath = "compatible.png",
                            generationDataPath = "compatible.json",
                            hasImage = true,
                            hasGenerationData = true,
                            diagnostic = "",
                        },
                        new
                        {
                            name = "Incompatible",
                            imagePath = "incompatible.png",
                            generationDataPath = "incompatible.json",
                            hasImage = true,
                            hasGenerationData = true,
                            diagnostic = "",
                        },
                    },
                });

                FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                    femapCreatorExe,
                    runner: (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = json,
                        Stderr = "",
                    });

                Assert.True(result.Success, result.ErrorMessage);
                FEMapCreatorTilesetInfo compatible = result.Tilesets.Single(x => x.Name == "Compatible");
                FEMapCreatorTilesetInfo incompatible = result.Tilesets.Single(x => x.Name == "Incompatible");
                Assert.True(compatible.IsCompatible);
                Assert.True(compatible.IsUsable);
                Assert.Equal(512, compatible.ImageWidth);
                Assert.Equal(16, compatible.ImageHeight);
                Assert.False(incompatible.IsCompatible);
                Assert.Contains("width 256 is incompatible", incompatible.Diagnostic, StringComparison.OrdinalIgnoreCase);
                Assert.Single(result.UsableTilesets);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void DiscoverTilesets_ReturnsParseFailedForInvalidJson()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorExe = CreateEmptyFile(tempRoot, "FEMapCreator.exe");

                FEMapCreatorTilesetDiscoveryResult result = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                    femapCreatorExe,
                    runner: (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = "{ not json }",
                        Stderr = "",
                    });

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.ParseFailed, result.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void RealCliIntegration_UsesConfiguredInstallationWhenAvailable()
        {
            string femapCreatorPath = Environment.GetEnvironmentVariable("FEBUILDERGBA_FEMAPCREATOR_PATH") ?? "";
            if (string.IsNullOrWhiteSpace(femapCreatorPath)) return; // skip when no real FEMapCreator path is configured

            string assetsDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_FEMAPCREATOR_ASSETS_DIR") ?? "";
            FEMapCreatorTilesetDiscoveryResult discovery = FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets(
                femapCreatorPath,
                assetsDir);
            Assert.True(discovery.Success, discovery.ErrorMessage + "\n" + discovery.StdoutTail + "\n" + discovery.StderrTail);

            string requestedTileset = Environment.GetEnvironmentVariable("FEBUILDERGBA_FEMAPCREATOR_TILESET") ?? "";
            var tileset = string.IsNullOrWhiteSpace(requestedTileset)
                ? discovery.UsableTilesets.FirstOrDefault()
                : discovery.UsableTilesets.FirstOrDefault(x => string.Equals(x.Name, requestedTileset, StringComparison.Ordinal));
            if (tileset == null) return; // skip when no compatible tileset is available in the local installation

            RandomMapGenerationResult generation = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
            {
                Width = 4,
                Height = 4,
                TilesetName = tileset.Name,
                Algorithm = RandomMapGeneratorAlgorithms.Default,
                Seed = 42,
                FEMapCreatorPath = femapCreatorPath,
                AssetsDir = assetsDir,
            });

            Assert.True(generation.Success, generation.ErrorMessage + "\n" + generation.StdoutTail + "\n" + generation.StderrTail);
            Assert.Equal(16, generation.Mars.Length);
        }

        static byte[] CreateIndexedPng(int width, int height)
        {
            byte[] palette = new byte[32];
            byte[] indices = new byte[width * height];
            byte[] pngBytes = IndexedPngWriter.Write(indices, width, height, palette, paletteColorCount: 1);
            Assert.NotNull(pngBytes);
            return pngBytes;
        }

        static byte[] CreatePngHeader(int width, int height)
        {
            byte[] header =
            {
                137, 80, 78, 71, 13, 10, 26, 10,
                0, 0, 0, 13,
                (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0, 0, 0, 0,
                0, 0, 0, 0,
            };
            WriteUInt32BigEndian(header, 16, (uint)width);
            WriteUInt32BigEndian(header, 20, (uint)height);
            return header;
        }

        static void WriteUInt32BigEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "febuildergba-tileset-discovery-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateEmptyFile(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "");
            return path;
        }

        static void DeleteDirectoryIfPresent(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
