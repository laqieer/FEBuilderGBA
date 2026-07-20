// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Independently designed, clean-room, deterministic built-in random-map generator. Given
    /// a <see cref="BuiltInRandomMapTilesetCorpus"/> gathered from the current ROM's own maps,
    /// this performs a bounded backtracking search over a row-major grid of MAR values,
    /// honoring either the strictly-observed directional adjacency model or (when strict
    /// evidence is too sparse) the structural edge-signature relaxation. No uniform-fill or
    /// identity fallback exists: every non-<see cref="BuiltInRandomMapErrorCategory.None"/>
    /// result reports a specific, honest failure reason. See <c>docs/CORE-SEAMS.md</c>
    /// ("Built-in Random Map Generator") for the full behavioral contract and
    /// <c>docs/ENGINEERING-NOTES.md</c> for the clean-room design provenance.
    /// </summary>
    public static class BuiltInRandomMapGeneratorCore
    {
        /// <summary>Deterministic restarts attempted per adjacency model before giving up on that model.</summary>
        public const int RestartCount = 4;
        /// <summary>Floor of the per-restart search-node budget.</summary>
        public const int MinimumNodeBudget = 10_000;
        /// <summary>Ceiling of the per-restart search-node budget.</summary>
        public const int MaximumNodeBudget = 1_000_000;
        /// <summary>Fewest distinct MAR values a corpus must supply before a layout is even attempted.</summary>
        public const int MinimumDistinctCandidates = 2;
        /// <summary>Diversity/occupancy quality gates only apply once the corpus offers at least this many candidates.</summary>
        public const int DiversityGateCandidateThreshold = 3;
        /// <summary>Maximum fraction of cells a single MAR value may occupy once the diversity gate is active.</summary>
        public const double MaxSingleChipOccupancy = 0.90;

        /// <summary>
        /// Node budget for one restart attempt over a <paramref name="width"/> x
        /// <paramref name="height"/> grid: <c>min(1_000_000, max(10_000, width*height*64))</c>.
        /// </summary>
        public static int ComputeNodeBudget(int width, int height) =>
            (int)Math.Min(MaximumNodeBudget, Math.Max(MinimumNodeBudget, (long)width * height * 64));

        /// <summary>
        /// Resolve the current map's tileset from <paramref name="rom"/>, build its corpus, and
        /// generate a layout in one call. Returns false only when the corpus itself could not
        /// be built (see <paramref name="error"/>); a built corpus always yields a typed
        /// <paramref name="result"/> (which may itself be a failure — inspect
        /// <see cref="BuiltInRandomMapGenerationResult.Success"/>/<c>ErrorCategory</c>).
        /// </summary>
        public static bool TryGenerateFromRom(
            ROM rom,
            uint mapSettingAddr,
            int width,
            int height,
            ushort[] currentGrid,
            int seed,
            CancellationToken cancellationToken,
            out BuiltInRandomMapGenerationResult result,
            out string error)
        {
            result = null;
            if (!BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, mapSettingAddr, out BuiltInRandomMapTilesetCorpus corpus, out error))
                return false;

            result = Generate(corpus, width, height, currentGrid, seed, cancellationToken);
            error = result.ErrorMessage;
            return true;
        }

        /// <summary>
        /// Generate a <paramref name="width"/> x <paramref name="height"/> layout from
        /// <paramref name="corpus"/>. Never mutates <paramref name="corpus"/> or
        /// <paramref name="currentGrid"/>; <see cref="BuiltInRandomMapGenerationResult.Mars"/>
        /// is always a freshly allocated array.
        /// </summary>
        public static BuiltInRandomMapGenerationResult Generate(
            BuiltInRandomMapTilesetCorpus corpus,
            int width,
            int height,
            ushort[] currentGrid,
            int seed,
            CancellationToken cancellationToken)
        {
            if (corpus == null)
                return Failure(BuiltInRandomMapErrorCategory.InvalidInput, "Corpus is null.");
            if (width < MapEditorTilesetCore.MAP_MIN_WIDTH || height < MapEditorTilesetCore.MAP_MIN_HEIGHT || height > MapEditorTilesetCore.MAP_MAX_HEIGHT)
                return Failure(BuiltInRandomMapErrorCategory.InvalidInput, $"Dimensions {width}x{height} are out of the FE main-map range.");
            uint limitWidth = MapEditorTilesetCore.GetLimitMapWidth(height);
            if (limitWidth == 0 || width > limitWidth)
                return Failure(BuiltInRandomMapErrorCategory.InvalidInput, $"Width {width} exceeds the maximum {limitWidth} for height {height}.");
            if (currentGrid != null && currentGrid.Length != width * height)
                return Failure(BuiltInRandomMapErrorCategory.InvalidInput, "currentGrid length does not match width*height.");
            if (corpus.Candidates.Count < MinimumDistinctCandidates)
                return Failure(BuiltInRandomMapErrorCategory.InsufficientSourceData, $"Corpus offers only {corpus.Candidates.Count} distinct chipset(s); at least {MinimumDistinctCandidates} are required.");

            bool relaxedAvailable = TryBuildEdgeSignatures(corpus, out Dictionary<ushort, MetatileEdgeSignature> signatures);
            if (!corpus.HasStrictAdjacencyEvidence && !relaxedAvailable)
                return Failure(BuiltInRandomMapErrorCategory.InsufficientSourceData, "Corpus has neither observed adjacency pairs nor decodable edge signatures.");

            try
            {
                var attempts = new List<(BuiltInRandomMapAdjacencyModel model, int restart)>();
                if (corpus.HasStrictAdjacencyEvidence)
                    for (int r = 0; r < RestartCount; r++) attempts.Add((BuiltInRandomMapAdjacencyModel.Strict, r));
                if (relaxedAvailable)
                    for (int r = 0; r < RestartCount; r++) attempts.Add((BuiltInRandomMapAdjacencyModel.EdgeRelaxed, r));

                for (int i = 0; i < attempts.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (BuiltInRandomMapAdjacencyModel model, int restart) = attempts[i];
                    int derivedSeed = DeriveSeed(seed, restart, model);
                    bool isFinalAttempt = i == attempts.Count - 1;

                    AttemptResult attempt = RunAttempt(
                        corpus, model, signatures, width, height, currentGrid,
                        derivedSeed, requireDifferentFromSource: !isFinalAttempt, cancellationToken);

                    if (attempt.Outcome == AttemptOutcome.Success)
                    {
                        int distinct = CountDistinct(attempt.Mars);
                        return new BuiltInRandomMapGenerationResult(
                            true, BuiltInRandomMapErrorCategory.None, "Generated successfully.",
                            attempt.Mars, derivedSeed, model, restart + 1, distinct);
                    }
                }

                return Failure(BuiltInRandomMapErrorCategory.SearchExhausted,
                    "All deterministic restarts exhausted their node budgets without a quality-gated layout.");
            }
            catch (OperationCanceledException)
            {
                return Failure(BuiltInRandomMapErrorCategory.Cancelled, "Generation was cancelled.");
            }
        }

        static BuiltInRandomMapGenerationResult Failure(BuiltInRandomMapErrorCategory category, string message) =>
            new BuiltInRandomMapGenerationResult(false, category, message, Array.Empty<ushort>(), 0, BuiltInRandomMapAdjacencyModel.Strict, 0, 0);

        static int DeriveSeed(int baseSeed, int restartIndex, BuiltInRandomMapAdjacencyModel model)
        {
            int modelSalt = model == BuiltInRandomMapAdjacencyModel.Strict ? 0 : 1;
            return unchecked(baseSeed * 397 + restartIndex * 31 + modelSalt + unchecked((int)0x9E3779B9));
        }

        static bool TryBuildEdgeSignatures(BuiltInRandomMapTilesetCorpus corpus, out Dictionary<ushort, MetatileEdgeSignature> signatures)
        {
            signatures = new Dictionary<ushort, MetatileEdgeSignature>();
            if (corpus.ConfigData == null || corpus.ObjData == null) return false;

            foreach (ushort candidate in corpus.Candidates)
            {
                if (BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(candidate, corpus.ConfigData, corpus.ObjData, out MetatileEdgeSignature signature))
                    signatures[candidate] = signature;
            }
            return signatures.Count >= MinimumDistinctCandidates;
        }

        enum AttemptOutcome { Success, Exhausted }

        readonly struct AttemptResult
        {
            public AttemptResult(AttemptOutcome outcome, ushort[] mars) { Outcome = outcome; Mars = mars; }
            public AttemptOutcome Outcome { get; }
            public ushort[] Mars { get; }
        }

        /// <summary>
        /// One bounded backtracking search: row-major cell order, each cell tries a
        /// deterministically weighted-random-ordered candidate list, checking compatibility
        /// against its already-assigned west/north neighbors only (a plain DFS — correctness
        /// does not depend on forward-checking; the node budget bounds the cost of the
        /// exponential worst case). Iterative (explicit stack via <c>triedIndex</c>/
        /// <c>cellPointer</c>) so large maps cannot blow the CLR call stack.
        /// </summary>
        static AttemptResult RunAttempt(
            BuiltInRandomMapTilesetCorpus corpus,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            int width,
            int height,
            ushort[] currentGrid,
            int seed,
            bool requireDifferentFromSource,
            CancellationToken cancellationToken)
        {
            int totalCells = width * height;
            var rng = new Random(seed);
            IReadOnlyList<ushort>[] candidateOrder = new IReadOnlyList<ushort>[totalCells];
            int[] triedIndex = new int[totalCells];
            ushort[] assigned = new ushort[totalCells];
            bool[] hasValue = new bool[totalCells];

            // Precompute every cell's weighted-random candidate order up front, in row-major
            // order, from a single RNG stream — deterministic regardless of how many times
            // backtracking later revisits a given cell.
            for (int cell = 0; cell < totalCells; cell++)
            {
                int x = cell % width;
                int y = cell / width;
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                candidateOrder[cell] = OrderCandidates(corpus, border, rng);
            }

            int nodeBudget = ComputeNodeBudget(width, height);
            int nodeCount = 0;
            int cellPointer = 0;

            while (cellPointer >= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (cellPointer == totalCells)
                {
                    ushort[] candidateGrid = new ushort[totalCells];
                    Array.Copy(assigned, candidateGrid, totalCells);

                    bool identical = requireDifferentFromSource && currentGrid != null && SequenceEqual(candidateGrid, currentGrid);
                    if (!identical && PassesQualityGates(corpus, model, signatures, candidateGrid, width, height))
                        return new AttemptResult(AttemptOutcome.Success, candidateGrid);

                    // Reject (soft-fail): back up one cell and keep searching for a different completion.
                    cellPointer--;
                    continue;
                }

                IReadOnlyList<ushort> order = candidateOrder[cellPointer];
                int x = cellPointer % width;
                int y = cellPointer / width;
                bool hasWest = x > 0;
                bool hasNorth = y > 0;
                ushort westValue = hasWest ? assigned[cellPointer - 1] : (ushort)0;
                ushort northValue = hasNorth ? assigned[cellPointer - width] : (ushort)0;

                bool placed = false;
                for (int i = triedIndex[cellPointer]; i < order.Count; i++)
                {
                    nodeCount++;
                    if (nodeCount > nodeBudget)
                        return new AttemptResult(AttemptOutcome.Exhausted, null);
                    cancellationToken.ThrowIfCancellationRequested();

                    ushort candidate = order[i];
                    if (hasWest && !IsCompatible(corpus, model, signatures, westValue, candidate, horizontal: true)) continue;
                    if (hasNorth && !IsCompatible(corpus, model, signatures, northValue, candidate, horizontal: false)) continue;

                    assigned[cellPointer] = candidate;
                    hasValue[cellPointer] = true;
                    triedIndex[cellPointer] = i + 1;
                    cellPointer++;
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    triedIndex[cellPointer] = 0;
                    hasValue[cellPointer] = false;
                    cellPointer--;
                }
            }

            return new AttemptResult(AttemptOutcome.Exhausted, null);
        }

        static bool IsCompatible(
            BuiltInRandomMapTilesetCorpus corpus,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            ushort upstream,
            ushort candidate,
            bool horizontal)
        {
            if (model == BuiltInRandomMapAdjacencyModel.Strict)
            {
                var table = horizontal ? corpus.HorizontalAdjacency : corpus.VerticalAdjacency;
                return table.TryGetValue(upstream, out IReadOnlySet<ushort> allowed) && allowed.Contains(candidate);
            }

            if (!signatures.TryGetValue(upstream, out MetatileEdgeSignature upstreamSig)) return false;
            if (!signatures.TryGetValue(candidate, out MetatileEdgeSignature candidateSig)) return false;
            return horizontal
                ? BuiltInRandomMapEdgeSignatureCore.HorizontallyCompatible(upstreamSig, candidateSig)
                : BuiltInRandomMapEdgeSignatureCore.VerticallyCompatible(upstreamSig, candidateSig);
        }

        static bool PassesQualityGates(
            BuiltInRandomMapTilesetCorpus corpus,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            ushort[] mars,
            int width,
            int height)
        {
            if (mars.Length != width * height) return false;

            for (int i = 0; i < mars.Length; i++)
                if (!BuiltInRandomMapTilesetCore.IsMarRenderable(mars[i], corpus.ConfigData)) return false;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort here = mars[y * width + x];
                    if (x + 1 < width && !IsCompatible(corpus, model, signatures, here, mars[y * width + x + 1], horizontal: true)) return false;
                    if (y + 1 < height && !IsCompatible(corpus, model, signatures, here, mars[(y + 1) * width + x], horizontal: false)) return false;
                }
            }

            if (corpus.Candidates.Count >= DiversityGateCandidateThreshold && mars.Length >= DiversityGateCandidateThreshold)
            {
                var counts = new Dictionary<ushort, int>();
                foreach (ushort v in mars)
                {
                    counts.TryGetValue(v, out int c);
                    counts[v] = c + 1;
                }
                if (counts.Count < DiversityGateCandidateThreshold) return false;

                int maxCount = 0;
                foreach (int c in counts.Values) if (c > maxCount) maxCount = c;
                if (maxCount > mars.Length * MaxSingleChipOccupancy) return false;
            }

            return true;
        }

        static List<ushort> OrderCandidates(BuiltInRandomMapTilesetCorpus corpus, bool border, Random rng)
        {
            var scored = new List<(ushort value, double key)>(corpus.Candidates.Count);
            foreach (ushort candidate in corpus.Candidates)
            {
                double weight = 1;
                if (border && corpus.BorderFrequency.TryGetValue(candidate, out long borderCount) && borderCount > 0)
                    weight = borderCount;
                else if (corpus.Frequency.TryGetValue(candidate, out long overallCount) && overallCount > 0)
                    weight = overallCount;

                double u = rng.NextDouble();
                // A-Res weighted-random-permutation key; higher key = earlier in the order.
                double key = Math.Pow(u, 1.0 / weight);
                scored.Add((candidate, key));
            }
            scored.Sort((a, b) => b.key.CompareTo(a.key));

            var result = new List<ushort>(scored.Count);
            foreach ((ushort value, double _) in scored) result.Add(value);
            return result;
        }

        static bool SequenceEqual(ushort[] a, ushort[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static int CountDistinct(ushort[] mars)
        {
            var set = new HashSet<ushort>();
            foreach (ushort v in mars) set.Add(v);
            return set.Count;
        }
    }
}
