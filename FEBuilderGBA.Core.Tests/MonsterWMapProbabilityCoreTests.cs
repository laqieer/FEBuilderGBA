using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="MonsterWMapProbabilityCore"/> — the three World Map Monster
    /// editing surfaces (#1464) that the Avalonia editor previously dropped: stage spread,
    /// per-base probabilities, and skirmish events. FE8-only.
    ///
    /// The Avalonia <c>MonsterWMapProbabilityViewer</c> previously ported only the base-point
    /// list. These tests round-trip (read→edit→write→read) each restored surface against a
    /// synthetic FE8U ROM whose pointer slots point at planted tables.
    /// </summary>
    [Collection("SharedState")]
    public class MonsterWMapProbabilityCoreTests
    {
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeFe7uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe7u.gba", new byte[0x1100000], "AE7E01");
            return rom;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // ================================================================
        // FE8-only gate
        // ================================================================

        [Fact]
        public void IsSupported_NullRom_False()
        {
            Assert.False(MonsterWMapProbabilityCore.IsSupported(null));
        }

        [Fact]
        public void IsSupported_FE8U_True()
        {
            var rom = MakeFe8uRom();
            Assert.True(MonsterWMapProbabilityCore.IsSupported(rom));
        }

        [Fact]
        public void IsSupported_FE7U_False()
        {
            var rom = MakeFe7uRom();
            // FE7 leaves the monster wmap stage/probability/skirmish slots at 0.
            Assert.False(MonsterWMapProbabilityCore.IsSupported(rom));
        }

        // ================================================================
        // Surface 2 — stage spread (stride 1)
        // ================================================================

        [Fact]
        public void StageSpread_LoadsBothRoutes_AndRoundTrips()
        {
            var rom = MakeFe8uRom();
            uint eirikaBase = 0x00900000u;
            uint ephraimBase = 0x00901000u;

            // Repoint the stage tables to planted bases.
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_1_pointer, eirikaBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_2_pointer, ephraimBase | 0x08000000u);

            // Plant distinct map ids so the two routes are distinguishable.
            for (int i = 0; i < MonsterWMapProbabilityCore.StageCount; i++)
            {
                rom.Data[eirikaBase + i] = (byte)(0x10 + i);
                rom.Data[ephraimBase + i] = (byte)(0x40 + i);
            }

            var eirika = MonsterWMapProbabilityCore.LoadStageList(rom, isEphraim: false);
            var ephraim = MonsterWMapProbabilityCore.LoadStageList(rom, isEphraim: true);
            Assert.Equal(MonsterWMapProbabilityCore.StageCount, eirika.Count);
            Assert.Equal(MonsterWMapProbabilityCore.StageCount, ephraim.Count);

            // Read row 3 on the Eirika route.
            uint addr = eirika[3].addr;
            Assert.Equal(0x13u, MonsterWMapProbabilityCore.ReadStageMapId(rom, addr));

            // Edit → write → read back.
            MonsterWMapProbabilityCore.WriteStageMapId(rom, addr, 0x7F);
            Assert.Equal(0x7Fu, MonsterWMapProbabilityCore.ReadStageMapId(rom, addr));
            Assert.Equal((byte)0x7F, rom.Data[addr]);

            // The Ephraim route is independent.
            Assert.Equal(0x43u, MonsterWMapProbabilityCore.ReadStageMapId(rom, ephraim[3].addr));
        }

        [Fact]
        public void GetStagePointer_SelectsByRoute()
        {
            var rom = MakeFe8uRom();
            Assert.Equal(rom.RomInfo.monster_wmap_stage_1_pointer, MonsterWMapProbabilityCore.GetStagePointer(rom, false));
            Assert.Equal(rom.RomInfo.monster_wmap_stage_2_pointer, MonsterWMapProbabilityCore.GetStagePointer(rom, true));
        }

        // ================================================================
        // Surface 3 — per-base probabilities (stride 9)
        // ================================================================

        [Fact]
        public void Probability_LoadsBothRoutes_AndRoundTripsNineByteRow()
        {
            var rom = MakeFe8uRom();
            uint eirikaBase = 0x00910000u;
            uint ephraimBase = 0x00911000u;

            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_1_pointer, eirikaBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_2_pointer, ephraimBase | 0x08000000u);

            var eirika = MonsterWMapProbabilityCore.LoadProbabilityList(rom, isEphraim: false);
            var ephraim = MonsterWMapProbabilityCore.LoadProbabilityList(rom, isEphraim: true);
            Assert.Equal(MonsterWMapProbabilityCore.ProbabilityCount, eirika.Count);
            Assert.Equal(MonsterWMapProbabilityCore.ProbabilityCount, ephraim.Count);

            // Each row is stride 9.
            Assert.Equal(eirika[0].addr + MonsterWMapProbabilityCore.ProbabilityWidth, eirika[1].addr);

            uint addr = eirika[2].addr;
            byte[] row = { 5, 10, 15, 20, 25, 30, 0, 0, 5 }; // sum = 110
            MonsterWMapProbabilityCore.WriteProbabilityRow(rom, addr, row);

            byte[] back = MonsterWMapProbabilityCore.ReadProbabilityRow(rom, addr);
            Assert.Equal(row, back);
            Assert.Equal(110u, MonsterWMapProbabilityCore.Sum(back));

            // Next row untouched (no stride overrun).
            byte[] next = MonsterWMapProbabilityCore.ReadProbabilityRow(rom, eirika[3].addr);
            Assert.Equal(0u, MonsterWMapProbabilityCore.Sum(next));
        }

        [Fact]
        public void Sum_EmptyAndNull_Zero()
        {
            Assert.Equal(0u, MonsterWMapProbabilityCore.Sum(null));
            Assert.Equal(0u, MonsterWMapProbabilityCore.Sum(new byte[0]));
            Assert.Equal(255u + 1u, MonsterWMapProbabilityCore.Sum(new byte[] { 255, 1 }));
        }

        [Fact]
        public void WriteProbabilityRow_ShortRow_ZeroPads()
        {
            var rom = MakeFe8uRom();
            uint addr = 0x00920000u;
            // Pre-fill with 0xFF so we can prove the unset bytes are zeroed.
            for (int k = 0; k < MonsterWMapProbabilityCore.ProbabilityWidth; k++) rom.Data[addr + k] = 0xFF;

            MonsterWMapProbabilityCore.WriteProbabilityRow(rom, addr, new byte[] { 1, 2, 3 });
            byte[] back = MonsterWMapProbabilityCore.ReadProbabilityRow(rom, addr);
            Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0, 0 }, back);
        }

        // ================================================================
        // Base point labels (uses worldmap_point name resolution)
        // ================================================================

        [Fact]
        public void GetBasePointLabels_AlwaysNineEntries()
        {
            var rom = MakeFe8uRom();
            uint baseListBase = 0x00930000u;
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_base_point_pointer, baseListBase | 0x08000000u);
            for (int i = 0; i < MonsterWMapProbabilityCore.BasePointCount; i++)
                rom.Data[baseListBase + i] = (byte)i;

            var labels = MonsterWMapProbabilityCore.GetBasePointLabels(rom);
            Assert.Equal(MonsterWMapProbabilityCore.BasePointCount, labels.Count);
            // Each label is at least the hex index prefix; never throws.
            for (uint i = 0; i < labels.Count; i++)
                Assert.StartsWith(U.ToHexString(i), labels[(int)i]);
        }

        // ================================================================
        // Surface 4 — skirmish events
        // ================================================================

        [Fact]
        public void SkirmishEvents_RoundTrip()
        {
            var rom = MakeFe8uRom();

            uint start = 0x00A12345u;
            uint end = 0x00B6789Au;
            MonsterWMapProbabilityCore.WriteSkirmishEvents(rom, start, end);

            Assert.Equal(start, MonsterWMapProbabilityCore.ReadSkirmishStartEvent(rom));
            Assert.Equal(end, MonsterWMapProbabilityCore.ReadSkirmishEndEvent(rom));

            // The pointers are stored at the ROMINFO slots in GBA form.
            uint startSlot = rom.RomInfo.worldmap_skirmish_startevent_pointer;
            Assert.Equal(start | 0x08000000u, rom.u32(startSlot));
        }

        [Fact]
        public void SkirmishEvents_NullRom_NoThrow()
        {
            Assert.Equal(0u, MonsterWMapProbabilityCore.ReadSkirmishStartEvent(null));
            Assert.Equal(0u, MonsterWMapProbabilityCore.ReadSkirmishEndEvent(null));
            MonsterWMapProbabilityCore.WriteSkirmishEvents(null, 1, 2); // no throw
        }

        // ================================================================
        // Safety guards
        // ================================================================

        [Fact]
        public void LoadStageList_NullRom_Empty()
        {
            Assert.Empty(MonsterWMapProbabilityCore.LoadStageList(null, false));
            Assert.Empty(MonsterWMapProbabilityCore.LoadProbabilityList(null, false));
        }

        [Fact]
        public void ReadStageMapId_OutOfBounds_Zero()
        {
            var rom = MakeFe8uRom();
            Assert.Equal(0u, MonsterWMapProbabilityCore.ReadStageMapId(rom, (uint)rom.Data.Length + 10));
            Assert.Equal(0u, MonsterWMapProbabilityCore.ReadStageMapId(rom, 0));
        }

        // Overflow-safe bounds: an addr near uint.MaxValue must not wrap the
        // length check and reach a throwing rom.u8/write_u8 (#1464 Copilot round 2).
        [Fact]
        public void ReadProbabilityRow_NearMaxAddr_NoThrow_ReturnsZeros()
        {
            var rom = MakeFe8uRom();
            byte[] row = MonsterWMapProbabilityCore.ReadProbabilityRow(rom, 0xFFFFFFFFu);
            Assert.Equal(new byte[MonsterWMapProbabilityCore.ProbabilityWidth], row);
        }

        [Fact]
        public void WriteProbabilityRow_NearMaxAddr_NoThrow_NoMutation()
        {
            var rom = MakeFe8uRom();
            // Must not throw and must not write anywhere in-bounds.
            MonsterWMapProbabilityCore.WriteProbabilityRow(rom, 0xFFFFFFFFu, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            // A near-EOF addr whose row would straddle the end is also refused.
            uint nearEof = (uint)rom.Data.Length - 3;
            MonsterWMapProbabilityCore.WriteProbabilityRow(rom, nearEof, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Assert.Equal((uint)0, rom.u8(nearEof));
        }
    }
}
