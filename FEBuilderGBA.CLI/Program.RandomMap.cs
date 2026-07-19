// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    internal sealed class RandomMapGeneratorCliOperations
    {
        internal Func<int> GenerateSeed { get; init; } = () => Random.Shared.Next();
        internal Func<RandomMapGenerationRequest, ProcessRunnerDelegate, RandomMapGenerationResult> Generate
        { get; init; } = (request, runner) => RandomMapGeneratorCore.Generate(request, runner);
        internal Action<IReadOnlyList<AtomicFileSetWriterCore.FileOutput>> WriteOutputs
        { get; init; } = outputs => AtomicFileSetWriterCore.WriteAll(outputs);
    }

    static partial class Program
    {
        internal const int RandomMapGeneratorCsvMinimumDimension =
            RandomMapGeneratorCore.MinimumDimension;
        internal const int RandomMapGeneratorCsvMaximumDimension =
            RandomMapGeneratorCore.MaximumDimension;
        internal const string RandomMapGeneratorDefaultAlgorithm =
            RandomMapGeneratorAlgorithms.Default;

        static int RunGenerateRandomMap(Dictionary<string, string> argsDic)
        {
            var operations = new RandomMapGeneratorCliOperations();
            return RunGenerateRandomMap(argsDic, operations, runner: null, Console.Out, Console.Error);
        }

        internal static int RunGenerateRandomMap(
            Dictionary<string, string> argsDic,
            RandomMapGeneratorCliOperations operations,
            ProcessRunnerDelegate runner,
            TextWriter stdout,
            TextWriter stderr)
        {
            if (argsDic == null
                || operations?.Generate == null
                || operations.GenerateSeed == null
                || operations.WriteOutputs == null
                || stdout == null
                || stderr == null)
            {
                return 1;
            }

            bool json = argsDic.ContainsKey("--json");
            int Fail(string msg)
            {
                if (json)
                {
                    stdout.WriteLine(JsonSerializer.Serialize(
                        new Dictionary<string, object>
                        {
                            ["command"] = "generate-random-map",
                            ["ok"] = false,
                            ["error"] = msg,
                        }));
                }
                else
                {
                    stderr.WriteLine("Error: " + msg);
                }
                return 1;
            }

            if (!TryGetRequiredNonEmptyArgument(argsDic, "--femapcreator", out string femapCreatorPath))
                return Fail("--generate-random-map requires --femapcreator=<path>");
            if (!TryGetRequiredNonEmptyArgument(argsDic, "--tileset", out string tilesetName))
                return Fail("--generate-random-map requires --tileset=<name>");
            if (!TryGetRequiredNonEmptyArgument(argsDic, "--out", out string outPath))
                return Fail("--generate-random-map requires --out=<path>");

            if (!TryParseRandomMapDimension(argsDic, "--width", out int width, out string widthError))
                return Fail(widthError);
            if (!TryParseRandomMapDimension(argsDic, "--height", out int height, out string heightError))
                return Fail(heightError);

            string assetsDir = "";
            if (argsDic.TryGetValue("--assets-dir", out string assetsDirValue))
            {
                if (string.IsNullOrWhiteSpace(assetsDirValue))
                    return Fail("--generate-random-map requires --assets-dir=<path> when --assets-dir is supplied");
                assetsDir = assetsDirValue.Trim();
            }

            string algorithm = RandomMapGeneratorDefaultAlgorithm;
            if (argsDic.TryGetValue("--algorithm", out string algorithmValue))
            {
                if (!RandomMapGeneratorAlgorithms.TryNormalize(
                    algorithmValue, out algorithm))
                {
                    return Fail("--generate-random-map requires --algorithm to be one of: "
                        + string.Join(", ", RandomMapGeneratorAlgorithms.All));
                }
            }

            int seed;
            if (argsDic.TryGetValue("--seed", out string seedValue))
            {
                if (!int.TryParse(seedValue, out seed))
                    return Fail("--generate-random-map requires --seed=<int>");
            }
            else
            {
                seed = operations.GenerateSeed();
            }

            var request = new RandomMapGenerationRequest
            {
                Width = width,
                Height = height,
                TilesetName = tilesetName.Trim(),
                Algorithm = algorithm,
                Seed = seed,
                FEMapCreatorPath = femapCreatorPath.Trim(),
                AssetsDir = assetsDir,
            };

            RandomMapGenerationResult generation = operations.Generate(request, runner);
            if (generation == null)
                return Fail("Random map generation returned no result.");
            if (!generation.Success)
                return Fail(string.IsNullOrWhiteSpace(generation.ErrorMessage)
                    ? "Random map generation failed."
                    : generation.ErrorMessage);

            if (!TryBuildMapExportData(width, height, generation.Mars, out byte[] mapData, out string mapDataError))
                return Fail(mapDataError);

            string csv = MapExportCsv.Serialize(mapData);
            if (string.IsNullOrEmpty(csv))
                return Fail("Generated map CSV export was empty.");

            try
            {
                operations.WriteOutputs(new[]
                {
                    new AtomicFileSetWriterCore.FileOutput(outPath, Encoding.UTF8.GetBytes(csv)),
                });
            }
            catch (IOException ex)
            {
                return Fail("Failed to write output: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail("Failed to write output: " + ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Fail("Failed to write output: " + ex.Message);
            }
            catch (NotSupportedException ex)
            {
                return Fail("Failed to write output: " + ex.Message);
            }

            if (json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(
                    new Dictionary<string, object>
                    {
                        ["command"] = "generate-random-map",
                        ["ok"] = true,
                        ["femapcreator"] = request.FEMapCreatorPath,
                        ["tileset"] = request.TilesetName,
                        ["algorithm"] = request.Algorithm,
                        ["width"] = request.Width,
                        ["height"] = request.Height,
                        ["seed"] = request.Seed,
                        ["assetsDir"] = string.IsNullOrEmpty(request.AssetsDir) ? null : request.AssetsDir,
                        ["out"] = outPath,
                    }));
            }
            else
            {
                stdout.WriteLine(
                    $"Generated random map: {request.Width}x{request.Height}, seed={request.Seed}, tileset={request.TilesetName}, algorithm={request.Algorithm} -> {outPath}");
            }

            return 0;
        }

        static bool TryGetRequiredNonEmptyArgument(
            Dictionary<string, string> argsDic,
            string key,
            out string value)
        {
            value = "";
            if (!argsDic.TryGetValue(key, out string rawValue) || string.IsNullOrWhiteSpace(rawValue))
                return false;
            value = rawValue.Trim();
            return true;
        }

        static bool TryParseRandomMapDimension(
            Dictionary<string, string> argsDic,
            string key,
            out int value,
            out string error)
        {
            value = 0;
            error = "";

            if (!argsDic.TryGetValue(key, out string rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                error = $"--generate-random-map requires {key}=<int>";
                return false;
            }
            if (!int.TryParse(rawValue, out value))
            {
                error = $"--generate-random-map requires {key}=<int>";
                return false;
            }
            if (value < RandomMapGeneratorCsvMinimumDimension
                || value > RandomMapGeneratorCsvMaximumDimension)
            {
                error = $"--generate-random-map requires {key}=<int> in the range {RandomMapGeneratorCsvMinimumDimension}..{RandomMapGeneratorCsvMaximumDimension}";
                return false;
            }
            return true;
        }

        static bool TryBuildMapExportData(
            int width,
            int height,
            ushort[] mars,
            out byte[] mapData,
            out string error)
        {
            mapData = Array.Empty<byte>();
            error = "";

            if (width < 1 || width > byte.MaxValue || height < 1 || height > byte.MaxValue)
            {
                error = $"Generated map dimensions {width}x{height} are not supported by FEBuilderGBA CSV export.";
                return false;
            }

            long expectedCellCount = (long)width * height;
            if (expectedCellCount > int.MaxValue)
            {
                error = $"Generated map dimensions are too large: {width}x{height}.";
                return false;
            }
            if (mars == null)
            {
                error = "Generated map data is missing.";
                return false;
            }
            if (mars.Length != (int)expectedCellCount)
            {
                error = $"Generated map cell count mismatch: expected {(int)expectedCellCount} but got {mars.Length}.";
                return false;
            }

            mapData = new byte[2 + mars.Length * 2];
            mapData[0] = (byte)width;
            mapData[1] = (byte)height;
            for (int i = 0; i < mars.Length; i++)
            {
                ushort mar = mars[i];
                mapData[2 + i * 2] = (byte)(mar & 0xFF);
                mapData[2 + i * 2 + 1] = (byte)(mar >> 8);
            }

            return true;
        }
    }
}
