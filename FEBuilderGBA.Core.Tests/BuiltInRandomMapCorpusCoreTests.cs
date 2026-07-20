// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BuiltInRandomMapCorpusCoreTests
    {
        static byte[] MakeBytes(int length, int seed)
        {
            byte[] result = new byte[length];
            new System.Random(seed).NextBytes(result);
            return result;
        }

        static byte[] Palette(int seed) => MakeBytes(2 * 16 * 16, seed);

        [Fact]
        public void TryBuildCorpus_MergesOnlyFingerprintMatchingMapsInAscendingOrder()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, 1);
            byte[] pal = Palette(2);
            byte[] cfg = MakeBytes(24, 3);

            ushort[] marsA = new ushort[15 * 10];
            for (int i = 0; i < marsA.Length; i++) marsA[i] = (ushort)((i % 3) * 4);
            ushort[] marsB = new ushort[15 * 10];
            for (int i = 0; i < marsB.Length; i++) marsB[i] = (ushort)(((i + 1) % 3) * 4);

            // Two maps sharing tilesetSlot 1 (fingerprint-identical); a third uses a
            // different tileset slot (different fingerprint) and must be excluded.
            uint addr0 = BuiltInRandomMapTestFixture.WriteMap(rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, marsA);
            BuiltInRandomMapTestFixture.WriteMap(rom, 1, tilesetSlot: 1, obj, pal, cfg, 15, 10, marsB);
            BuiltInRandomMapTestFixture.WriteMap(rom, 2, tilesetSlot: 2, MakeBytes(64, 9), Palette(9), MakeBytes(24, 9), 15, 10, marsA);

            bool ok = BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, addr0, out BuiltInRandomMapTilesetCorpus corpus, out string error);

            Assert.True(ok, error);
            Assert.Equal(new uint[] { 0, 1 }, corpus.ContributingMapIds); // MakeMapIDList tags are the 0-based table row index
            Assert.Equal(2 * 15 * 10, corpus.TotalCells);
            Assert.True(corpus.HasStrictAdjacencyEvidence);
        }

        [Fact]
        public void TryBuildCorpus_DamagedMapEntryDoesNotFailWholeScan()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, 1);
            byte[] pal = Palette(2);
            byte[] cfg = MakeBytes(24, 3);
            ushort[] mars = new ushort[15 * 10];
            for (int i = 0; i < mars.Length; i++) mars[i] = (ushort)((i % 3) * 4);

            uint addr0 = BuiltInRandomMapTestFixture.WriteMap(rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);

            // Map index 1: a damaged entry whose obj_plist points at an unpopulated slot
            // (PLIST 250 was never written by WriteMap, so it resolves to U.NOT_FOUND).
            uint dataSize = rom.RomInfo.map_setting_datasize;
            uint damagedAddr = 0x00600000u + 1u * dataSize;
            BuiltInRandomMapTestFixture.WriteU32(rom, damagedAddr + 0, 0x08000001);
            BuiltInRandomMapTestFixture.WriteU16(rom, damagedAddr + 4, 250);
            rom.Data[damagedAddr + 6] = 250;
            rom.Data[damagedAddr + 7] = 250;
            rom.Data[damagedAddr + 8] = 250;

            bool ok = BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, addr0, out BuiltInRandomMapTilesetCorpus corpus, out string error);

            Assert.True(ok, error);
            Assert.Single(corpus.ContributingMapIds);
            Assert.Equal(0u, corpus.ContributingMapIds[0]);
        }

        [Fact]
        public void TryBuildCorpus_CrossRomIsolation_NoLeakageBetweenSeparateRomInstances()
        {
            ROM romA = BuiltInRandomMapTestFixture.CreateRom();
            ushort[] marsA = new ushort[15 * 10];
            for (int i = 0; i < marsA.Length; i++) marsA[i] = (ushort)((i % 3) * 4);
            uint addrA = BuiltInRandomMapTestFixture.WriteMap(romA, 0, tilesetSlot: 1, MakeBytes(64, 1), Palette(2), MakeBytes(24, 3), 15, 10, marsA);

            ROM romB = BuiltInRandomMapTestFixture.CreateRom();
            ushort[] marsB = new ushort[15 * 10];
            for (int i = 0; i < marsB.Length; i++) marsB[i] = (ushort)(((i + 2) % 3) * 4);
            // Same tilesetSlot number (1) reused in a fresh ROM instance, but with different
            // OBJ/PAL/CFG bytes -- a static cross-ROM cache would incorrectly reuse romA's blobs.
            uint addrB = BuiltInRandomMapTestFixture.WriteMap(romB, 0, tilesetSlot: 1, MakeBytes(64, 77), Palette(78), MakeBytes(24, 79), 15, 10, marsB);
            BuiltInRandomMapTestFixture.WriteMap(romB, 1, tilesetSlot: 1, MakeBytes(64, 77), Palette(78), MakeBytes(24, 79), 15, 10, marsB);

            Assert.True(BuiltInRandomMapCorpusCore.TryBuildCorpus(romA, addrA, out BuiltInRandomMapTilesetCorpus corpusA, out string errorA), errorA);
            Assert.True(BuiltInRandomMapCorpusCore.TryBuildCorpus(romB, addrB, out BuiltInRandomMapTilesetCorpus corpusB, out string errorB), errorB);

            Assert.NotEqual(corpusA.Fingerprint, corpusB.Fingerprint);
            Assert.Single(corpusA.ContributingMapIds);
            Assert.Equal(2, corpusB.ContributingMapIds.Count);
        }

        [Fact]
        public void TryBuildCorpus_FrequencyAndAdjacencyMatchHandBuiltMap()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            // A tiny, fully-known 15x10 map: value 0 everywhere except a single cell (x=1,y=1)
            // set to value 4, so adjacency/frequency counts are hand-verifiable.
            ushort[] mars = new ushort[15 * 10];
            mars[1 * 15 + 1] = 4;

            uint addr = BuiltInRandomMapTestFixture.WriteMap(rom, 0, tilesetSlot: 1, MakeBytes(64, 1), Palette(2), MakeBytes(24, 3), 15, 10, mars);

            bool ok = BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, addr, out BuiltInRandomMapTilesetCorpus corpus, out string error);
            Assert.True(ok, error);

            Assert.Equal(150 - 1, corpus.Frequency[0]);
            Assert.Equal(1, corpus.Frequency[4]);
            // Cell (1,1) is not on the border (width=15,height=10 -> border is x==0||y==0||x==14||y==9).
            Assert.False(corpus.BorderFrequency.ContainsKey(4));

            // West neighbor of (1,1) is (0,1)=0; so horizontal[0] must contain 4.
            Assert.Contains((ushort)4, corpus.HorizontalAdjacency[0]);
            // North neighbor of (1,1) is (1,0)=0; so vertical[0] must contain 4.
            Assert.Contains((ushort)4, corpus.VerticalAdjacency[0]);
            // East neighbor of (1,1) is (2,1)=0; so horizontal[4] must contain 0.
            Assert.Contains((ushort)0, corpus.HorizontalAdjacency[4]);
            Assert.Contains((ushort)0, corpus.VerticalAdjacency[4]);
        }

        [Fact]
        public void TryBuildCorpus_UnresolvableCurrentMap_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            bool ok = BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, 0x00600000, out BuiltInRandomMapTilesetCorpus corpus, out string error);
            Assert.False(ok);
            Assert.Null(corpus);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void TryBuildCorpus_DoesNotMutateRomData()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            ushort[] mars = new ushort[15 * 10];
            for (int i = 0; i < mars.Length; i++) mars[i] = (ushort)((i % 3) * 4);
            uint addr = BuiltInRandomMapTestFixture.WriteMap(rom, 0, tilesetSlot: 1, MakeBytes(64, 1), Palette(2), MakeBytes(24, 3), 15, 10, mars);

            byte[] before = (byte[])rom.Data.Clone();
            Assert.True(BuiltInRandomMapCorpusCore.TryBuildCorpus(rom, addr, out _, out string error), error);
            Assert.Equal(before, rom.Data);
        }
    }
}
