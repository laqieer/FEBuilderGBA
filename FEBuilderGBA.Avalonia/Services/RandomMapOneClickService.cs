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
        /// Fingerprint resolved from the immutable ROM snapshot used for backend selection.
        /// The Map Editor reuses this exact identity for its apply-time live-ROM guard instead
        /// of performing a separate pre-service resolution on the dispatcher.
        /// </summary>
        public TilesetFingerprint SourceTilesetFingerprint { get; init; } = TilesetFingerprint.Empty;

        /// <summary>
        /// Typed mapping state carried through every outcome. The Map Editor formats any
        /// stale/invalid fallback notice with localized <c>R._(...)</c> text at the Avalonia UI
        /// boundary rather than receiving an English sentence from Core.
        /// </summary>
        public FEMapCreatorMappingStatus MappingStatus { get; init; } = FEMapCreatorMappingStatus.NoMapping;

        /// <summary>Locale-neutral reason associated with <see cref="MappingStatus"/>.</summary>
        public FEMapCreatorMappingReason MappingReason { get; init; } = FEMapCreatorMappingReason.None;

        /// <summary>Optional technical detail kept separate from localized user-facing text.</summary>
        public string MappingDetail { get; init; } = "";
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
    /// Cancellation-aware seam for resolving/decompressing the immutable generation tileset.
    /// The service always dispatches this work away from the Avalonia UI thread.
    /// </summary>
    internal delegate bool RandomMapTilesetResolverDelegate(
        ROM rom,
        uint mapSettingAddr,
        CancellationToken cancellationToken,
        out MapTilesetSnapshot snapshot,
        out string error);

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
    /// success or generic failure (review finding #1). Before the first worker hop it clones the
    /// ROM and current-grid inputs, so built-in corpus generation reads one immutable point-in-time
    /// snapshot while the live UI remains editable; apply-time identity checks still guard the
    /// eventual write. Every dependency is injectable so tests never need a real FEMapCreator
    /// process, ROM, or config file.
    /// </summary>
    internal sealed class RandomMapOneClickService
    {
        readonly ProcessRunnerDelegate? _runner;
        readonly RandomMapExternalGenerateDelegate _generateExternal;
        readonly RandomMapBuiltInGenerateDelegate _generateBuiltIn;
        readonly RandomMapMappingResolverDelegate _resolveMapping;
        readonly RandomMapTilesetResolverDelegate _resolveTileset;

        internal RandomMapOneClickService(
            ProcessRunnerDelegate? runner = null,
            RandomMapExternalGenerateDelegate? generateExternal = null,
            RandomMapBuiltInGenerateDelegate? generateBuiltIn = null,
            RandomMapMappingResolverDelegate? resolveMapping = null,
            RandomMapTilesetResolverDelegate? resolveTileset = null)
        {
            _runner = runner;
            _generateExternal = generateExternal ?? DefaultGenerateExternal;
            _generateBuiltIn = generateBuiltIn ?? BuiltInRandomMapGeneratorCore.TryGenerateFromRom;
            _resolveMapping = resolveMapping ?? DefaultResolveMapping;
            _resolveTileset = resolveTileset ?? DefaultResolveTileset;
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

            // Clone before the first await while the UI dispatcher still owns the call. The
            // worker must never scan the shared mutable ROM/current-grid objects.
            ROM generationRom = rom.Clone();
            ushort[]? generationGrid = currentGrid == null ? null : (ushort[])currentGrid.Clone();
            RandomMapMappingConfigSnapshot mappingConfig = CaptureMappingConfig();

            (bool Success, MapTilesetSnapshot Snapshot, string Error) tilesetResolution;
            try
            {
                tilesetResolution = await Task.Run(
                    () =>
                    {
                        bool success = _resolveTileset(
                            generationRom,
                            mapSettingAddr,
                            cancellationToken,
                            out MapTilesetSnapshot resolvedSnapshot,
                            out string resolveError);
                        return (success, resolvedSnapshot, resolveError);
                    },
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Cancelled();
            }

            if (!tilesetResolution.Success)
            {
                return Failure(string.Format(
                    R._("Could not resolve the current map's tileset: {0}"),
                    tilesetResolution.Error));
            }

            MapTilesetSnapshot snapshot = tilesetResolution.Snapshot;
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

                ushort[] externalMars = externalResult.MarsBuffer;
                if (!TryValidateGeneratedGrid(
                    externalMars,
                    width,
                    height,
                    // Trusted read-only hot path: the validator only reads config bytes, so
                    // use the snapshot's internal zero-copy buffer instead of the defensive
                    // public getter to avoid an extra per-generation array clone.
                    snapshot.ConfigDataBuffer,
                    out string externalValidationError))
                {
                    return Failure(externalValidationError, selection);
                }

                if (IsSourceIdentical(externalMars, generationGrid))
                    return SourceIdentityFailure(selection);

                return new RandomMapOneClickResult
                {
                    Success = true,
                    BackendUsed = RandomMapBackendUsed.External,
                    MappingStatus = selection.MappingStatus,
                    MappingReason = selection.MappingReason,
                    MappingDetail = selection.MappingDetail,
                    SourceTilesetFingerprint = snapshot.Fingerprint,
                    Outcome = new RandomMapGenerationOutcome
                    {
                        Mars = externalMars,
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
                            generationRom, mapSettingAddr, width, height, generationGrid, seed, cancellationToken,
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

            ushort[] builtInMars = outcome.builtInResult.MarsBuffer;
            if (!TryValidateGeneratedGrid(
                builtInMars,
                width,
                height,
                // Trusted read-only hot path: reuse the internal zero-copy config buffer
                // rather than cloning through the public defensive getter.
                snapshot.ConfigDataBuffer,
                out string builtInValidationError))
            {
                return Failure(builtInValidationError, selection);
            }

            if (IsSourceIdentical(builtInMars, generationGrid))
                return SourceIdentityFailure(selection);

            return new RandomMapOneClickResult
            {
                Success = true,
                BackendUsed = RandomMapBackendUsed.BuiltIn,
                MappingStatus = selection.MappingStatus,
                MappingReason = selection.MappingReason,
                MappingDetail = selection.MappingDetail,
                SourceTilesetFingerprint = snapshot.Fingerprint,
                Outcome = new RandomMapGenerationOutcome
                {
                    Mars = builtInMars,
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
                    string.Format(R._("This tileset does not have enough source map data for the built-in generator: {0}"), detail)
                    + " "
                    + R._("Draw or import a representative map for this tileset, or configure FEMapCreator in Options."),
                BuiltInRandomMapErrorCategory.Cancelled =>
                    R._("Random map generation was cancelled."),
                BuiltInRandomMapErrorCategory.SearchExhausted =>
                    string.Format(R._("The built-in generator could not find a valid layout within its search budget: {0}"), detail)
                    + " "
                    + R._("Try a different seed, or configure FEMapCreator in Options."),
                _ => detail,
            };
        }

        static bool TryValidateGeneratedGrid(
            ushort[]? mars,
            int width,
            int height,
            byte[] configData,
            out string error)
        {
            if (mars == null)
            {
                error = R._("Random map generation returned no map data.");
                return false;
            }

            long requiredCellCount = (long)width * height;
            if (width <= 0
                || height <= 0
                || requiredCellCount > int.MaxValue
                || mars.Length != requiredCellCount)
            {
                error = string.Format(
                    R._("Random map generation returned {0} cells, but {1} were required for a {2}x{3} map."),
                    mars.Length,
                    requiredCellCount,
                    width,
                    height);
                return false;
            }

            for (int i = 0; i < mars.Length; i++)
            {
                if (!BuiltInRandomMapTilesetCore.IsMarRenderable(mars[i], configData))
                {
                    error = string.Format(
                        R._("Random map generation returned an unrenderable tile at cell {0} (MAR 0x{1:X4}) for the current map tileset."),
                        i,
                        mars[i]);
                    return false;
                }
            }

            error = "";
            return true;
        }

        static bool IsSourceIdentical(ushort[]? candidate, ushort[]? source)
        {
            if (candidate == null || source == null || candidate.Length != source.Length)
                return false;

            for (int i = 0; i < candidate.Length; i++)
            {
                if (candidate[i] != source[i])
                    return false;
            }
            return true;
        }

        static RandomMapOneClickResult SourceIdentityFailure(RandomMapBackendSelection selection) =>
            Failure(
                R._("Random map generation returned the current map unchanged. Try a different seed."),
                selection);

        static RandomMapOneClickResult Failure(
            string message,
            RandomMapBackendSelection? selection = null) => new RandomMapOneClickResult
        {
            Success = false,
            ErrorMessage = message,
            MappingStatus = selection?.MappingStatus ?? FEMapCreatorMappingStatus.NoMapping,
            MappingReason = selection?.MappingReason ?? FEMapCreatorMappingReason.None,
            MappingDetail = selection?.MappingDetail ?? "",
        };

        static RandomMapOneClickResult Cancelled(
            RandomMapBackendSelection? selection = null) => new RandomMapOneClickResult
        {
            Success = false,
            Cancelled = true,
            ErrorMessage = R._("Random map generation was cancelled."),
            MappingStatus = selection?.MappingStatus ?? FEMapCreatorMappingStatus.NoMapping,
            MappingReason = selection?.MappingReason ?? FEMapCreatorMappingReason.None,
            MappingDetail = selection?.MappingDetail ?? "",
        };

        static bool DefaultResolveTileset(
            ROM rom,
            uint mapSettingAddr,
            CancellationToken cancellationToken,
            out MapTilesetSnapshot snapshot,
            out string error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                mapSettingAddr,
                out snapshot,
                out error);
            cancellationToken.ThrowIfCancellationRequested();
            return success;
        }

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
