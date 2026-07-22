// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BuiltInRandomMapGeneratorCoreTests
    {
        const int Width = 15;
        const int Height = 10;
        const int BytesPerTile = 32;

        static IReadOnlySet<ushort> Set(params ushort[] values) => new SortedSet<ushort>(values);

        static BuiltInRandomMapTilesetCorpus MakeStrictCorpus()
        {
            // Candidates 0, 4, 8 form a fully-connected ring in both directions (each value
            // may be immediately west/north of every value, including itself), so any
            // complete grid built from them is strict-compatible and has ample diversity.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 100, [4] = 100, [8] = 100 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            return BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: new byte[64],
                totalCells: Width * Height);
        }

        [Fact]
        public void Generate_StrictModelCorpus_SucceedsAndIsQualityGated()
        {
            var corpus = MakeStrictCorpus();
            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, currentGrid: null, seed: 12345, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(BuiltInRandomMapAdjacencyModel.Strict, result.AdjacencyModel);
            Assert.Equal(Width * Height, result.Mars.Length);
            foreach (ushort mar in result.Mars)
                Assert.Contains(mar, new ushort[] { 0, 4, 8 });
            Assert.True(result.DistinctChipsetsUsed >= 3);

            // Verify every horizontal/vertical neighbor pair is one the strict model allows.
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    ushort v = result.Mars[y * Width + x];
                    if (x > 0)
                    {
                        ushort west = result.Mars[y * Width + x - 1];
                        Assert.Contains(v, corpus.HorizontalAdjacency[west]);
                    }
                    if (y > 0)
                    {
                        ushort north = result.Mars[(y - 1) * Width + x];
                        Assert.Contains(v, corpus.VerticalAdjacency[north]);
                    }
                }
            }
        }

        [Fact]
        public void Generate_SameSeedAndCorpus_IsDeterministic()
        {
            var corpus = MakeStrictCorpus();
            var r1 = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 777, CancellationToken.None);
            var r2 = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 777, CancellationToken.None);

            Assert.True(r1.Success);
            Assert.True(r2.Success);
            Assert.Equal(r1.Mars, r2.Mars);
            Assert.Equal(r1.EffectiveSeed, r2.EffectiveSeed);
            Assert.Equal(r1.RestartsUsed, r2.RestartsUsed);
        }

        [Fact]
        public void Generate_EffectiveSeed_IsCallerSeed_NotInternalDerivedRestartSeed()
        {
            // Plan v4 review fix: EffectiveSeed must be directly replayable — i.e. exactly the
            // seed the caller passed in, never the internal per-restart derived value.
            var corpus = MakeStrictCorpus();
            const int callerSeed = 999;
            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: callerSeed, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(callerSeed, result.EffectiveSeed);
        }

        [Fact]
        public void Generate_ReplayingWithResultEffectiveSeed_ReproducesIdenticalOutcome()
        {
            // Plan v4 review fix: calling Generate again with result.EffectiveSeed (rather than
            // the original caller seed variable) must deterministically reproduce the same
            // Mars/AdjacencyModel/RestartsUsed outcome — proving the seed is truly replayable.
            var corpus = MakeStrictCorpus();
            var first = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 424242, CancellationToken.None);
            Assert.True(first.Success, first.ErrorMessage);

            var replay = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: first.EffectiveSeed, CancellationToken.None);

            Assert.True(replay.Success, replay.ErrorMessage);
            Assert.Equal(first.Mars, replay.Mars);
            Assert.Equal(first.AdjacencyModel, replay.AdjacencyModel);
            Assert.Equal(first.RestartsUsed, replay.RestartsUsed);
            Assert.Equal(first.EffectiveSeed, replay.EffectiveSeed);
        }

        [Fact]
        public void Generate_DoesNotMutateCorpusOrCurrentGrid()
        {
            var corpus = MakeStrictCorpus();
            ushort[] currentGrid = new ushort[Width * Height];
            for (int i = 0; i < currentGrid.Length; i++) currentGrid[i] = (ushort)((i % 3) * 4);
            ushort[] currentGridCopy = (ushort[])currentGrid.Clone();
            var candidatesBefore = corpus.Candidates.ToArray();

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, currentGrid, seed: 55, CancellationToken.None);

            Assert.Equal(currentGridCopy, currentGrid);
            Assert.Equal(candidatesBefore, corpus.Candidates.ToArray());
            Assert.NotSame(currentGrid, result.Mars);

            // MakeStrictCorpus's fully-connected 0/4/8 ring admits an astronomically large
            // number of valid completions, so the source-difference rule (Generate rejects a
            // candidate identical to currentGrid, on every attempt, while an alternate is
            // still reachable) is expected to succeed on the very first restart with a grid
            // that differs from the deliberately-patterned currentGrid.
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEqual(currentGrid, result.Mars);
        }

        [Fact]
        public void GenerationResult_Mars_CannotMutateStoredOutcome()
        {
            ushort[] source = new ushort[] { 0, 4, 8 };
            var result = new BuiltInRandomMapGenerationResult(
                success: true,
                BuiltInRandomMapErrorCategory.None,
                "ok",
                source,
                effectiveSeed: 1,
                BuiltInRandomMapAdjacencyModel.Strict,
                restartsUsed: 1,
                distinctChipsetsUsed: 3);

            source[0] = 12;
            ushort[] exposed = result.Mars;
            exposed[1] = 12;

            Assert.Equal(new ushort[] { 0, 4, 8 }, result.Mars);
            Assert.NotSame(exposed, result.Mars);
            Assert.Equal(3, result.DistinctChipsetsUsed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Generate_FewerThanTwoCandidates_FailsInsufficientSourceData(int candidateCount)
        {
            var candidates = candidateCount == 0 ? new List<ushort>() : new List<ushort> { 0 };
            var freq = new Dictionary<ushort, long>();
            foreach (ushort c in candidates) freq[c] = 10;
            var empty = new Dictionary<ushort, IReadOnlySet<ushort>>();

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                empty, empty, null, null, null, Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InsufficientSourceData, result.ErrorCategory);
            Assert.Empty(result.Mars);
        }

        [Fact]
        public void Generate_NoAdjacencyEvidenceAndNoEdgeSignatures_FailsInsufficientSourceData()
        {
            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5 };
            var empty = new Dictionary<ushort, IReadOnlySet<ushort>>();

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                empty, empty, objData: null, paletteData: null, configData: null, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InsufficientSourceData, result.ErrorCategory);
        }

        [Fact]
        public void Generate_NoHorizontallyCompatibleNeighbors_FailsSearchExhaustedQuickly()
        {
            // Two candidates, each strictly observed as a border cell only (so
            // HasStrictAdjacencyEvidence's dictionaries exist) but with an *empty* successor
            // set for both directions: no cell wider than 1 can ever be completed. A valid,
            // non-null ConfigData covering both MAR 0 (tsaBase 0) and MAR 4 (tsaBase 8) is
            // supplied so both remain renderable and the failure is attributable to the
            // empty adjacency sets, not to the IsMarRenderable prefilter stripping candidates.
            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = new SortedSet<ushort>(),
                [4] = new SortedSet<ushort>(),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = new SortedSet<ushort>(),
                [4] = new SortedSet<ushort>(),
            };
            byte[] configData = new byte[16];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.SearchExhausted, result.ErrorCategory);
        }

        [Fact]
        public void Generate_OnlyOneModelViableCandidateWithTwoIsolatedExtras_SucceedsDespiteRawCandidateCountBeingThree()
        {
            // Plan v4 solver review (comment 3621647167): the diversity gate must be scoped to
            // candidates that are actually *viable* under the active model, not the raw
            // renderable candidate count. Candidates 0/4/8 give Candidates.Count == 3 (the
            // gate's raw threshold), but only "0 followed by 0" is ever a valid directional
            // pair in either direction — MAR 4 and MAR 8 have no adjacency evidence at all (as
            // upstream OR downstream) and so are structurally isolated: they can never be
            // placed next to anything, including themselves. ComputeViableCandidates must
            // exclude both, leaving exactly one viable candidate (0) — below the
            // DiversityGateCandidateThreshold — so the gate never engages and the unique
            // all-zero completion is accepted instead of being spuriously rejected for
            // "insufficient diversity" it could structurally never achieve. A valid, non-null
            // ConfigData is supplied so IsMarRenderable succeeds for every candidate and the
            // outcome is attributable to viability scoping rather than an unrelated
            // renderability failure; ObjData stays null so EdgeRelaxed is unavailable and only
            // the Strict ladder runs.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5, [8] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            byte[] configData = new byte[64]; // tsaBase for MAR=8 is 16; well within range.

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(Width * Height, result.Mars.Length);
            Assert.All(result.Mars, mar => Assert.Equal((ushort)0, mar));
        }

        [Fact]
        public void Generate_ThreeModelViableCandidatesButGridCanNeverMixThemAll_FailsSearchExhausted()
        {
            // True-positive companion to the isolated-candidate inversion above: every one of
            // 0/4/8 individually satisfies ComputeViableCandidates (each has at least one
            // compatible partner — outgoing or incoming, per HasAxisSupport — in both required
            // axes; here 0 and 4 in fact support each other bidirectionally and 8 supports
            // itself bidirectionally too, so this case is unaffected by the OR-vs-AND axis
            // relaxation), so none is "isolated" and the diversity gate legitimately activates.
            // But the grid is a single connected component, and 8 can never border 0 or 4 in
            // either direction, so every strict-complete grid is either entirely 0/4 (<=2
            // distinct values) or entirely 8 (1 distinct value) — never all three at once. The
            // gate must keep rejecting every completion it is handed. Local propagation cannot
            // detect this global impossibility, so the search must eventually exhaust without
            // fabricating a low-diversity success.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5, [8] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4),
                [4] = Set(0, 4),
                [8] = Set(8),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4),
                [4] = Set(0, 4),
                [8] = Set(8),
            };
            byte[] configData = new byte[64];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.SearchExhausted, result.ErrorCategory);
        }

        [Fact]
        public void ComputeViableCandidates_IncludesBoundaryOnlyCandidateAndExcludesIsolatedCandidate()
        {
            var candidates = new List<ushort> { 0, 4, 8, 12 };
            var frequency = candidates.ToDictionary(candidate => candidate, _ => 1L);
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4),
                [4] = Set(0, 4),
                [8] = Set(0),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty,
                new List<uint> { 0 },
                candidates,
                frequency,
                frequency,
                horizontal,
                vertical,
                objData: null,
                paletteData: null,
                configData: new byte[32],
                totalCells: Width * Height);
            MethodInfo method = typeof(BuiltInRandomMapGeneratorCore).GetMethod(
                "ComputeViableCandidates",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var viable = Assert.IsType<List<ushort>>(method.Invoke(
                null,
                new object[]
                {
                    corpus,
                    candidates,
                    BuiltInRandomMapAdjacencyModel.Strict,
                    new Dictionary<ushort, MetatileEdgeSignature>(),
                    Width,
                    Height,
                    CancellationToken.None,
                }));

            Assert.Equal(new ushort[] { 0, 4, 8 }, viable);
        }

        [Fact]
        public void Generate_BoundaryOnlyViableCandidate_CountsTowardDiversityAndOnlyOccupiesColumnZero()
        {
            // User-clarification regression for ComputeViableCandidates/HasAxisSupport: model
            // viability must mean "can occupy at least one actual grid position", evaluated as
            // outgoing-OR-incoming per axis — not outgoing-AND-incoming. Candidate 8 here has an
            // outgoing horizontal partner (8 -> 0) but NO incoming one (no value's horizontal
            // set ever contains 8), so it can only ever legally sit at the horizontal low edge,
            // x == 0 (the one position with no west neighbor to satisfy). Under the OLD
            // AND-based check this candidate would have been wrongly excluded from viability
            // (leaving only {0, 4}, count == 2, below DiversityGateCandidateThreshold == 3), so
            // the diversity gate would never engage and a completion using only 0/4 (2 distinct
            // values) would be accepted. Under the corrected OR-based check, {0, 4, 8} are all
            // viable (count == 3), so the gate activates and the search must actually place 8
            // somewhere to reach 3 distinct values. Vertical adjacency is fully permissive for
            // all three candidates so only the horizontal axis is exercised by this case.
            // BorderFrequency (reusing the same dictionary as Frequency, as in the corpus
            // construction below) gives candidate 8 an extreme weight so the A-Res weighted
            // order (see OrderCandidates) makes it the practically-certain first choice at every
            // cell where it is still in-domain (u^(1/1_000_000) is within a hair of 1 for any
            // u > 0, versus a uniform u for the weight-1 candidates) — i.e. every cell in
            // column x == 0, which is the only column where propagation from a west neighbor
            // never excludes it. Once assigned, horizontal[8] = {0} forces column x == 1 to be
            // 0 in every row; columns x >= 2 are free to alternate between 0 and 4 (equal
            // weight), so both values remain available to make up the required diversity too.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 1, [4] = 1, [8] = 1_000_000 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4),
                [4] = Set(0, 4),
                [8] = Set(0), // outgoing only: nothing maps to 8, so it has no incoming partner.
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            byte[] configData = new byte[64];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 4242, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.DistinctChipsetsUsed >= 3, $"Expected >= 3 distinct MARs; got {result.DistinctChipsetsUsed}.");
            Assert.Contains((ushort)8, result.Mars);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (result.Mars[y * Width + x] == 8)
                        Assert.Equal(0, x); // 8 has no incoming partner, so it can only ever be at the west edge.
                }
            }
        }

        [Fact]
        public void Generate_SameSeedAcrossDifferentDictionaryConstructionOrders_ProducesIdenticalResult()
        {
            // Plan v4 solver review: MRV domain-size comparisons and candidate ordering must
            // never depend on HashSet/Dictionary enumeration order. Build two corpora that are
            // logically identical to MakeStrictCorpus but whose Candidates list and adjacency
            // dictionaries are populated in reversed/shuffled key order (still functionally
            // identical sets/maps) — Generate must produce byte-for-byte the same Mars for the
            // same seed regardless of construction order.
            IReadOnlySet<ushort> Ring() => Set(0, 4, 8);

            var forwardCandidates = new List<ushort> { 0, 4, 8 };
            var forwardFreq = new Dictionary<ushort, long> { [0] = 100, [4] = 100, [8] = 100 };
            var forwardAdjacency = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Ring(), [4] = Ring(), [8] = Ring(),
            };
            var forwardCorpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, forwardCandidates, forwardFreq, forwardFreq,
                forwardAdjacency, forwardAdjacency, objData: null, paletteData: null, configData: new byte[64],
                totalCells: Width * Height);

            var reversedCandidates = new List<ushort> { 8, 4, 0 };
            var reversedFreq = new Dictionary<ushort, long> { [8] = 100, [4] = 100, [0] = 100 };
            var reversedAdjacency = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [8] = Ring(), [4] = Ring(), [0] = Ring(),
            };
            var reversedCorpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, reversedCandidates, reversedFreq, reversedFreq,
                reversedAdjacency, reversedAdjacency, objData: null, paletteData: null, configData: new byte[64],
                totalCells: Width * Height);

            var forwardResult = BuiltInRandomMapGeneratorCore.Generate(forwardCorpus, Width, Height, null, seed: 2024, CancellationToken.None);
            var reversedResult = BuiltInRandomMapGeneratorCore.Generate(reversedCorpus, Width, Height, null, seed: 2024, CancellationToken.None);

            Assert.True(forwardResult.Success, forwardResult.ErrorMessage);
            Assert.True(reversedResult.Success, reversedResult.ErrorMessage);
            Assert.Equal(forwardResult.Mars, reversedResult.Mars);
            Assert.Equal(forwardResult.RestartsUsed, reversedResult.RestartsUsed);
        }

        [Fact]
        public void Generate_CyclicDirectionalAdjacency_PropagationForcesModularCompletionFromFirstCell()
        {
            // MRV + four-neighbor propagation regression targeting the exact west/east and
            // north/south Direction handling called out in the Plan v4 solver review: only the
            // transition value -> Next(value) is ever valid immediately east OR immediately
            // south of a cell (0->4, 4->8, 8->0; nothing else, in either axis). The unique valid
            // completion is fully determined by the first cell's value: Mars[x,y] ==
            // NextIter(Mars[0,0], x + y) — this holds regardless of propagation depth (even a
            // one-hop-per-assignment search would eventually reach the same forced grid, one
            // MRV step at a time), so it remains the right correctness check for the Direction
            // mapping itself: if west/east or north/south were ever swapped, this exact
            // algebraic relationship would not hold (or no completion would be found at all).
            // What queue-based cascading changes is *how quickly* that determinism is reached
            // internally (a single propagation call now reaches the full fixed point, versus
            // needing one MRV/assignment step per remaining cell under a one-hop-only search);
            // that internal difference isn't independently observable through the public
            // Generate() API (both eventually reach the same forced grid), so RestartsUsed == 1
            // below is asserted only as a general "no wasted retry" regression, not as a
            // cascading-specific proof.
            ushort Next(ushort v) => v == 0 ? (ushort)4 : v == 4 ? (ushort)8 : (ushort)0;

            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 10, [4] = 10, [8] = 10 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(4),
                [4] = Set(8),
                [8] = Set(0),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(4),
                [4] = Set(8),
                [8] = Set(0),
            };
            byte[] configData = new byte[64];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 31337, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, result.RestartsUsed); // fully forced from the first cell: no retry needed.
            ushort first = result.Mars[0];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    ushort expected = first;
                    for (int step = 0; step < x + y; step++) expected = Next(expected);
                    Assert.Equal(expected, result.Mars[y * Width + x]);
                }
            }
        }

        [Fact]
        public void Generate_FixedPointPropagation_ReachesBeyondImmediateNeighborsBeforeSelectingAnotherCell()
        {
            var candidates = new List<ushort> { 0, 4, 8 };
            var frequencyInner = new Dictionary<ushort, long> { [0] = 10, [4] = 10, [8] = 10 };
            var countingFrequency = new CountingReadOnlyDictionary(frequencyInner);
            var adjacencyInner = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(4),
                [4] = Set(8),
                [8] = Set(0),
            };
            using var cts = new CancellationTokenSource();
            // Viability consumes 24 lookups and revising cell 0's two immediate
            // neighbors consumes at most 30 more. Cancelling at 70 therefore
            // requires propagation to continue beyond that first neighbor ring,
            // while leaving a clear margin before any full-grid traversal.
            var cancellingAdjacency = new CancellingAdjacencyDictionary(
                adjacencyInner,
                cts,
                cancelAfterTryGetValueCalls: 70);
            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty,
                new List<uint> { 0 },
                candidates,
                countingFrequency,
                countingFrequency,
                cancellingAdjacency,
                cancellingAdjacency,
                objData: null,
                paletteData: null,
                configData: new byte[64],
                totalCells: Width * Height);

            BuiltInRandomMapGenerationResult result = BuiltInRandomMapGeneratorCore.Generate(
                corpus,
                Width,
                Height,
                currentGrid: null,
                seed: 31337,
                cts.Token);

            Assert.True(cts.IsCancellationRequested);
            Assert.Equal(BuiltInRandomMapErrorCategory.Cancelled, result.ErrorCategory);
            Assert.Equal(
                candidates.Count,
                countingFrequency.TryGetValueCalls);
        }

        [Fact]
        public void Generate_PreCancelledToken_FailsCancelledImmediately()
        {
            var corpus = MakeStrictCorpus();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, cts.Token);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.Cancelled, result.ErrorCategory);
        }

        [Fact]
        public void Generate_CancelledDuringCandidateOrderPrecomputation_StopsBeforeRemainingCells()
        {
            var candidates = new List<ushort> { 0, 4, 8 };
            var frequency = new Dictionary<ushort, long> { [0] = 1, [4] = 1, [8] = 1 };
            var adjacency = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            using var cts = new CancellationTokenSource();
            var cancellingBorderFrequency = new CancellingReadOnlyDictionary(
                frequency,
                cts,
                cancelAfterTryGetValueCalls: candidates.Count + 1);
            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty,
                new List<uint> { 0 },
                candidates,
                frequency,
                cancellingBorderFrequency,
                adjacency,
                adjacency,
                objData: null,
                paletteData: null,
                configData: new byte[64],
                totalCells: Width * Height);

            BuiltInRandomMapGenerationResult result = BuiltInRandomMapGeneratorCore.Generate(
                corpus,
                Width,
                Height,
                currentGrid: null,
                seed: 1,
                cts.Token);

            Assert.True(cts.IsCancellationRequested);
            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.Cancelled, result.ErrorCategory);
            Assert.True(
                cancellingBorderFrequency.TryGetValueCalls < Width * Height * candidates.Count,
                "Cancellation must be observed between cells instead of precomputing every remaining candidate order.");
        }

        [Fact]
        public void Generate_CancelledDuringViabilityScan_StopsBeforeRemainingCandidatePairs()
        {
            var candidates = Enumerable.Range(0, 256).Select(i => (ushort)(i * 4)).ToList();
            var frequency = candidates.ToDictionary(candidate => candidate, _ => 1L);
            using var cts = new CancellationTokenSource();
            var cancellingSet = new CancellingReadOnlySet(
                Array.Empty<ushort>(),
                cts,
                cancelAfterContainsCalls: 5);
            var adjacency = candidates.ToDictionary(
                candidate => candidate,
                _ => (IReadOnlySet<ushort>)cancellingSet);
            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty,
                new List<uint> { 0 },
                candidates,
                frequency,
                frequency,
                adjacency,
                adjacency,
                objData: null,
                paletteData: null,
                configData: new byte[candidates.Count * 8],
                totalCells: Width * Height);

            BuiltInRandomMapGenerationResult result = BuiltInRandomMapGeneratorCore.Generate(
                corpus,
                Width,
                Height,
                currentGrid: null,
                seed: 1,
                cts.Token);

            Assert.True(cts.IsCancellationRequested);
            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.Cancelled, result.ErrorCategory);
            Assert.True(
                cancellingSet.ContainsCalls < candidates.Count,
                "Cancellation must be observed inside the candidate-pair viability scan.");
        }

        [Fact]
        public void TryGenerateFromRom_PreCancelledCorpusScan_ReturnsTypedCancelledResult()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            ushort[] source = new ushort[Width * Height];
            for (int i = 0; i < source.Length; i++)
                source[i] = (ushort)((i % 3) * 4);
            uint mapSettingAddr = BuiltInRandomMapTestFixture.WriteMap(
                rom,
                mapIndex: 0,
                tilesetSlot: 1,
                objRaw: new byte[BytesPerTile],
                palRaw: IdentityPalette(),
                configRaw: new byte[64],
                Width,
                Height,
                source);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool resolved = BuiltInRandomMapGeneratorCore.TryGenerateFromRom(
                rom,
                mapSettingAddr,
                Width,
                Height,
                source,
                seed: 123,
                cts.Token,
                out BuiltInRandomMapGenerationResult result,
                out string error);

            Assert.True(resolved);
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.Cancelled, result.ErrorCategory);
            Assert.NotEmpty(error);
        }

        [Theory]
        [InlineData(14, 10)] // width below MAP_MIN_WIDTH
        [InlineData(15, 9)]  // height below MAP_MIN_HEIGHT
        [InlineData(15, 64)] // height above MAP_MAX_HEIGHT
        public void Generate_OutOfRangeDimensions_FailsInvalidInput(int width, int height)
        {
            var corpus = MakeStrictCorpus();
            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, width, height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InvalidInput, result.ErrorCategory);
        }

        [Fact]
        public void Generate_CurrentGridLengthMismatch_FailsInvalidInput()
        {
            var corpus = MakeStrictCorpus();
            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, new ushort[3], seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InvalidInput, result.ErrorCategory);
        }

        [Fact]
        public void Generate_NullCorpus_FailsInvalidInput()
        {
            var result = BuiltInRandomMapGeneratorCore.Generate(null, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InvalidInput, result.ErrorCategory);
        }

        [Fact]
        public void Generate_SparseCorpusWithNoStrictEvidenceButValidEdgeSignatures_SucceedsViaEdgeRelaxed()
        {
            // Two chipsets (MAR 0 and MAR 4) whose every rendered pixel is the same constant
            // color index (5), so every touching-edge signature comparison trivially
            // matches for any pairing including self-pairing. No horizontal/vertical
            // adjacency dictionaries are supplied at all (a "truly sparse" hand-drawn map
            // that never repeats a directional pair), so only EdgeRelaxed evidence exists.
            byte[] objData = new byte[BytesPerTile * 1];
            FillConstantTile(objData, tileIndex: 0, color: 5);

            byte[] configData = new byte[16];
            SetTsa(configData, tsaBase: 0, sub: 0, tileIndex: 0, hFlip: false, vFlip: false); // MAR=0
            SetTsa(configData, tsaBase: 0, sub: 1, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, tsaBase: 0, sub: 2, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, tsaBase: 0, sub: 3, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, tsaBase: 8, sub: 0, tileIndex: 0, hFlip: false, vFlip: false); // MAR=4
            SetTsa(configData, tsaBase: 8, sub: 1, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, tsaBase: 8, sub: 2, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, tsaBase: 8, sub: 3, tileIndex: 0, hFlip: false, vFlip: false);

            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 3, [4] = 3 };
            var empty = new Dictionary<ushort, IReadOnlySet<ushort>>();

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                empty, empty, objData, paletteData: IdentityPalette(), configData, totalCells: Width * Height);

            Assert.False(corpus.HasStrictAdjacencyEvidence);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 42, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(BuiltInRandomMapAdjacencyModel.EdgeRelaxed, result.AdjacencyModel);
            Assert.Equal(Width * Height, result.Mars.Length);
            foreach (ushort mar in result.Mars)
                Assert.Contains(mar, new ushort[] { 0, 4 });
        }

        [Fact]
        public void Generate_RejectsSourceIdenticalCandidate_WhenAlternateSolutionExists()
        {
            var corpus = MakeStrictCorpus();
            var baseline = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 909, CancellationToken.None);
            Assert.True(baseline.Success, baseline.ErrorMessage);

            // Re-run with currentGrid set to the exact grid the same seed would otherwise
            // reproduce. MakeStrictCorpus's fully-connected 0/4/8 ring admits an enormous
            // number of valid completions, so a genuine alternate is always reachable within
            // the same attempt: this proves the source-identity rule *rejects* the identical
            // candidate on every attempt of the ladder (there is no final-attempt exception —
            // Plan v4 §2.6 is absolute) and keeps backtracking for a distinct one, resolving
            // on the very same restart index the identical completion first appeared on.
            var repeat = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, baseline.Mars, seed: 909, CancellationToken.None);

            Assert.True(repeat.Success, repeat.ErrorMessage);
            Assert.Equal(baseline.RestartsUsed, repeat.RestartsUsed);
            Assert.Equal(baseline.AdjacencyModel, repeat.AdjacencyModel);
            Assert.NotEqual(baseline.Mars, repeat.Mars);
        }

        [Fact]
        public void Generate_SourceIdenticalOnlySolution_FailsSearchExhausted_AndLeavesInputUntouched()
        {
            // Plan v4 §2.6 is absolute: no identity/uniform-fill success fallback anywhere in
            // the ladder, including its last attempt. Deliberately construct a corpus with
            // EXACTLY one valid strict-compatible complete grid (all-zero, i.e. a uniform
            // single-MAR layout): "4" has no adjacency evidence at all in either direction, so
            // it dead-ends the instant it acquires a neighbor, and only "0 followed by 0" is
            // ever valid. With currentGrid pre-set to that unique grid, EVERY restart of EVERY
            // attempt (all 4 Strict restarts; EdgeRelaxed never activates because objData is
            // null) must reject the only completion it can ever find as source-identical and
            // keep backtracking until its node budget or search space is exhausted -- there is
            // no final-attempt exception that may accept it, so Generate must report
            // SearchExhausted rather than fabricate a success by returning the source grid (or
            // any other uniform, single-MAR grid) unchanged.
            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            byte[] configData = new byte[16]; // covers MAR 0 (tsaBase 0) and MAR 4 (tsaBase 8).

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            ushort[] currentGrid = new ushort[Width * Height]; // all-zero: the one otherwise-valid grid.
            ushort[] currentGridCopy = (ushort[])currentGrid.Clone();

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, currentGrid, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.SearchExhausted, result.ErrorCategory);
            Assert.Empty(result.Mars);
            // No mutation on failure: the caller-supplied currentGrid must be untouched.
            Assert.Equal(currentGridCopy, currentGrid);
        }

        [Fact]
        public void Generate_MixedRenderableAndNonRenderableCandidates_SucceedsUsingOnlyRenderableOnes()
        {
            // Candidates 0/4/8 are mutually strict-compatible in both directions (like
            // MakeStrictCorpus), but ConfigData is only 16 bytes long: MAR 0 (tsaBase 0) and
            // MAR 4 (tsaBase 8) fit (tsaBase+7 < 16), while MAR 8 (tsaBase 16) does not
            // (16+7 is not < 16). Generation must prefilter MAR 8 out before search/model
            // construction and still succeed using only the two renderable candidates.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 100, [4] = 100, [8] = 100 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>>
            {
                [0] = Set(0, 4, 8),
                [4] = Set(0, 4, 8),
                [8] = Set(0, 4, 8),
            };
            byte[] configData = new byte[16];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1234, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            foreach (ushort mar in result.Mars)
                Assert.Contains(mar, new ushort[] { 0, 4 });
            Assert.DoesNotContain((ushort)8, result.Mars);
        }

        [Fact]
        public void Generate_AllCandidatesNonRenderable_FailsInsufficientSourceDataWithoutSearching()
        {
            // Two candidates with full strict adjacency evidence (so, absent the prefilter,
            // a search would be attempted), but ConfigData is far too short for either
            // MAR's TSA block: neither MAR 0 (tsaBase 0) nor MAR 4 (tsaBase 8) satisfies
            // tsaBase+7 < ConfigData.Length for a 4-byte buffer. This must fail fast with
            // InsufficientSourceData (an honest, specific reason) rather than burning the
            // node budget on candidates that could never pass the renderability quality
            // gate, and rather than a misleading SearchExhausted.
            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0, 4), [4] = Set(0, 4) };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0, 4), [4] = Set(0, 4) };
            byte[] configData = new byte[4];

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.InsufficientSourceData, result.ErrorCategory);
            Assert.Empty(result.Mars);
        }

        [Fact]
        public void ComputeNodeBudget_ClampsBetween10000And1000000()
        {
            Assert.Equal(10_000, BuiltInRandomMapGeneratorCore.ComputeNodeBudget(15, 10));
            Assert.Equal(15 * 63 * 64, BuiltInRandomMapGeneratorCore.ComputeNodeBudget(15, 63));
            Assert.Equal(1_000_000, BuiltInRandomMapGeneratorCore.ComputeNodeBudget(500, 500));
        }

        static void FillConstantTile(byte[] objData, int tileIndex, int color)
        {
            int baseOff = tileIndex * BytesPerTile;
            byte packed = (byte)((color & 0xF) | ((color & 0xF) << 4));
            for (int i = 0; i < BytesPerTile; i++)
                objData[baseOff + i] = packed;
        }

        static byte[] IdentityPalette()
        {
            byte[] paletteData = new byte[2 * 16 * 16];
            for (int palette = 0; palette < 16; palette++)
            {
                for (int color = 0; color < 16; color++)
                {
                    int offset = (palette * 16 + color) * 2;
                    paletteData[offset] = (byte)color;
                }
            }
            return paletteData;
        }

        static void SetTsa(byte[] configData, int tsaBase, int sub, int tileIndex, bool hFlip, bool vFlip)
        {
            ushort tsa = (ushort)(tileIndex & 0x3FF);
            if (hFlip) tsa |= 0x400;
            if (vFlip) tsa |= 0x800;
            int off = tsaBase + sub * 2;
            configData[off] = (byte)(tsa & 0xFF);
            configData[off + 1] = (byte)((tsa >> 8) & 0xFF);
        }

        sealed class CancellingReadOnlyDictionary : IReadOnlyDictionary<ushort, long>
        {
            readonly IReadOnlyDictionary<ushort, long> inner;
            readonly CancellationTokenSource cancellationTokenSource;
            readonly int cancelAfterTryGetValueCalls;

            public CancellingReadOnlyDictionary(
                IReadOnlyDictionary<ushort, long> inner,
                CancellationTokenSource cancellationTokenSource,
                int cancelAfterTryGetValueCalls)
            {
                this.inner = inner;
                this.cancellationTokenSource = cancellationTokenSource;
                this.cancelAfterTryGetValueCalls = cancelAfterTryGetValueCalls;
            }

            public int TryGetValueCalls { get; private set; }
            public long this[ushort key] => inner[key];
            public IEnumerable<ushort> Keys => inner.Keys;
            public IEnumerable<long> Values => inner.Values;
            public int Count => inner.Count;

            public bool ContainsKey(ushort key) => inner.ContainsKey(key);

            public bool TryGetValue(ushort key, out long value)
            {
                TryGetValueCalls++;
                bool found = inner.TryGetValue(key, out value);
                if (TryGetValueCalls == cancelAfterTryGetValueCalls)
                    cancellationTokenSource.Cancel();
                return found;
            }

            public IEnumerator<KeyValuePair<ushort, long>> GetEnumerator() => inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class CountingReadOnlyDictionary : IReadOnlyDictionary<ushort, long>
        {
            readonly IReadOnlyDictionary<ushort, long> inner;

            public CountingReadOnlyDictionary(IReadOnlyDictionary<ushort, long> inner)
            {
                this.inner = inner;
            }

            public int TryGetValueCalls { get; private set; }
            public long this[ushort key] => inner[key];
            public IEnumerable<ushort> Keys => inner.Keys;
            public IEnumerable<long> Values => inner.Values;
            public int Count => inner.Count;
            public bool ContainsKey(ushort key) => inner.ContainsKey(key);

            public bool TryGetValue(ushort key, out long value)
            {
                TryGetValueCalls++;
                return inner.TryGetValue(key, out value);
            }

            public IEnumerator<KeyValuePair<ushort, long>> GetEnumerator() => inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class CancellingAdjacencyDictionary : IReadOnlyDictionary<ushort, IReadOnlySet<ushort>>
        {
            readonly IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> inner;
            readonly CancellationTokenSource cancellationTokenSource;
            readonly int cancelAfterTryGetValueCalls;

            public CancellingAdjacencyDictionary(
                IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> inner,
                CancellationTokenSource cancellationTokenSource,
                int cancelAfterTryGetValueCalls)
            {
                this.inner = inner;
                this.cancellationTokenSource = cancellationTokenSource;
                this.cancelAfterTryGetValueCalls = cancelAfterTryGetValueCalls;
            }

            public int TryGetValueCalls { get; private set; }
            public IReadOnlySet<ushort> this[ushort key] => inner[key];
            public IEnumerable<ushort> Keys => inner.Keys;
            public IEnumerable<IReadOnlySet<ushort>> Values => inner.Values;
            public int Count => inner.Count;
            public bool ContainsKey(ushort key) => inner.ContainsKey(key);

            public bool TryGetValue(ushort key, out IReadOnlySet<ushort> value)
            {
                TryGetValueCalls++;
                bool found = inner.TryGetValue(key, out value!);
                if (TryGetValueCalls == cancelAfterTryGetValueCalls)
                    cancellationTokenSource.Cancel();
                return found;
            }

            public IEnumerator<KeyValuePair<ushort, IReadOnlySet<ushort>>> GetEnumerator() => inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class CancellingReadOnlySet : IReadOnlySet<ushort>
        {
            readonly IReadOnlySet<ushort> inner;
            readonly CancellationTokenSource cancellationTokenSource;
            readonly int cancelAfterContainsCalls;

            public CancellingReadOnlySet(
                IEnumerable<ushort> values,
                CancellationTokenSource cancellationTokenSource,
                int cancelAfterContainsCalls)
            {
                inner = new HashSet<ushort>(values);
                this.cancellationTokenSource = cancellationTokenSource;
                this.cancelAfterContainsCalls = cancelAfterContainsCalls;
            }

            public int ContainsCalls { get; private set; }
            public int Count => inner.Count;

            public bool Contains(ushort item)
            {
                ContainsCalls++;
                bool found = inner.Contains(item);
                if (ContainsCalls == cancelAfterContainsCalls)
                    cancellationTokenSource.Cancel();
                return found;
            }

            public bool IsProperSubsetOf(IEnumerable<ushort> other) => inner.IsProperSubsetOf(other);
            public bool IsProperSupersetOf(IEnumerable<ushort> other) => inner.IsProperSupersetOf(other);
            public bool IsSubsetOf(IEnumerable<ushort> other) => inner.IsSubsetOf(other);
            public bool IsSupersetOf(IEnumerable<ushort> other) => inner.IsSupersetOf(other);
            public bool Overlaps(IEnumerable<ushort> other) => inner.Overlaps(other);
            public bool SetEquals(IEnumerable<ushort> other) => inner.SetEquals(other);
            public IEnumerator<ushort> GetEnumerator() => inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
