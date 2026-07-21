// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Independently designed, clean-room, deterministic built-in random-map generator. Given
    /// a <see cref="BuiltInRandomMapTilesetCorpus"/> gathered from the current ROM's own maps,
    /// this performs a bounded, iterative constraint-satisfaction search over a grid of MAR
    /// values — minimum-remaining-values (MRV) cell selection with queue-based four-neighbor
    /// constraint propagation to a local fixed point (arc consistency, not a one-hop prune, and
    /// not a plain row-major scan) — honoring either the strictly-observed directional adjacency
    /// model or (when strict evidence is too sparse) the structural edge-signature relaxation. No uniform-fill or identity fallback exists: every
    /// non-<see cref="BuiltInRandomMapErrorCategory.None"/> result reports a specific, honest
    /// failure reason. See <c>docs/CORE-SEAMS.md</c> ("Built-in Random Map Generator") for the
    /// full behavioral contract and <c>docs/ENGINEERING-NOTES.md</c> for the clean-room design
    /// provenance.
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
            try
            {
                if (!BuiltInRandomMapCorpusCore.TryBuildCorpus(
                    rom,
                    mapSettingAddr,
                    cancellationToken,
                    out BuiltInRandomMapTilesetCorpus corpus,
                    out error))
                {
                    return false;
                }

                result = Generate(corpus, width, height, currentGrid, seed, cancellationToken);
                error = result.ErrorMessage;
            }
            catch (OperationCanceledException)
            {
                result = Failure(
                    BuiltInRandomMapErrorCategory.Cancelled,
                    "Random map generation was cancelled.");
                error = result.ErrorMessage;
            }
            return true;
        }

        /// <summary>
        /// Generate a <paramref name="width"/> x <paramref name="height"/> layout from
        /// <paramref name="corpus"/>. Never mutates <paramref name="corpus"/> or
        /// <paramref name="currentGrid"/>; <see cref="BuiltInRandomMapGenerationResult.Mars"/>
        /// is always a freshly allocated array. Candidate MAR values are prefiltered through
        /// <see cref="BuiltInRandomMapTilesetCore.IsMarRenderable"/> before any model or search
        /// state is built, so a value that can never render cannot consume node budget.
        /// Source-identity condition (Plan v4 §2.6, absolute — no identity/uniform-fill success
        /// fallback): EVERY restart of EVERY model, including the ladder's very last attempt,
        /// rejects a completion that is sequence-identical to <paramref name="currentGrid"/> and
        /// keeps backtracking within that attempt for a distinct one. If no attempt in the whole
        /// ladder ever finds a quality-gated completion that differs from <paramref name="currentGrid"/>,
        /// generation reports <see cref="BuiltInRandomMapErrorCategory.SearchExhausted"/> —
        /// there is no attempt, final or otherwise, that may fall back to returning the source
        /// grid unchanged. When <paramref name="currentGrid"/> is null this rule never applies
        /// (there is nothing to be identical to).
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

            // Prefilter to only renderable candidates *before* any model/search construction:
            // a MAR value that can never legally render must not consume node budget or be
            // able to trigger a spurious SearchExhausted. corpus.Candidates is already
            // ascending, so a single forward pass preserves that deterministic order.
            var candidates = new List<ushort>(corpus.Candidates.Count);
            foreach (ushort candidate in corpus.Candidates)
                if (BuiltInRandomMapTilesetCore.IsMarRenderable(candidate, corpus.ConfigData))
                    candidates.Add(candidate);

            if (candidates.Count < MinimumDistinctCandidates)
                return Failure(BuiltInRandomMapErrorCategory.InsufficientSourceData,
                    $"Corpus offers only {candidates.Count} renderable chipset(s) (of {corpus.Candidates.Count} total); at least {MinimumDistinctCandidates} are required.");

            bool relaxedAvailable = TryBuildEdgeSignatures(corpus, candidates, out Dictionary<ushort, MetatileEdgeSignature> signatures);
            if (!corpus.HasStrictAdjacencyEvidence && !relaxedAvailable)
                return Failure(BuiltInRandomMapErrorCategory.InsufficientSourceData, "Corpus has neither observed adjacency pairs nor decodable edge signatures.");

            try
            {
                var attempts = new List<(BuiltInRandomMapAdjacencyModel model, int restart)>();
                if (corpus.HasStrictAdjacencyEvidence)
                    for (int r = 0; r < RestartCount; r++) attempts.Add((BuiltInRandomMapAdjacencyModel.Strict, r));
                if (relaxedAvailable)
                    for (int r = 0; r < RestartCount; r++) attempts.Add((BuiltInRandomMapAdjacencyModel.EdgeRelaxed, r));

                // Model-scoped diversity viability (Plan v4 solver review, comment 3621647167):
                // computed once per distinct model — not per restart, and never re-derived
                // inside the hot search loop — then reused across every restart of that model.
                var viableByModel = new Dictionary<BuiltInRandomMapAdjacencyModel, IReadOnlyList<ushort>>();

                for (int i = 0; i < attempts.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (BuiltInRandomMapAdjacencyModel model, int restart) = attempts[i];
                    int derivedSeed = DeriveSeed(seed, restart, model);

                    if (!viableByModel.TryGetValue(model, out IReadOnlyList<ushort> viableCandidates))
                    {
                        viableCandidates = ComputeViableCandidates(corpus, candidates, model, signatures, width, height, cancellationToken);
                        viableByModel[model] = viableCandidates;
                    }

                    AttemptResult attempt = RunAttempt(
                        corpus, candidates, viableCandidates, model, signatures, width, height, currentGrid,
                        derivedSeed, cancellationToken);

                    if (attempt.Outcome == AttemptOutcome.Success)
                    {
                        int distinct = CountDistinct(attempt.Mars);
                        return new BuiltInRandomMapGenerationResult(
                            true, BuiltInRandomMapErrorCategory.None, "Generated successfully.",
                            attempt.Mars, seed, model, restart + 1, distinct);
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

        static bool TryBuildEdgeSignatures(BuiltInRandomMapTilesetCorpus corpus, IReadOnlyList<ushort> candidates, out Dictionary<ushort, MetatileEdgeSignature> signatures)
        {
            signatures = new Dictionary<ushort, MetatileEdgeSignature>();
            if (corpus.ConfigData == null || corpus.ObjData == null || corpus.PaletteData == null)
                return false;

            foreach (ushort candidate in candidates)
            {
                if (BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(
                    candidate,
                    corpus.ConfigData,
                    corpus.ObjData,
                    out MetatileEdgeSignature signature))
                {
                    signatures[candidate] = signature;
                }
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

        /// <summary>Which side of an already-assigned cell a domain-filtering neighbor sits on.</summary>
        enum Direction { West, East, North, South }

        /// <summary>
        /// One decision level of the iterative CSP search: the cell chosen (by MRV) at this
        /// depth, its deterministically weighted candidate order (computed once, from its
        /// domain snapshot at selection time), a cursor into that order, and the exact
        /// per-neighbor domain removals caused by whichever candidate is currently committed
        /// — restored verbatim before the next candidate (or the parent frame) is tried.
        /// </summary>
        sealed class SearchFrame
        {
            public SearchFrame(int cell, List<ushort> order) { Cell = cell; Order = order; }
            public int Cell { get; }
            public List<ushort> Order { get; }
            public int NextIndex { get; set; }
            public List<(int Cell, ushort Value)> Removals { get; set; }
        }

        /// <summary>
        /// One bounded search over a mutable-domain CSP: iterative (explicit <see cref="SearchFrame"/>
        /// stack, no recursion, so large maps cannot blow the CLR call stack), minimum-remaining-
        /// values (MRV) cell selection with deterministic row-major tie-breaking, seeded
        /// weighted-random candidate ordering restricted to the selected cell's live domain, and
        /// four-neighbor (west/east/north/south) constraint propagation on every assignment —
        /// queue-based to a local fixed point (arc consistency over the grid's adjacency arcs),
        /// not merely a one-hop neighbor prune: narrowing one cell's domain re-examines its own
        /// unassigned neighbors too, cascading until nothing shrinks further or some domain
        /// empties (see <c>PropagateFromChangedCell</c>/<c>Revise</c>). Backtracking restores
        /// exactly every domain value the whole cascade removed before trying the next candidate
        /// (or unwinding to the parent). The node budget bounds the cost of the exponential worst
        /// case by counting every candidate assignment attempt (whether or not propagation
        /// subsequently accepts it) — propagation itself never advances the node count.
        /// </summary>
        static AttemptResult RunAttempt(
            BuiltInRandomMapTilesetCorpus corpus,
            IReadOnlyList<ushort> candidates,
            IReadOnlyList<ushort> viableCandidates,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            int width,
            int height,
            ushort[] currentGrid,
            int seed,
            CancellationToken cancellationToken)
        {
            int totalCells = width * height;
            var rng = new Random(seed);

            var domain = new HashSet<ushort>[totalCells];
            for (int cell = 0; cell < totalCells; cell++)
                domain[cell] = new HashSet<ushort>(candidates);

            bool[] isAssigned = new bool[totalCells];
            ushort[] assignedValue = new ushort[totalCells];
            int assignedCount = 0;

            int nodeBudget = ComputeNodeBudget(width, height);
            int nodeCount = 0;

            var stack = new List<SearchFrame>(totalCells);

            // Worklist membership for propagation (see TryPropagate/PropagateFromChangedCell):
            // an invariant maintained purely by Enqueue/Dequeue bookkeeping — every cell that is
            // ever enqueued during one propagation call is dequeued (and its flag cleared) before
            // that call returns, on both the success and failure path — so this array is always
            // all-false between calls without needing an explicit O(totalCells) reset.
            bool[] inQueue = new bool[totalCells];

            bool IsBorder(int cell)
            {
                int x = cell % width;
                int y = cell / width;
                return x == 0 || y == 0 || x == width - 1 || y == height - 1;
            }

            int WestOf(int cell) => cell % width > 0 ? cell - 1 : -1;
            int EastOf(int cell) => cell % width < width - 1 ? cell + 1 : -1;
            int NorthOf(int cell) => cell / width > 0 ? cell - width : -1;
            int SouthOf(int cell) => cell / width < height - 1 ? cell + width : -1;

            // Arc-consistency revision of one cell's domain against one live neighbor: remove
            // every value from domain[cell] that has no supporting value remaining in
            // domain[neighbor] under the directional constraint from cell to neighbor. This is
            // the general form of a single-hop forward-check — when domain[neighbor] happens to
            // be a freshly collapsed singleton it degenerates to exactly that — so the same
            // routine serves both the immediate assignment and every later cascade step.
            bool Revise(int cell, int neighbor, Direction directionToNeighbor, List<(int Cell, ushort Value)> removals)
            {
                if (neighbor < 0) return true;
                cancellationToken.ThrowIfCancellationRequested();

                HashSet<ushort> cellDomain = domain[cell];
                HashSet<ushort> neighborDomain = domain[neighbor];
                List<ushort> toRemove = null;
                foreach (ushort v in cellDomain)
                {
                    bool supported = false;
                    foreach (ushort w in neighborDomain)
                    {
                        if (IsNeighborCompatible(corpus, model, signatures, v, directionToNeighbor, w)) { supported = true; break; }
                    }
                    if (!supported) (toRemove ??= new List<ushort>()).Add(v);
                }
                if (toRemove != null)
                {
                    foreach (ushort v in toRemove)
                    {
                        cellDomain.Remove(v);
                        removals.Add((cell, v));
                    }
                }
                return cellDomain.Count > 0;
            }

            void EnqueueNeighborsOf(int cell, Queue<int> queue)
            {
                void Enqueue(int c)
                {
                    if (c < 0 || isAssigned[c] || inQueue[c]) return;
                    inQueue[c] = true;
                    queue.Enqueue(c);
                }
                Enqueue(WestOf(cell)); Enqueue(EastOf(cell)); Enqueue(NorthOf(cell)); Enqueue(SouthOf(cell));
            }

            // Queue-based propagation to a local fixed point (arc consistency over the grid's
            // 4-neighbor arcs), not just a one-hop neighbor prune: after the cell that changed
            // (initially the freshly assigned cell) is revised against each of its neighbors,
            // any neighbor whose domain actually shrank has ITS other neighbors re-enqueued too
            // — because a value that only had support from the value(s) just removed may no
            // longer be supportable — and so on until nothing shrinks further or some cell's
            // domain empties. Domains only ever shrink within one propagation call, so this
            // always terminates. Every removal anywhere in the cascade lands in one flat
            // `removals` list for exact restoration on backtrack, exactly as before.
            bool PropagateFromChangedCell(int changedCell, List<(int Cell, ushort Value)> removals)
            {
                var queue = new Queue<int>();
                EnqueueNeighborsOf(changedCell, queue);

                while (queue.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int cell = queue.Dequeue();
                    inQueue[cell] = false;

                    int before = domain[cell].Count;
                    bool ok =
                        Revise(cell, WestOf(cell), Direction.West, removals) &&
                        Revise(cell, EastOf(cell), Direction.East, removals) &&
                        Revise(cell, NorthOf(cell), Direction.North, removals) &&
                        Revise(cell, SouthOf(cell), Direction.South, removals);

                    if (!ok)
                    {
                        while (queue.Count > 0) inQueue[queue.Dequeue()] = false;
                        return false;
                    }

                    if (domain[cell].Count < before)
                        EnqueueNeighborsOf(cell, queue);
                }
                return true;
            }

            // Commit one candidate value and propagate its consequences to a fixed point. On
            // failure, every removal this attempt made — including the assigned cell's own
            // collapse to a singleton, below — is restored before returning, so the caller can
            // try the next candidate against a clean domain state (exact restoration, never
            // "best effort").
            bool TryPropagate(int cell, ushort candidateValue, out List<(int Cell, ushort Value)> removals)
            {
                removals = new List<(int Cell, ushort Value)>();

                // Collapse the assigned cell's own domain to exactly the committed value *before*
                // cascading, and mark it assigned for the duration of this call, so every Revise
                // against it (directly, or several hops later in the cascade) sees the correct,
                // fully-fixed domain and the worklist never re-narrows or re-visits it as if it
                // were still an open choice. The removed alternatives are ordinary removals,
                // restored on failure exactly like any other cell's.
                HashSet<ushort> assignedDomain = domain[cell];
                foreach (ushort other in assignedDomain)
                    if (other != candidateValue) removals.Add((cell, other));
                assignedDomain.Clear();
                assignedDomain.Add(candidateValue);
                isAssigned[cell] = true;

                bool ok = PropagateFromChangedCell(cell, removals);

                if (!ok)
                {
                    foreach ((int removedCell, ushort removedValue) in removals) domain[removedCell].Add(removedValue);
                    removals = null;
                    isAssigned[cell] = false;
                }
                return ok;
            }

            // Try the frame's remaining ordered candidates, in order, until one both exists in
            // the cell's (possibly since-narrowed) domain and propagates without emptying any
            // unassigned neighbor's domain. Every entry examined counts as one search node.
            bool TryAdvanceFrame(SearchFrame frame, out bool budgetExceeded)
            {
                budgetExceeded = false;
                while (frame.NextIndex < frame.Order.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nodeCount++;
                    if (nodeCount > nodeBudget)
                    {
                        budgetExceeded = true;
                        return false;
                    }

                    ushort candidateValue = frame.Order[frame.NextIndex++];
                    if (!domain[frame.Cell].Contains(candidateValue)) continue;
                    if (!TryPropagate(frame.Cell, candidateValue, out List<(int Cell, ushort Value)> removals)) continue;

                    isAssigned[frame.Cell] = true;
                    assignedValue[frame.Cell] = candidateValue;
                    assignedCount++;
                    frame.Removals = removals;
                    return true;
                }
                return false;
            }

            // Undo exactly what committing this frame's current candidate changed: restore
            // every value propagation removed from a neighbor's domain, then clear the
            // assignment itself. Called both when retrying a frame with its next candidate and
            // when unwinding a frame entirely.
            void UndoFrame(SearchFrame frame)
            {
                if (frame.Removals != null)
                {
                    foreach ((int removedCell, ushort removedValue) in frame.Removals) domain[removedCell].Add(removedValue);
                    frame.Removals = null;
                }
                isAssigned[frame.Cell] = false;
                assignedCount--;
            }

            // Unwind the stack: undo the top frame's current candidate and try its next one;
            // if that frame's order is exhausted, pop it and keep unwinding into its parent.
            // Returns false when the whole stack is exhausted (no solution reachable) or the
            // node budget is spent while resuming — both end the attempt identically.
            bool Backtrack()
            {
                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SearchFrame top = stack[stack.Count - 1];
                    UndoFrame(top);
                    if (TryAdvanceFrame(top, out bool budgetExceeded))
                        return true;
                    if (budgetExceeded)
                        return false;
                    stack.RemoveAt(stack.Count - 1);
                }
                return false;
            }

            // Minimum-remaining-values selection over every unassigned cell, scanning in
            // row-major order so ties (including the uniform initial state) deterministically
            // resolve to the lowest cell index.
            int SelectMrvCell()
            {
                int best = -1;
                int bestSize = int.MaxValue;
                for (int cell = 0; cell < totalCells; cell++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (isAssigned[cell]) continue;
                    int size = domain[cell].Count;
                    if (size < bestSize)
                    {
                        bestSize = size;
                        best = cell;
                        if (bestSize == 0) break;
                    }
                }
                return best;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (assignedCount == totalCells)
                {
                    ushort[] candidateGrid = new ushort[totalCells];
                    Array.Copy(assignedValue, candidateGrid, totalCells);

                    bool identical = currentGrid != null && SequenceEqual(candidateGrid, currentGrid);
                    if (!identical && PassesQualityGates(corpus, viableCandidates, model, signatures, candidateGrid, width, height))
                        return new AttemptResult(AttemptOutcome.Success, candidateGrid);

                    // Reject (soft-fail): back up and keep searching for a different completion.
                    if (!Backtrack())
                        return new AttemptResult(AttemptOutcome.Exhausted, null);
                    continue;
                }

                int cell = SelectMrvCell();
                if (domain[cell].Count == 0)
                {
                    if (!Backtrack())
                        return new AttemptResult(AttemptOutcome.Exhausted, null);
                    continue;
                }

                var sortedDomain = new List<ushort>(domain[cell]);
                sortedDomain.Sort();
                List<ushort> order = OrderCandidates(corpus, sortedDomain, IsBorder(cell), rng, cancellationToken);

                var frame = new SearchFrame(cell, order);
                stack.Add(frame);

                if (TryAdvanceFrame(frame, out bool freshBudgetExceeded))
                    continue;

                stack.RemoveAt(stack.Count - 1);
                if (freshBudgetExceeded || !Backtrack())
                    return new AttemptResult(AttemptOutcome.Exhausted, null);
            }
        }

        /// <summary>
        /// Directional adjacency check for the strict model (observed pairs) or the edge-relaxed
        /// model (structural edge-signature compatibility): is <paramref name="candidate"/> a
        /// legal immediate successor of <paramref name="upstream"/> — east-of when
        /// <paramref name="horizontal"/>, south-of otherwise?
        /// </summary>
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

        /// <summary>
        /// Is <paramref name="neighborCandidate"/> — a value still in a neighbor's live domain —
        /// compatible with <paramref name="assignedValue"/> just placed at the cell that neighbor
        /// sits <paramref name="directionToNeighbor"/> of? West/north neighbors are the upstream
        /// side of <see cref="IsCompatible"/>'s directional check; east/south neighbors are the
        /// downstream side. Keeping this explicit (rather than inlining the swap at each call
        /// site) is what makes it possible to get west vs. east and north vs. south right.
        /// </summary>
        static bool IsNeighborCompatible(
            BuiltInRandomMapTilesetCorpus corpus,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            ushort assignedValue,
            Direction directionToNeighbor,
            ushort neighborCandidate)
        {
            switch (directionToNeighbor)
            {
                case Direction.West:
                    return IsCompatible(corpus, model, signatures, neighborCandidate, assignedValue, horizontal: true);
                case Direction.East:
                    return IsCompatible(corpus, model, signatures, assignedValue, neighborCandidate, horizontal: true);
                case Direction.North:
                    return IsCompatible(corpus, model, signatures, neighborCandidate, assignedValue, horizontal: false);
                case Direction.South:
                    return IsCompatible(corpus, model, signatures, assignedValue, neighborCandidate, horizontal: false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(directionToNeighbor));
            }
        }

        /// <summary>
        /// Candidates that can actually occupy *some* grid position of these dimensions —
        /// excluding ones that are structurally isolated under <paramref name="model"/> (Plan v4
        /// solver review, comment 3621647167). This is deliberately NOT "has both an outgoing
        /// AND an incoming partner": a grid position's requirement per axis is exactly one of
        /// three shapes — the low edge (x==0 / y==0, needing only an outgoing/successor partner,
        /// since there is no upstream neighbor there), the high edge (needing only an
        /// incoming/predecessor partner), or an interior position (needing both). The low and
        /// high edges both always exist whenever an axis has more than one position, so "has an
        /// outgoing partner OR has an incoming partner" is exactly necessary and sufficient for
        /// at least one feasible position on that axis to exist; requiring both unconditionally
        /// would wrongly reject a candidate that is only ever placeable at an edge/corner (e.g.
        /// one whose only observed adjacency is "may end a row/column"). Horizontal and vertical
        /// requirements fall on independent neighbor cells, so a position satisfying each axis
        /// independently combines into one real (x,y) — the overall check is simply "axis
        /// satisfiable" for every axis the grid's dimensions require: horizontal only when
        /// <paramref name="width"/> &gt; 1, vertical only when <paramref name="height"/> &gt; 1.
        /// Computed once per model attempt from a sorted snapshot of <paramref name="candidates"/>,
        /// so the result never depends on hash-bucket iteration order.
        /// </summary>
        static List<ushort> ComputeViableCandidates(
            BuiltInRandomMapTilesetCorpus corpus,
            IReadOnlyList<ushort> candidates,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            bool needHorizontal = width > 1;
            bool needVertical = height > 1;

            var sorted = new List<ushort>(candidates);
            sorted.Sort();

            var viable = new List<ushort>(sorted.Count);
            foreach (ushort candidate in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool ok = true;

                if (needHorizontal)
                    ok = HasAxisSupport(corpus, model, signatures, sorted, candidate, horizontal: true, cancellationToken);
                if (ok && needVertical)
                    ok = HasAxisSupport(corpus, model, signatures, sorted, candidate, horizontal: false, cancellationToken);

                if (ok) viable.Add(candidate);
            }
            return viable;
        }

        /// <summary>
        /// Does <paramref name="candidate"/> have at least one compatible outgoing OR incoming
        /// partner (including itself) along this axis — i.e. can it occupy at least the low or
        /// the high edge position on this axis? (See <see cref="ComputeViableCandidates"/> for
        /// why OR, not AND, is the correct existence check.) A fully isolated candidate — no
        /// outgoing and no incoming partner at all — still fails this and is excluded.
        /// </summary>
        static bool HasAxisSupport(
            BuiltInRandomMapTilesetCorpus corpus,
            BuiltInRandomMapAdjacencyModel model,
            Dictionary<ushort, MetatileEdgeSignature> signatures,
            List<ushort> sortedCandidates,
            ushort candidate,
            bool horizontal,
            CancellationToken cancellationToken)
        {
            bool hasOutgoing = false;
            bool hasIncoming = false;
            foreach (ushort other in sortedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!hasOutgoing && IsCompatible(corpus, model, signatures, candidate, other, horizontal)) hasOutgoing = true;
                if (!hasIncoming && IsCompatible(corpus, model, signatures, other, candidate, horizontal)) hasIncoming = true;
                if (hasOutgoing && hasIncoming) break;
            }
            return hasOutgoing || hasIncoming;
        }

        /// <summary>
        /// Diversity/occupancy/renderability/adjacency quality gates for one complete candidate
        /// grid. <paramref name="viableCandidates"/> (not the raw prefiltered candidate list)
        /// gates the diversity check (Plan v4 solver review, comment 3621647167): a corpus
        /// offering three renderable MAR values where one is structurally isolated under
        /// <paramref name="model"/> must not be held to a three-distinct-values bar no
        /// completion could ever satisfy.
        /// </summary>
        static bool PassesQualityGates(
            BuiltInRandomMapTilesetCorpus corpus,
            IReadOnlyList<ushort> viableCandidates,
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

            if (viableCandidates.Count >= DiversityGateCandidateThreshold && mars.Length >= DiversityGateCandidateThreshold)
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

        /// <summary>
        /// Deterministic weighted-random permutation (A-Res) of <paramref name="sortedDomain"/> —
        /// which must already be in a stable (ascending) order so the result depends only on the
        /// RNG stream, never on hash-bucket iteration order. Border cells weight by observed
        /// border frequency; interior cells weight by overall frequency.
        /// </summary>
        static List<ushort> OrderCandidates(
            BuiltInRandomMapTilesetCorpus corpus,
            List<ushort> sortedDomain,
            bool border,
            Random rng,
            CancellationToken cancellationToken)
        {
            var scored = new List<(ushort value, double key)>(sortedDomain.Count);
            foreach (ushort candidate in sortedDomain)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            scored.Sort((a, b) =>
            {
                int keyComparison = b.key.CompareTo(a.key);
                return keyComparison != 0 ? keyComparison : a.value.CompareTo(b.value);
            });

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
