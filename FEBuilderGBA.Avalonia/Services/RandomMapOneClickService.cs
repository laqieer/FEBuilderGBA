#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Which backend actually produced a <see cref="RandomMapOneClickResult"/> (#1978 Slice 3).</summary>
    internal enum RandomMapBackendUsed
    {
        BuiltIn,
        External,
    }

    /// <summary>
    /// Typed outcome of <see cref="RandomMapOneClickService.GenerateAsync"/>. Exactly one of
    /// <see cref="Outcome"/> (on success) or <see cref="ErrorMessage"/> (on failure) is
    /// meaningful; never both.
    /// </summary>
    internal sealed class RandomMapOneClickResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; } = "";
        public RandomMapGenerationOutcome? Outcome { get; init; }
        public RandomMapBackendUsed BackendUsed { get; init; }

        /// <summary>
        /// Non-empty only when the built-in engine ran because a saved external mapping exists
        /// but is no longer valid (<see cref="FEMapCreatorMappingStatus.Stale"/>/
        /// <see cref="FEMapCreatorMappingStatus.Invalid"/>) — must be shown to the user, never
        /// silently swallowed. Empty for a plain no-mapping-configured built-in run and for any
        /// external run.
        /// </summary>
        public string Notice { get; init; } = "";
    }

    /// <summary>
    /// Delegate used to resolve the current, freshly-validated FEMapCreator profile and
    /// per-fingerprint mapping lookup (#1978 Slice 3). The default production implementation
    /// reads <see cref="CoreState.Config"/> and touches only the local filesystem (via
    /// <see cref="FEMapCreatorProfileCore.Validate"/> / <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>)
    /// — it never launches a process or touches the network, so it is safe to call synchronously
    /// on every one-click generation before any backend is chosen.
    /// </summary>
    internal delegate (FEMapCreatorSetupSnapshot Profile, FEMapCreatorMappingLookupResult MappingLookup)
        RandomMapMappingResolverDelegate(TilesetFingerprint fingerprint);

    /// <summary>Delegate wrapping the built-in engine's one-call ROM resolution + generation entry point.</summary>
    internal delegate bool RandomMapBuiltInGenerateDelegate(
        ROM rom,
        uint mapSettingAddr,
        int width,
        int height,
        ushort[]? currentGrid,
        int seed,
        CancellationToken cancellationToken,
        out BuiltInRandomMapGenerationResult? result,
        out string error);

    /// <summary>
    /// Orchestrates one-click random-map generation (#1978 Slice 3): resolves the current
    /// tileset fingerprint, decides between the external FEMapCreator adapter and the built-in
    /// engine per <see cref="RandomMapBackendSelectorCore"/>, and runs the chosen backend off
    /// the UI thread. Never falls back from a started external attempt to the built-in engine —
    /// once the external adapter is launched, any failure is surfaced directly. Every dependency
    /// is injectable so tests never need a real FEMapCreator process, ROM, or config file.
    /// </summary>
    internal sealed class RandomMapOneClickService
    {
        readonly ProcessRunnerDelegate _runner;
        readonly Func<RandomMapGenerationRequest, ProcessRunnerDelegate, RandomMapGenerationResult> _generateExternal;
        readonly RandomMapBuiltInGenerateDelegate _generateBuiltIn;
        readonly RandomMapMappingResolverDelegate _resolveMapping;

        internal RandomMapOneClickService(
            ProcessRunnerDelegate? runner = null,
            Func<RandomMapGenerationRequest, ProcessRunnerDelegate, RandomMapGenerationResult>? generateExternal = null,
            RandomMapBuiltInGenerateDelegate? generateBuiltIn = null,
            RandomMapMappingResolverDelegate? resolveMapping = null)
        {
            _runner = runner ?? ProcessRunnerCore.Run;
            _generateExternal = generateExternal ?? RandomMapGeneratorCore.Generate;
            _generateBuiltIn = generateBuiltIn ?? BuiltInRandomMapGeneratorCore.TryGenerateFromRom;
            _resolveMapping = resolveMapping ?? DefaultResolveMapping;
        }

        internal async Task<RandomMapOneClickResult> GenerateAsync(
            ROM rom,
            uint mapSettingAddr,
            int width,
            int height,
            ushort[]? currentGrid,
            int seed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!BuiltInRandomMapTilesetCore.TryResolveMapTileset(rom, mapSettingAddr, out MapTilesetSnapshot snapshot, out string tilesetError))
            {
                return Failure(string.Format(R._("Could not resolve the current map's tileset: {0}"), tilesetError));
            }

            (FEMapCreatorSetupSnapshot profile, FEMapCreatorMappingLookupResult mappingLookup) = _resolveMapping(snapshot.Fingerprint);
            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(mappingLookup);

            cancellationToken.ThrowIfCancellationRequested();

            if (selection.Kind == RandomMapBackendKind.External)
            {
                var request = new RandomMapGenerationRequest
                {
                    Width = width,
                    Height = height,
                    TilesetName = selection.ExternalMapping.TilesetName,
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = seed,
                    FEMapCreatorPath = profile.ExecutablePath,
                    AssetsDir = profile.AssetsRoot,
                };

                RandomMapGenerationResult externalResult = await Task.Run(
                    () => _generateExternal(request, _runner), cancellationToken);

                if (externalResult == null || !externalResult.Success)
                {
                    string detail = externalResult == null
                        ? R._("Random map generation returned no result.")
                        : (string.IsNullOrWhiteSpace(externalResult.ErrorMessage)
                            ? R._("Random map generation failed.")
                            : externalResult.ErrorMessage);
                    return Failure(detail);
                }

                return new RandomMapOneClickResult
                {
                    Success = true,
                    BackendUsed = RandomMapBackendUsed.External,
                    Outcome = new RandomMapGenerationOutcome
                    {
                        Mars = externalResult.Mars,
                        Width = width,
                        Height = height,
                        EffectiveSeed = seed,
                    },
                };
            }

            // A tuple-returning local wraps the out-parameter API so it can be awaited via
            // Task.Run (C# cannot await a method with out parameters directly).
            (bool corpusOk, BuiltInRandomMapGenerationResult? builtInResult, string builtInError) = await Task.Run(
                () =>
                {
                    bool corpusResolved = _generateBuiltIn(
                        rom, mapSettingAddr, width, height, currentGrid, seed, cancellationToken,
                        out BuiltInRandomMapGenerationResult? r, out string e);
                    return (corpusResolved, r, e);
                },
                cancellationToken);

            if (!corpusOk)
                return Failure(string.Format(R._("Could not build the built-in generator's source corpus: {0}"), builtInError));

            if (builtInResult == null || !builtInResult.Success)
            {
                string message = MapBuiltInError(builtInResult);
                return Failure(message);
            }

            return new RandomMapOneClickResult
            {
                Success = true,
                BackendUsed = RandomMapBackendUsed.BuiltIn,
                Notice = selection.Notice,
                Outcome = new RandomMapGenerationOutcome
                {
                    Mars = builtInResult.Mars,
                    Width = width,
                    Height = height,
                    EffectiveSeed = builtInResult.EffectiveSeed,
                },
            };
        }

        static string MapBuiltInError(BuiltInRandomMapGenerationResult? result)
        {
            if (result == null)
                return R._("Built-in random map generation returned no result.");

            string detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorCategory.ToString()
                : result.ErrorMessage;

            return result.ErrorCategory switch
            {
                BuiltInRandomMapErrorCategory.InvalidInput =>
                    string.Format(R._("Random map request was invalid: {0}"), detail),
                BuiltInRandomMapErrorCategory.InsufficientSourceData =>
                    string.Format(R._("This tileset does not have enough source map data for the built-in generator: {0}"), detail),
                BuiltInRandomMapErrorCategory.Cancelled =>
                    R._("Random map generation was cancelled."),
                BuiltInRandomMapErrorCategory.SearchExhausted =>
                    string.Format(R._("The built-in generator could not find a valid layout within its search budget: {0}"), detail),
                _ => detail,
            };
        }

        static RandomMapOneClickResult Failure(string message) => new RandomMapOneClickResult
        {
            Success = false,
            ErrorMessage = message,
        };

        static (FEMapCreatorSetupSnapshot Profile, FEMapCreatorMappingLookupResult MappingLookup) DefaultResolveMapping(TilesetFingerprint fingerprint)
        {
            Config config = CoreState.Config;
            string rawExePath = config?.at(FEMapCreatorProfileCore.ExecutablePathConfigKey, "") ?? "";
            string rawAssetsRoot = config?.at(FEMapCreatorProfileCore.AssetsRootConfigKey, "") ?? "";
            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(rawExePath, rawAssetsRoot);

            var mappings = config != null
                ? FEMapCreatorTilesetMappingStoreCore.LoadAll(config)
                : Array.Empty<FEMapCreatorTilesetMappingEntry>();
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprint, profile);
            return (profile, lookup);
        }
    }
}
