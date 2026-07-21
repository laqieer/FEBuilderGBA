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
    /// <see cref="Outcome"/> (on success) or <see cref="ErrorMessage"/> (on failure/cancellation)
    /// is meaningful; never both.
    /// </summary>
    internal sealed class RandomMapOneClickResult
    {
        public bool Success { get; init; }

        /// <summary>
        /// True when the result reflects cancellation observed after a backend attempt started
        /// (#1978 Slice 3 review finding #1) — distinct from a genuine backend failure so the UI
        /// can show a deterministic "Cancelled" status rather than "Failed" (finding #4). Never
        /// true together with <see cref="Success"/>.
        /// </summary>
        public bool Cancelled { get; init; }

        public string ErrorMessage { get; init; } = "";
        public RandomMapGenerationOutcome? Outcome { get; init; }
        public RandomMapBackendUsed BackendUsed { get; init; }

        /// <summary>
        /// Typed mapping state carried through every outcome. The Map Editor formats any
        /// stale/invalid fallback notice with localized <c>R._(...)</c> text at the Avalonia UI
        /// boundary rather than receiving an English sentence from Core.
        /// </summary>
        public FEMapCreatorMappingStatus MappingStatus { get; init; } = FEMapCreatorMappingStatus.NoMapping;

        /// <summary>Technical detail associated with <see cref="MappingStatus"/>.</summary>
        public string MappingReason { get; init; } = "";
    }

    /// <summary>
    /// Immutable snapshot of the three shared Config values needed by authoritative FEMapCreator
    /// mapping resolution. Captured before the worker hop so the background resolver never reads
    /// the live, UI-mutated <see cref="Config"/> dictionary.
    /// </summary>
    internal sealed class RandomMapMappingConfigSnapshot
    {
        internal RandomMapMappingConfigSnapshot(
            string rawExecutablePath,
            string rawAssetsRoot,
            string rawMappingsJson)
        {
            RawExecutablePath = rawExecutablePath ?? "";
            RawAssetsRoot = rawAssetsRoot ?? "";
            RawMappingsJson = rawMappingsJson ?? "";
        }

        internal string RawExecutablePath { get; }
        internal string RawAssetsRoot { get; }
        internal string RawMappingsJson { get; }
    }

    /// <summary>
    /// Delegate used to resolve the current, freshly-validated FEMapCreator profile and
    /// per-fingerprint mapping lookup (#1978 Slice 3). The default production implementation
    /// reads <see cref="CoreState.Config"/> and touches only the local filesystem (via
    /// <see cref="FEMapCreatorProfileCore.Validate"/> / <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>)
    /// — it never launches a process or touches the network. The service runs it off the UI
    /// thread and supplies cancellation through every authoritative file hash.
    /// </summary>
    internal delegate (FEMapCreatorSetupSnapshot Profile, FEMapCreatorMappingLookupResult MappingLookup)
        RandomMapMappingResolverDelegate(
            TilesetFingerprint fingerprint,
            RandomMapMappingConfigSnapshot configSnapshot,
            CancellationToken cancellationToken);

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
    /// Cancellation-aware external-generation seam (#1978 Slice 3 review finding #1). The default
    /// production implementation forwards <paramref name="cancellationToken"/> into
    /// <see cref="RandomMapGeneratorCore.Generate"/>'s cancellation-aware overload so a started
    /// external process is genuinely owned and terminated on cancel rather than merely abandoning
    /// an awaited <see cref="Task"/>.
    /// </summary>
    internal delegate RandomMapGenerationResult RandomMapExternalGenerateDelegate(
        RandomMapGenerationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Orchestrates one-click random-map generation (#1978 Slice 3): resolves the current
    /// tileset fingerprint, decides between the external FEMapCreator adapter and the built-in
    /// engine per <see cref="RandomMapBackendSelectorCore"/>, and runs the chosen backend off
    /// the UI thread. Never falls back from a started external attempt to the built-in engine —
    /// once the external adapter is launched, any failure is surfaced directly. A cancellation
    /// observed after a backend attempt starts is discarded (never applied) and reported as a
    /// distinct <see cref="RandomMapOneClickResult.Cancelled"/> outcome rather than a stale
    /// success or generic failure (review finding #1). Every dependency is injectable so tests
    /// never need a real FEMapCreator process, ROM, or config file.
    /// </summary>
    internal sealed class RandomMapOneClickService
    {
        readonly ProcessRunnerDelegate? _runner;
        readonly RandomMapExternalGenerateDelegate _generateExternal;
        readonly RandomMapBuiltInGenerateDelegate _generateBuiltIn;
        readonly RandomMapMappingResolverDelegate _resolveMapping;

        internal RandomMapOneClickService(
            ProcessRunnerDelegate? runner = null,
            RandomMapExternalGenerateDelegate? generateExternal = null,
            RandomMapBuiltInGenerateDelegate? generateBuiltIn = null,
            RandomMapMappingResolverDelegate? resolveMapping = null)
        {
            _runner = runner;
            _generateExternal = generateExternal ?? DefaultGenerateExternal;
            _generateBuiltIn = generateBuiltIn ?? BuiltInRandomMapGeneratorCore.TryGenerateFromRom;
            _resolveMapping = resolveMapping ?? DefaultResolveMapping;
        }

        /// <summary>
        /// When a caller supplies a custom synchronous <see cref="ProcessRunnerDelegate"/> (e.g.
        /// a CLI-style test double with no cancellation semantics), preserve that exact call
        /// shape; <see cref="RandomMapGeneratorCore.Generate"/> still performs best-effort
        /// before/after cancellation checks around the call. Otherwise route through the
        /// cancellation-aware <see cref="ProcessRunnerCore.Run(string, System.Collections.Generic.IEnumerable{string}, string, int, int, CancellationToken)"/>
        /// overload directly so a started production external process is genuinely killed on
        /// cancel.
        /// </summary>
        RandomMapGenerationResult DefaultGenerateExternal(RandomMapGenerationRequest request, CancellationToken cancellationToken)
        {
            return _runner != null
                ? RandomMapGeneratorCore.Generate(request, _runner, cancellationToken)
                : RandomMapGeneratorCore.Generate(request, cancellationToken: cancellationToken, cancellableRunner: ProcessRunnerCore.Run);
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

            RandomMapMappingConfigSnapshot mappingConfig = CaptureMappingConfig();
            FEMapCreatorSetupSnapshot profile;
            FEMapCreatorMappingLookupResult mappingLookup;
            try
            {
                (profile, mappingLookup) = await Task.Run(
                    () => _resolveMapping(snapshot.Fingerprint, mappingConfig, cancellationToken),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Cancelled();
            }

            RandomMapBackendSelection selection = RandomMapBackendSelectorCore.Select(mappingLookup);

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

                RandomMapGenerationResult? externalResult;
                try
                {
                    externalResult = await Task.Run(
                        () => _generateExternal(request, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // A started external process was cancelled before returning a result:
                    // discard it — never apply a late/partial result (review finding #1).
                    return Cancelled(selection);
                }

                // Mandatory re-check immediately after backend completion (review finding #1):
                // a token cancelled during the call, or the process runner itself reporting
                // Cancelled, must never be treated as a usable success.
                if (cancellationToken.IsCancellationRequested
                    || externalResult?.ErrorCategory == RandomMapGeneratorErrorCategory.Cancelled)
                {
                    return Cancelled(selection);
                }

                if (externalResult == null || !externalResult.Success)
                {
                    string detail = externalResult == null
                        ? R._("Random map generation returned no result.")
                        : (string.IsNullOrWhiteSpace(externalResult.ErrorMessage)
                            ? R._("Random map generation failed.")
                            : externalResult.ErrorMessage);
                    return Failure(detail, selection);
                }

                return new RandomMapOneClickResult
                {
                    Success = true,
                    BackendUsed = RandomMapBackendUsed.External,
                    MappingStatus = selection.MappingStatus,
                    MappingReason = selection.MappingReason,
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
            (bool corpusOk, BuiltInRandomMapGenerationResult? builtInResult, string builtInError) outcome;
            try
            {
                outcome = await Task.Run(
                    () =>
                    {
                        bool corpusResolved = _generateBuiltIn(
                            rom, mapSettingAddr, width, height, currentGrid, seed, cancellationToken,
                            out BuiltInRandomMapGenerationResult? r, out string e);
                        return (corpusResolved, r, e);
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Cancelled(selection);
            }

            // Mandatory re-check immediately after backend completion (review finding #1).
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(selection);

            if (!outcome.corpusOk)
            {
                // Review finding #2: carry the stale/invalid-mapping notice on every built-in
                // outcome, including corpus-resolution failure — never only on success.
                return Failure(
                    string.Format(R._("Could not build the built-in generator's source corpus: {0}"), outcome.builtInError),
                    selection);
            }

            if (outcome.builtInResult == null || !outcome.builtInResult.Success)
            {
                if (outcome.builtInResult?.ErrorCategory == BuiltInRandomMapErrorCategory.Cancelled)
                    return Cancelled(selection);

                // Review finding #2: carry the notice on generator failure too.
                return Failure(MapBuiltInError(outcome.builtInResult), selection);
            }

            return new RandomMapOneClickResult
            {
                Success = true,
                BackendUsed = RandomMapBackendUsed.BuiltIn,
                MappingStatus = selection.MappingStatus,
                MappingReason = selection.MappingReason,
                Outcome = new RandomMapGenerationOutcome
                {
                    Mars = outcome.builtInResult.Mars,
                    Width = width,
                    Height = height,
                    EffectiveSeed = outcome.builtInResult.EffectiveSeed,
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

        static RandomMapOneClickResult Failure(
            string message,
            RandomMapBackendSelection? selection = null) => new RandomMapOneClickResult
        {
            Success = false,
            ErrorMessage = message,
            MappingStatus = selection?.MappingStatus ?? FEMapCreatorMappingStatus.NoMapping,
            MappingReason = selection?.MappingReason ?? "",
        };

        static RandomMapOneClickResult Cancelled(
            RandomMapBackendSelection? selection = null) => new RandomMapOneClickResult
        {
            Success = false,
            Cancelled = true,
            ErrorMessage = R._("Random map generation was cancelled."),
            MappingStatus = selection?.MappingStatus ?? FEMapCreatorMappingStatus.NoMapping,
            MappingReason = selection?.MappingReason ?? "",
        };

        static (FEMapCreatorSetupSnapshot Profile, FEMapCreatorMappingLookupResult MappingLookup) DefaultResolveMapping(
            TilesetFingerprint fingerprint,
            RandomMapMappingConfigSnapshot configSnapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FEMapCreatorSetupSnapshot profile =
                FEMapCreatorProfileCore.Validate(
                    configSnapshot.RawExecutablePath,
                    configSnapshot.RawAssetsRoot,
                    cancellationToken);

            var detachedConfig = new Config();
            if (!string.IsNullOrWhiteSpace(configSnapshot.RawMappingsJson))
            {
                detachedConfig[FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] =
                    configSnapshot.RawMappingsJson;
            }
            var mappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(detachedConfig);
            cancellationToken.ThrowIfCancellationRequested();
            FEMapCreatorMappingLookupResult lookup =
                FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprint, profile, cancellationToken);
            return (profile, lookup);
        }

        static RandomMapMappingConfigSnapshot CaptureMappingConfig()
        {
            Config config = CoreState.Config;
            return new RandomMapMappingConfigSnapshot(
                config?.at(FEMapCreatorProfileCore.ExecutablePathConfigKey, "") ?? "",
                config?.at(FEMapCreatorProfileCore.AssetsRootConfigKey, "") ?? "",
                config?.at(FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey, "") ?? "");
        }
    }
}
