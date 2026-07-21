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
        /// Non-empty only when the built-in engine ran because a saved external mapping exists
        /// but is no longer valid (<see cref="FEMapCreatorMappingStatus.Stale"/>/
        /// <see cref="FEMapCreatorMappingStatus.Invalid"/>) — must be shown to the user, never
        /// silently swallowed. Carried on every built-in outcome (success, failure, AND
        /// cancellation — #1978 Slice 3 review finding #2), never on an external outcome or a
        /// plain no-mapping-configured built-in run.
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
                    return Cancelled(selection.Notice);
                }

                // Mandatory re-check immediately after backend completion (review finding #1):
                // a token cancelled during the call, or the process runner itself reporting
                // Cancelled, must never be treated as a usable success.
                if (cancellationToken.IsCancellationRequested
                    || externalResult?.ErrorCategory == RandomMapGeneratorErrorCategory.Cancelled)
                {
                    return Cancelled(selection.Notice);
                }

                if (externalResult == null || !externalResult.Success)
                {
                    string detail = externalResult == null
                        ? R._("Random map generation returned no result.")
                        : (string.IsNullOrWhiteSpace(externalResult.ErrorMessage)
                            ? R._("Random map generation failed.")
                            : externalResult.ErrorMessage);
                    return Failure(detail, selection.Notice);
                }

                return new RandomMapOneClickResult
                {
                    Success = true,
                    BackendUsed = RandomMapBackendUsed.External,
                    Notice = selection.Notice,
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
                return Cancelled(selection.Notice);
            }

            // Mandatory re-check immediately after backend completion (review finding #1).
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(selection.Notice);

            if (!outcome.corpusOk)
            {
                // Review finding #2: carry the stale/invalid-mapping notice on every built-in
                // outcome, including corpus-resolution failure — never only on success.
                return Failure(
                    string.Format(R._("Could not build the built-in generator's source corpus: {0}"), outcome.builtInError),
                    selection.Notice);
            }

            if (outcome.builtInResult == null || !outcome.builtInResult.Success)
            {
                if (outcome.builtInResult?.ErrorCategory == BuiltInRandomMapErrorCategory.Cancelled)
                    return Cancelled(selection.Notice);

                // Review finding #2: carry the notice on generator failure too.
                return Failure(MapBuiltInError(outcome.builtInResult), selection.Notice);
            }

            return new RandomMapOneClickResult
            {
                Success = true,
                BackendUsed = RandomMapBackendUsed.BuiltIn,
                Notice = selection.Notice,
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

        static RandomMapOneClickResult Failure(string message, string notice = "") => new RandomMapOneClickResult
        {
            Success = false,
            ErrorMessage = message,
            Notice = notice,
        };

        static RandomMapOneClickResult Cancelled(string notice = "") => new RandomMapOneClickResult
        {
            Success = false,
            Cancelled = true,
            ErrorMessage = R._("Random map generation was cancelled."),
            Notice = notice,
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
