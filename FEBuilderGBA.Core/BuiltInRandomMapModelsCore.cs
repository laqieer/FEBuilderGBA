// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Failure categories surfaced by <see cref="BuiltInRandomMapGeneratorCore"/>. Unlike
    /// <see cref="RandomMapGeneratorErrorCategory"/> (the external FEMapCreator process
    /// adapter), this engine never shells out to another process, so its failures are
    /// limited to input validation, source-data sufficiency, cancellation, and bounded
    /// search exhaustion.
    /// </summary>
    public enum BuiltInRandomMapErrorCategory
    {
        /// <summary>No failure.</summary>
        None,
        /// <summary>Requested dimensions, seed, or corpus reference were invalid.</summary>
        InvalidInput,
        /// <summary>
        /// The current ROM tileset does not supply enough distinct renderable chipsets or
        /// directional/edge evidence to produce a non-degenerate layout.
        /// </summary>
        InsufficientSourceData,
        /// <summary>The caller's <see cref="System.Threading.CancellationToken"/> was signaled.</summary>
        Cancelled,
        /// <summary>
        /// All deterministic restarts exhausted their node budgets or failed the quality
        /// gates without finding an acceptable layout.
        /// </summary>
        SearchExhausted,
    }

    /// <summary>
    /// Which adjacency evidence a successful <see cref="BuiltInRandomMapGeneratorCore"/>
    /// result was produced under. See <c>docs/CORE-SEAMS.md</c> ("Built-in Random Map
    /// Generator") for the exact compatibility semantics of each model.
    /// </summary>
    public enum BuiltInRandomMapAdjacencyModel
    {
        /// <summary>
        /// Candidate pairs are compatible only when the ascending-map-index ROM scan
        /// directly observed that exact directional pair at least once.
        /// </summary>
        Strict,

        /// <summary>
        /// Candidate pairs are compatible when their touching 16x16 metatile boundary raw,
        /// palette-independent 4bpp pixel-index signatures match exactly (see
        /// <see cref="BuiltInRandomMapEdgeSignatureCore"/>). Used when the strict model
        /// has too little (or no) direct observation to drive a search.
        /// </summary>
        EdgeRelaxed,
    }

    /// <summary>
    /// Result of one <see cref="BuiltInRandomMapGeneratorCore.Generate"/> attempt. Immutable
    /// after construction; <see cref="Mars"/> is a defensive copy and is never the caller's
    /// or the corpus's backing array.
    /// </summary>
    public sealed class BuiltInRandomMapGenerationResult
    {
        internal BuiltInRandomMapGenerationResult(
            bool success,
            BuiltInRandomMapErrorCategory errorCategory,
            string errorMessage,
            ushort[] mars,
            int effectiveSeed,
            BuiltInRandomMapAdjacencyModel adjacencyModel,
            int restartsUsed,
            int distinctChipsetsUsed)
        {
            Success = success;
            ErrorCategory = errorCategory;
            ErrorMessage = errorMessage ?? "";
            Mars = mars ?? Array.Empty<ushort>();
            EffectiveSeed = effectiveSeed;
            AdjacencyModel = adjacencyModel;
            RestartsUsed = restartsUsed;
            DistinctChipsetsUsed = distinctChipsetsUsed;
        }

        /// <summary>True when a complete, quality-gated layout was produced.</summary>
        public bool Success { get; }

        /// <summary>Typed failure category, or <see cref="BuiltInRandomMapErrorCategory.None"/> on success.</summary>
        public BuiltInRandomMapErrorCategory ErrorCategory { get; }

        /// <summary>Human-readable failure detail, or a short success summary.</summary>
        public string ErrorMessage { get; }

        /// <summary>Row-major FEBuilder MAR values at the requested dimensions. Empty on failure.</summary>
        public ushort[] Mars { get; }

        /// <summary>
        /// The caller-supplied seed that produced <see cref="Mars"/> — always equal to the
        /// <c>seed</c> argument passed to <see cref="BuiltInRandomMapGeneratorCore.Generate"/>,
        /// never an internally-derived per-restart value. This makes the value directly
        /// replayable: calling <c>Generate</c> again with the same corpus, width, height,
        /// currentGrid, and <c>seed=EffectiveSeed</c> deterministically reproduces this same
        /// <see cref="Mars"/>/<see cref="AdjacencyModel"/>/<see cref="RestartsUsed"/> outcome.
        /// The internal per-restart derived seed used to drive the actual search RNG is
        /// private and never surfaced here. Meaningful only when <see cref="Success"/> is
        /// true; 0 on failure.
        /// </summary>
        public int EffectiveSeed { get; }

        /// <summary>Which adjacency evidence produced this result.</summary>
        public BuiltInRandomMapAdjacencyModel AdjacencyModel { get; }

        /// <summary>How many of the bounded restarts were attempted (1..<see cref="BuiltInRandomMapGeneratorCore.RestartCount"/>).</summary>
        public int RestartsUsed { get; }

        /// <summary>Count of distinct MAR values present in <see cref="Mars"/>.</summary>
        public int DistinctChipsetsUsed { get; }
    }
}
