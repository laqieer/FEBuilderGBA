// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// Discovers FEMapCreator tilesets through <c>tilesets list --json</c>, validates that
    /// asset paths stay under the sanctioned asset root, and flags tilesets whose image width
    /// is incompatible with FEBuilderGBA's fixed 32-column chipset palette.
    /// </summary>
    public static class FEMapCreatorTilesetDiscoveryCore
    {
        const int ProcessTimeoutMs = 120_000;
        const int MaximumOutputChars = 1_000_000;
        const int DiagnosticTailChars = 4096;

        sealed class TilesetListDto
        {
            public string AssetsRoot { get; set; } = "";
            public List<TilesetDto> Tilesets { get; set; } = new List<TilesetDto>();
            public List<TilesetDto> Entries { get; set; } = new List<TilesetDto>();
        }

        sealed class TilesetDto
        {
            public string Name { get; set; } = "";
            public string ImagePath { get; set; } = "";
            public string GenerationDataPath { get; set; } = "";
            public bool HasImage { get; set; }
            public bool HasGenerationData { get; set; }
            public string Diagnostic { get; set; } = "";
        }

        /// <summary>
        /// Discover FEMapCreator tilesets. Never throws; all faults surface through the typed
        /// result. When <paramref name="assetsDir"/> is supplied, it must be an existing
        /// absolute directory and is forwarded as <c>--assets-dir</c>.
        /// </summary>
        public static FEMapCreatorTilesetDiscoveryResult DiscoverTilesets(
            string feMapCreatorPath,
            string assetsDir = null,
            ProcessRunnerDelegate runner = null)
        {
            try
            {
                runner ??= ProcessRunnerCore.Run;

                if (!FEMapCreatorLauncherCore.TryNormalizeAssetsDirectory(
                    assetsDir,
                    out string normalizedAssetsDir,
                    out string assetsDirError))
                {
                    return Fail(RandomMapGeneratorErrorCategory.InvalidPath, assetsDirError);
                }

                FEMapCreatorLauncherCore.FEMapCreatorLaunchSpec spec =
                    FEMapCreatorLauncherCore.CreateLaunchSpec(
                        feMapCreatorPath,
                        BuildDiscoveryArguments(normalizedAssetsDir));
                if (!spec.Success)
                    return Fail(spec.ErrorCategory, spec.ErrorMessage);

                ProcessRunResult processResult = runner(
                    spec.Command,
                    spec.Arguments,
                    spec.WorkingDirectory,
                    ProcessTimeoutMs,
                    MaximumOutputChars);
                if (!processResult.Started)
                    return FailForNotStarted(spec, processResult);
                if (processResult.TimedOut)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.TimedOut,
                        AppendTerminationFailure("FEMapCreator tileset discovery timed out.", processResult),
                        processResult);
                if (processResult.OutputLimitExceeded)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.OutputLimitExceeded,
                        AppendTerminationFailure("FEMapCreator tileset discovery exceeded the output capture limit.", processResult),
                        processResult);
                if (processResult.ExitCode != 0)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.NonZeroExit,
                        "FEMapCreator tileset discovery exited with code " + processResult.ExitCode + ".",
                        processResult);

                TilesetListDto payload = ParseDiscoveryJson(processResult.Stdout, out string parseError);
                if (payload == null)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.ParseFailed, parseError, processResult);

                string effectiveAssetsRoot = ResolveEffectiveAssetsRoot(
                    payload.AssetsRoot,
                    normalizedAssetsDir,
                    out string assetsRootError);
                if (string.IsNullOrEmpty(effectiveAssetsRoot))
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.ParseFailed, assetsRootError, processResult);

                var result = new FEMapCreatorTilesetDiscoveryResult
                {
                    Success = true,
                    ErrorCategory = RandomMapGeneratorErrorCategory.None,
                    AssetsRoot = payload.AssetsRoot ?? "",
                    EffectiveAssetsRoot = effectiveAssetsRoot,
                    ErrorMessage = "Discovered FEMapCreator tilesets.",
                    ExitCode = processResult.ExitCode,
                    StdoutTail = Tail(processResult.Stdout),
                    StderrTail = Tail(processResult.Stderr),
                };

                List<TilesetDto> sourceEntries = payload.Tilesets;
                if ((sourceEntries == null || sourceEntries.Count == 0) && payload.Entries != null)
                    sourceEntries = payload.Entries;
                if (sourceEntries == null)
                    sourceEntries = new List<TilesetDto>();

                foreach (TilesetDto dto in sourceEntries)
                {
                    FEMapCreatorTilesetInfo info = BuildTilesetInfo(dto, effectiveAssetsRoot);
                    result.Tilesets.Add(info);
                    if (info.IsUsable)
                        result.UsableTilesets.Add(info);
                }

                return result;
            }
            catch (Exception ex)
            {
                return Fail(RandomMapGeneratorErrorCategory.ParseFailed,
                    "Unable to process FEMapCreator tileset discovery output: " + ex.Message);
            }
        }

        static IEnumerable<string> BuildDiscoveryArguments(string normalizedAssetsDir)
        {
            var args = new List<string>
            {
                "tilesets",
                "list",
                "--json",
            };
            if (!string.IsNullOrEmpty(normalizedAssetsDir))
            {
                args.Add("--assets-dir");
                args.Add(normalizedAssetsDir);
            }
            return args;
        }

        static TilesetListDto ParseDiscoveryJson(string stdout, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(stdout))
            {
                error = "FEMapCreator tileset discovery produced no JSON output.";
                return null;
            }

            try
            {
                TilesetListDto payload = JsonSerializer.Deserialize<TilesetListDto>(
                    stdout,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });
                if (payload == null)
                {
                    error = "FEMapCreator tileset discovery returned null JSON.";
                    return null;
                }
                return payload;
            }
            catch (JsonException ex)
            {
                error = "FEMapCreator tileset discovery returned invalid JSON: " + ex.Message;
                return null;
            }
        }

        static string ResolveEffectiveAssetsRoot(
            string reportedAssetsRoot,
            string normalizedAssetsDir,
            out string error)
        {
            error = "";
            if (!string.IsNullOrEmpty(normalizedAssetsDir))
                return normalizedAssetsDir;

            if (string.IsNullOrWhiteSpace(reportedAssetsRoot))
            {
                error = "FEMapCreator tileset discovery JSON did not include assetsRoot.";
                return "";
            }

            string rawRoot = reportedAssetsRoot.Trim();
            if (Uri.TryCreate(rawRoot, UriKind.Absolute, out Uri uri))
            {
                if (!uri.IsFile)
                {
                    error = "FEMapCreator assetsRoot must be a local absolute path: " + reportedAssetsRoot;
                    return "";
                }
                rawRoot = uri.LocalPath;
            }

            if (!Path.IsPathRooted(rawRoot))
            {
                error = "FEMapCreator assetsRoot must be an absolute path: " + reportedAssetsRoot;
                return "";
            }

            try
            {
                return Path.GetFullPath(rawRoot);
            }
            catch (Exception ex)
            {
                error = "FEMapCreator assetsRoot could not be normalized: " + ex.Message;
                return "";
            }
        }

        static FEMapCreatorTilesetInfo BuildTilesetInfo(TilesetDto dto, string effectiveAssetsRoot)
        {
            var info = new FEMapCreatorTilesetInfo();
            if (dto == null)
            {
                info.Diagnostic = "Tileset entry was null.";
                return info;
            }

            info.Name = dto.Name ?? "";
            info.ImagePath = dto.ImagePath ?? "";
            info.GenerationDataPath = dto.GenerationDataPath ?? "";
            info.HasImage = dto.HasImage;
            info.HasGenerationData = dto.HasGenerationData;
            info.Diagnostic = dto.Diagnostic ?? "";

            if (string.IsNullOrWhiteSpace(info.Name))
                info.Diagnostic = AppendDiagnostic(info.Diagnostic, "Tileset name is missing.");
            if (!info.HasImage)
                info.Diagnostic = AppendDiagnostic(info.Diagnostic, "FEMapCreator reported no image asset.");
            if (!info.HasGenerationData)
                info.Diagnostic = AppendDiagnostic(info.Diagnostic, "FEMapCreator reported no generation-data asset.");

            if (!TryConfinePath(info.ImagePath, effectiveAssetsRoot, out string resolvedImagePath, out string imagePathError))
            {
                if (!string.IsNullOrWhiteSpace(info.ImagePath))
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic, imagePathError);
            }
            else
            {
                info.ResolvedImagePath = resolvedImagePath;
                if (!File.Exists(resolvedImagePath))
                {
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic,
                        "Image asset does not exist: " + resolvedImagePath);
                }
            }

            if (!TryConfinePath(info.GenerationDataPath, effectiveAssetsRoot, out string resolvedGenerationDataPath, out string generationPathError))
            {
                if (!string.IsNullOrWhiteSpace(info.GenerationDataPath))
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic, generationPathError);
            }
            else
            {
                info.ResolvedGenerationDataPath = resolvedGenerationDataPath;
                if (!File.Exists(resolvedGenerationDataPath))
                {
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic,
                        "Generation-data asset does not exist: " + resolvedGenerationDataPath);
                }
            }

            if (string.IsNullOrWhiteSpace(info.Diagnostic) && !string.IsNullOrEmpty(info.ResolvedImagePath))
            {
                IndexedPngInfo pngInfo = IndexedPngReader.Read(File.ReadAllBytes(info.ResolvedImagePath));
                if (!pngInfo.Ok)
                {
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic,
                        "Tileset image is not a valid PNG: " + pngInfo.Error);
                }
                else if (pngInfo.Width != MapEditorTilesetCore.PALETTE_COLUMNS * MapEditorTilesetCore.CHIPSET_PIXEL_SIZE)
                {
                    info.Diagnostic = AppendDiagnostic(info.Diagnostic,
                        $"Tileset image width {pngInfo.Width} is incompatible; expected {MapEditorTilesetCore.PALETTE_COLUMNS * MapEditorTilesetCore.CHIPSET_PIXEL_SIZE}.");
                }
                else
                {
                    info.IsCompatible = true;
                }
            }

            return info;
        }

        static bool TryConfinePath(
            string rawPath,
            string effectiveAssetsRoot,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = "";
            error = "";

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                error = "Asset path is missing.";
                return false;
            }

            try
            {
                resolvedPath = Path.IsPathRooted(rawPath)
                    ? Path.GetFullPath(rawPath)
                    : Path.GetFullPath(Path.Combine(effectiveAssetsRoot, rawPath));
            }
            catch (Exception ex)
            {
                error = "Asset path could not be normalized: " + ex.Message;
                return false;
            }

            if (!FEMapCreatorLauncherCore.IsUnderRoot(resolvedPath, effectiveAssetsRoot))
            {
                error = "Asset path escapes the sanctioned asset root: " + resolvedPath;
                return false;
            }

            return true;
        }

        static FEMapCreatorTilesetDiscoveryResult Fail(
            RandomMapGeneratorErrorCategory category,
            string message)
        {
            return new FEMapCreatorTilesetDiscoveryResult
            {
                Success = false,
                ErrorCategory = category,
                ErrorMessage = message ?? "",
            };
        }

        static FEMapCreatorTilesetDiscoveryResult FailFromProcessResult(
            RandomMapGeneratorErrorCategory category,
            string message,
            ProcessRunResult processResult)
        {
            return new FEMapCreatorTilesetDiscoveryResult
            {
                Success = false,
                ErrorCategory = category,
                ErrorMessage = message ?? "",
                ExitCode = processResult.ExitCode,
                StdoutTail = Tail(processResult.Stdout),
                StderrTail = Tail(processResult.Stderr),
            };
        }

        static FEMapCreatorTilesetDiscoveryResult FailForNotStarted(
            FEMapCreatorLauncherCore.FEMapCreatorLaunchSpec spec,
            ProcessRunResult processResult)
        {
            RandomMapGeneratorErrorCategory category = spec.UsesManagedHost
                ? RandomMapGeneratorErrorCategory.HostUnavailable
                : RandomMapGeneratorErrorCategory.ProcessStartFailed;
            string message = spec.UsesManagedHost
                ? "Unable to start the .NET host for FEMapCreator: " + processResult.ErrorMessage
                : "Unable to start FEMapCreator: " + processResult.ErrorMessage;
            return FailFromProcessResult(category, message, processResult);
        }

        static string AppendDiagnostic(string existing, string addition)
        {
            if (string.IsNullOrWhiteSpace(addition))
                return existing ?? "";
            if (string.IsNullOrWhiteSpace(existing))
                return addition;
            return existing + " " + addition;
        }

        static string AppendTerminationFailure(string baseMessage, ProcessRunResult processResult)
        {
            if (!processResult.TerminationFailed)
                return baseMessage;
            return baseMessage + " Process termination cleanup failed.";
        }

        static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            if (text.Length <= DiagnosticTailChars)
                return text;
            return text.Substring(text.Length - DiagnosticTailChars, DiagnosticTailChars);
        }
    }
}
