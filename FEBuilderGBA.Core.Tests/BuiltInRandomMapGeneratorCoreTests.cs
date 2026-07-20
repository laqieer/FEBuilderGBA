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
            // set for both directions: no cell wider than 1 can ever be completed.
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

            var corpus = BuiltInRandomMapTilesetCorpus.CreateForTesting(
                TilesetFingerprint.Empty, new List<uint> { 0 }, candidates, freq, freq,
                horizontal, vertical, objData: null, paletteData: null, configData: null, totalCells: Width * Height);

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
