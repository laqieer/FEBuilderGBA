// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
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
            // number of valid completions, so the non-final-restart source-difference rule
            // (Generate rejects a candidate identical to currentGrid while an alternate is
            // still reachable) is expected to succeed on the very first restart with a grid
            // that differs from the deliberately-patterned currentGrid.
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEqual(currentGrid, result.Mars);
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
        public void Generate_OnlyOneStrictCompletionLacksDiversity_FailsSearchExhausted()
        {
            // Candidates 0/4/8 (>=3, so the diversity gate applies). Only "0 followed by 0"
            // is ever a valid directional pair in either direction, so the unique
            // strict-compatible complete grid is all-zero, which the diversity gate
            // (>=3 distinct values, since Candidates.Count>=3) always rejects. A valid,
            // non-null ConfigData is supplied so IsMarRenderable succeeds for every
            // candidate and the failure is attributable to the diversity gate rather than
            // an unrelated renderability failure; ObjData stays null so EdgeRelaxed is
            // unavailable and only the Strict ladder runs.
            var candidates = new List<ushort> { 0, 4, 8 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5, [8] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            byte[] configData = new byte[64]; // tsaBase for MAR=8 is 16; well within range.

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(BuiltInRandomMapErrorCategory.SearchExhausted, result.ErrorCategory);
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
                empty, empty, objData, paletteData: null, configData, totalCells: Width * Height);

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
            // number of valid completions, so a genuine alternate is always reachable
            // within the same attempt: this proves the source-identity rule *rejects* the
            // identical candidate (rather than silently accepting it) and keeps
            // backtracking for a distinct one, resolving on the very same restart index the
            // identical completion first appeared on -- i.e. rejection-with-alternate is
            // enforced on an ordinary, non-final restart, not deferred to the ladder's end.
            var repeat = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, baseline.Mars, seed: 909, CancellationToken.None);

            Assert.True(repeat.Success, repeat.ErrorMessage);
            Assert.Equal(baseline.RestartsUsed, repeat.RestartsUsed);
            Assert.Equal(baseline.AdjacencyModel, repeat.AdjacencyModel);
            Assert.NotEqual(baseline.Mars, repeat.Mars);
        }

        [Fact]
        public void Generate_FinalRestartAcceptsSourceIdentity_WhenNoAlternateSolutionExists()
        {
            // Deliberately construct a corpus with EXACTLY one valid strict-compatible
            // complete grid (all-zero): "4" has no adjacency evidence at all in either
            // direction, so it dead-ends the instant it acquires a neighbor, and only
            // "0 followed by 0" is ever valid. With currentGrid pre-set to that unique
            // grid, every non-final restart must fully exhaust its search (the only
            // completion is identical and gets rejected, and no alternate can ever exist)
            // -- proving the identity-rejection rule is enforced even at the cost of a
            // full Exhausted restart -- while the ladder's *final* restart is deliberately
            // allowed to accept that same identical completion, which is the only way
            // Generate can ever succeed for this corpus. This is intentional, deterministic
            // Plan v4 behavior, not an accidental fallback.
            var candidates = new List<ushort> { 0, 4 };
            var freq = new Dictionary<ushort, long> { [0] = 5, [4] = 5 };
            var horizontal = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            var vertical = new Dictionary<ushort, IReadOnlySet<ushort>> { [0] = Set(0) };
            byte[] configData = new byte[16]; // covers MAR 0 (tsaBase 0) and MAR 4 (tsaBase 8).

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: configData, totalCells: Width * Height);

            ushort[] currentGrid = new ushort[Width * Height]; // all-zero: the one valid grid.

            var result = BuiltInRandomMapGeneratorCore.Generate(corpus, Width, Height, currentGrid, seed: 1, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(BuiltInRandomMapAdjacencyModel.Strict, result.AdjacencyModel);
            Assert.Equal(BuiltInRandomMapGeneratorCore.RestartCount, result.RestartsUsed); // only the ladder's last attempt could ever succeed.
            Assert.Equal(currentGrid, result.Mars); // the final attempt intentionally accepted the identical grid.
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

        static void SetTsa(byte[] configData, int tsaBase, int sub, int tileIndex, bool hFlip, bool vFlip)
        {
            ushort tsa = (ushort)(tileIndex & 0x3FF);
            if (hFlip) tsa |= 0x400;
            if (vFlip) tsa |= 0x800;
            int off = tsaBase + sub * 2;
            configData[off] = (byte)(tsa & 0xFF);
            configData[off + 1] = (byte)((tsa >> 8) & 0xFF);
        }
    }
}
