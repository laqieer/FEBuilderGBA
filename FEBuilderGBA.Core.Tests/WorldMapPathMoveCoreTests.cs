// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for WorldMapPathMoveCore (#1598) — the FE8 World Map Path MOVEMENT
// editor's data resolve/decode/write. Proves the corruption fix:
//   * the movement base is p32(record+8), NOT the 12-byte path-record table base
//   * ElapsedTime is a u16 (a value > 255 round-trips as 16-bit)
//   * the 0xFFFFFFFF terminator is NOT emitted as a node
//   * WriteNode targets ONLY a validated movement node — writing to the record
//     base or the terminator slot is rejected with ZERO mutation
//
// Reuses the synthetic-ROM harness shape from WorldMapPathCoreTests
// (rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01") + SetPtr).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathMoveCoreTests
    {
        // Distinct offsets clear of the 0x0..0x200 danger zone and each other.
        const uint ROAD_TABLE_OFFSET = 0x001000; // 12-byte path records
        const uint MOVE_OFFSET       = 0x004000; // 8-byte movement nodes for record 0
        const uint POINT_TABLE_OFFSET = 0x003000; // world-map point entries (32 B)

        // =================================================================
        // (a) MakePathList
        // =================================================================

        [Fact]
        public void MakePathList_ReturnsAtLeastOneEntry()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 1);
                var list = WorldMapPathMoveCore.MakePathList(rom);
                Assert.True(list.Count >= 1);
                Assert.Equal(0u, list[0].tag);
            });
        }

        // =================================================================
        // (b) ResolveMovementBase
        // =================================================================

        [Fact]
        public void ResolveMovementBase_ReturnsPlantedMovementOffset()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 2);
                bool ok = WorldMapPathMoveCore.ResolveMovementBase(rom, 0, out uint b);
                Assert.True(ok);
                Assert.Equal(MOVE_OFFSET, b);
            });
        }

        [Fact]
        public void ResolveMovementBase_NullMovementPointer_ReturnsFalse()
        {
            WithRom((rom) =>
            {
                // record0 +0 path pointer set so MakePathList includes it, but +8 == 0.
                SetPtr(rom, rom.RomInfo.worldmap_road_pointer, ROAD_TABLE_OFFSET);
                uint entry = ROAD_TABLE_OFFSET;
                SetPtr(rom, entry + 0, 0x002000); // path-data pointer
                // entry+8 left 0.
                bool ok = WorldMapPathMoveCore.ResolveMovementBase(rom, 0, out uint b);
                Assert.False(ok);
                Assert.Equal(0u, b);
            });
        }

        // =================================================================
        // (c) LoadMovementList
        // =================================================================

        [Fact]
        public void LoadMovementList_DecodesNodes_U16ElapsedTime_NoTerminatorRow()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 0);
                // 3 nodes, ElapsedTime u16 0x0547 (>255 proves 16-bit), then terminator.
                WriteNode(rom, MOVE_OFFSET + 0 * 8, 0x0547, 12, 34);
                WriteNode(rom, MOVE_OFFSET + 1 * 8, 0x0800, 56, 78);
                WriteNode(rom, MOVE_OFFSET + 2 * 8, 0x0A8F, 90, 99);
                rom.write_u32(MOVE_OFFSET + 3 * 8, 0xFFFFFFFF);

                var list = WorldMapPathMoveCore.LoadMovementList(rom, MOVE_OFFSET);
                Assert.Equal(3, list.Count); // terminator NOT emitted

                Assert.Equal(MOVE_OFFSET + 0 * 8, list[0].addr);
                Assert.Equal(0x0547u, WorldMapPathMoveCore.ReadElapsedTime(rom, list[0].addr)); // u16 preserved
                Assert.Equal(12u, WorldMapPathMoveCore.ReadX(rom, list[0].addr));
                Assert.Equal(34u, WorldMapPathMoveCore.ReadY(rom, list[0].addr));
                Assert.Equal(0x0A8Fu, WorldMapPathMoveCore.ReadElapsedTime(rom, list[2].addr));
            });
        }

        // =================================================================
        // (d) WriteNode persists
        // =================================================================

        [Fact]
        public void WriteNode_OnRealNode_Persists()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 0);
                WriteNode(rom, MOVE_OFFSET + 0 * 8, 1, 2, 3);
                rom.write_u32(MOVE_OFFSET + 1 * 8, 0xFFFFFFFF);

                string err = WorldMapPathMoveCore.WriteNode(rom, MOVE_OFFSET + 0 * 8, 0x0123, 0x0044, 0x0055);
                Assert.Equal("", err);

                Assert.Equal(0x0123u, rom.u16(MOVE_OFFSET + 0));
                Assert.Equal(0x0044u, rom.u16(MOVE_OFFSET + 4));
                Assert.Equal(0x0055u, rom.u16(MOVE_OFFSET + 6));
            });
        }

        // =================================================================
        // (e) CORRUPTION GUARD — record bytes untouched after a node write
        // =================================================================

        [Fact]
        public void WriteNode_LeavesRecordTableByteIdentical()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 0);
                WriteNode(rom, MOVE_OFFSET + 0 * 8, 1, 2, 3);
                rom.write_u32(MOVE_OFFSET + 1 * 8, 0xFFFFFFFF);

                // Capture the 12-byte record (+0 ptr, +4/+5 ids, +8 move ptr).
                byte[] recordBefore = rom.getBinaryData(ROAD_TABLE_OFFSET, 12);

                string err = WorldMapPathMoveCore.WriteNode(rom, MOVE_OFFSET + 0 * 8, 0x0111, 0x0022, 0x0033);
                Assert.Equal("", err);

                byte[] recordAfter = rom.getBinaryData(ROAD_TABLE_OFFSET, 12);
                Assert.Equal(recordBefore, recordAfter);
            });
        }

        // =================================================================
        // (f) non-FE8 — empty lists, write rejected, zero mutation
        // =================================================================

        [Fact]
        public void NonFE8_AllOperationsRejected()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
            {
                Assert.Empty(WorldMapPathMoveCore.MakePathList(rom));
                Assert.Empty(WorldMapPathMoveCore.LoadMovementList(rom, MOVE_OFFSET));

                byte[] before = (byte[])rom.Data.Clone();
                string err = WorldMapPathMoveCore.WriteNode(rom, MOVE_OFFSET, 1, 2, 3);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        // =================================================================
        // (g) WriteNode on a non-node address (the record table base) — rejected
        // =================================================================

        [Fact]
        public void WriteNode_OnRecordTableBase_RejectedZeroMutation()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 0);
                WriteNode(rom, MOVE_OFFSET + 0 * 8, 1, 2, 3);
                rom.write_u32(MOVE_OFFSET + 1 * 8, 0xFFFFFFFF);

                byte[] before = (byte[])rom.Data.Clone();
                // The record-table base is NOT a movement node.
                string err = WorldMapPathMoveCore.WriteNode(rom, ROAD_TABLE_OFFSET, 0x0111, 0x0022, 0x0033);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        // =================================================================
        // (h) TERMINATOR GUARD — writing the terminator slot is rejected
        // =================================================================

        [Fact]
        public void WriteNode_OnTerminatorSlot_RejectedTerminatorByteIdentical()
        {
            WithRom((rom) =>
            {
                PlantRecord(rom, 0, pathDataOffset: 0x002000, moveOffset: MOVE_OFFSET, nodeCount: 0);
                const int nodeCount = 2;
                WriteNode(rom, MOVE_OFFSET + 0 * 8, 10, 1, 2);
                WriteNode(rom, MOVE_OFFSET + 1 * 8, 20, 3, 4);
                uint termAddr = MOVE_OFFSET + (uint)(nodeCount * 8);
                rom.write_u32(termAddr, 0xFFFFFFFF);

                byte[] before = (byte[])rom.Data.Clone();
                string err = WorldMapPathMoveCore.WriteNode(rom, termAddr, 0x0111, 0x0022, 0x0033);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
                // Terminator still intact.
                Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));
            });
        }

        [Fact]
        public void WriteNode_NullRom_ReturnsError()
        {
            string err = WorldMapPathMoveCore.WriteNode(null, MOVE_OFFSET, 1, 2, 3);
            Assert.False(string.IsNullOrEmpty(err));
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                body(rom);
            }
            finally { CoreState.ROM = savedRom; }
        }

        static void WithRomVersion(Func<ROM> make, Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = make();
                CoreState.ROM = rom;
                body(rom);
            }
            finally { CoreState.ROM = savedRom; }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        static ROM MakeFE7Rom()
        {
            var rom = new ROM();
            rom.LoadLow("synth_fe7.gba", new byte[0x1000000], "AE7E01");
            return rom;
        }

        // Plant the 12-byte record table (base pointer wired) + record `id`:
        //   +0 path-data pointer (so MakePathList includes it)
        //   +8 movement-data pointer (to `moveOffset`)
        // and lay down `nodeCount` placeholder nodes + a terminator (caller may
        // overwrite the nodes with their own data).
        static void PlantRecord(ROM rom, int id, uint pathDataOffset, uint moveOffset, int nodeCount)
        {
            SetPtr(rom, rom.RomInfo.worldmap_road_pointer, ROAD_TABLE_OFFSET);
            // Minimal point table so MakePathList labels resolve.
            SetPtr(rom, rom.RomInfo.worldmap_point_pointer, POINT_TABLE_OFFSET);

            uint entry = ROAD_TABLE_OFFSET + (uint)id * 12;
            SetPtr(rom, entry + 0, pathDataOffset);
            rom.write_u8(entry + 4, 1); // base-point id #1
            rom.write_u8(entry + 5, 2); // base-point id #2
            SetPtr(rom, entry + 8, moveOffset);

            for (int i = 0; i < nodeCount; i++)
                WriteNode(rom, moveOffset + (uint)(i * 8), (ushort)(i + 1), (ushort)i, (ushort)i);
            rom.write_u32(moveOffset + (uint)(nodeCount * 8), 0xFFFFFFFF);
        }

        static void WriteNode(ROM rom, uint addr, ushort t, ushort x, ushort y)
        {
            rom.write_u16(addr + 0, t);
            rom.write_u16(addr + 4, x);
            rom.write_u16(addr + 6, y);
        }

        static void SetPtr(ROM rom, uint pointerSlot, uint dataOffset)
            => rom.write_u32(pointerSlot, U.toPointer(dataOffset));
    }
}
