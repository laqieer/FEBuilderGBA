// SPDX-License-Identifier: GPL-3.0-or-later
// #1017 Core tests for EventUnitCoordCore — the FE8 Event-Unit after-coord
// (move-path) list read/write seam.
//
// Uses a synthetic ROM (RomInfo not needed — the seam only touches the unit
// block's W4/B7/D8 fields + the 8-byte blob in free space). Validates:
//   * ReadAfterCoords: START row from W4 (Unk1=Unk2=0xFF, others 0) + blob rows
//   * Read with P8=0 / B7=0 -> only the START row, no throw
//   * Add a coord -> WriteAfterCoords -> re-read: B7++, blob round-trips, W4=list[0]
//   * In-place (same size) keeps P8; grow repoints P8 (both round-trip)
//   * count-0 clear -> P8=0, B7=0, old region 0xFF-filled
//   * index-0 -> W4 coupling
//   * Guards (ZERO mutation): null list, empty list, >255 after-rows, free-space
//     exhaustion -> byte-identical restore
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EventUnitCoordCoreTests
    {
        // A 20-byte unit block near the ROM START. Free 0xFF space follows the
        // midpoint so the append path lands there; the blob is planted in the
        // low region (also 0xFF after the planted bytes).
        const uint UNIT_ADDR = 0x400;       // unit block base
        const uint BLOB_ADDR = 0x800;       // planted after-coord blob base
        const uint ROM_LEN = 0x20000;       // 128 KiB

        static uint W4Addr => UNIT_ADDR + 4;
        static uint B7Addr => UNIT_ADDR + 7;
        static uint P8Addr => UNIT_ADDR + 8;

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF); // free-space marker for FindFreeSpace
            // Zero the low region so reads are deterministic (covers the unit
            // block + the planted blob area).
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U header sig
            return rom;
        }

        /// <summary>Plant a FE8 unit block: W4 packed pos + B7 records + D8->blob.</summary>
        static void PlantBlock(ROM rom, uint x, uint y, uint ext, Fe8Coord[] afterRecords)
        {
            rom.write_u16(W4Addr, (ushort)U.MakeFe8UnitPos(x, y, ext));
            if (afterRecords == null || afterRecords.Length == 0)
            {
                rom.write_u8(B7Addr, 0);
                rom.write_p32(P8Addr, 0);
                return;
            }
            rom.write_u8(B7Addr, (uint)afterRecords.Length);
            rom.write_p32(P8Addr, U.toPointer(BLOB_ADDR));
            uint a = BLOB_ADDR;
            foreach (Fe8Coord r in afterRecords)
            {
                rom.write_u16(a + 0, (ushort)U.MakeFe8UnitPos(r.X, r.Y, r.Ext));
                rom.write_u8(a + 2, r.Speed);
                rom.write_u8(a + 3, r.UnitId);
                rom.write_u8(a + 4, r.Unk1);
                rom.write_u8(a + 5, r.Unk2);
                rom.write_u16(a + 6, (ushort)r.Wait);
                a += 8;
            }
        }

        static Fe8Coord Rec(uint x, uint y, uint ext, uint speed, uint unitId, uint unk1, uint unk2, uint wait)
            => new Fe8Coord { X = x, Y = y, Ext = ext, Speed = speed, UnitId = unitId, Unk1 = unk1, Unk2 = unk2, Wait = wait };

        // ---------------------------------------------------------------
        // READ
        // ---------------------------------------------------------------

        [Fact]
        public void ReadAfterCoords_StartRowFromW4_PlusBlobRows()
        {
            ROM rom = MakeRom();
            var records = new[]
            {
                Rec(5, 6, 1, 0x11, 0x22, 0x33, 0x44, 0x1234),
                Rec(7, 8, 2, 0x55, 0x66, 0x77, 0x88, 0x4321),
            };
            PlantBlock(rom, x: 3, y: 4, ext: 2, afterRecords: records);

            var list = EventUnitCoordCore.ReadAfterCoords(rom, rom.u16(W4Addr), rom.u8(B7Addr), rom.u32(P8Addr));

            Assert.Equal(3, list.Count); // START + 2 records

            // START row — from W4: X/Y/Ext, Unk1=Unk2=0xFF, others 0.
            Fe8Coord start = list[0];
            Assert.Equal(3u, start.X);
            Assert.Equal(4u, start.Y);
            Assert.Equal(2u, start.Ext);
            Assert.Equal(0u, start.Speed);
            Assert.Equal(0u, start.UnitId);
            Assert.Equal(0xFFu, start.Unk1);
            Assert.Equal(0xFFu, start.Unk2);
            Assert.Equal(0u, start.Wait);

            // Record 1 — all 8 fields.
            Fe8Coord r1 = list[1];
            Assert.Equal(5u, r1.X); Assert.Equal(6u, r1.Y); Assert.Equal(1u, r1.Ext);
            Assert.Equal(0x11u, r1.Speed); Assert.Equal(0x22u, r1.UnitId);
            Assert.Equal(0x33u, r1.Unk1); Assert.Equal(0x44u, r1.Unk2);
            Assert.Equal(0x1234u, r1.Wait);

            // Record 2.
            Fe8Coord r2 = list[2];
            Assert.Equal(7u, r2.X); Assert.Equal(8u, r2.Y); Assert.Equal(2u, r2.Ext);
            Assert.Equal(0x55u, r2.Speed); Assert.Equal(0x66u, r2.UnitId);
            Assert.Equal(0x77u, r2.Unk1); Assert.Equal(0x88u, r2.Unk2);
            Assert.Equal(0x4321u, r2.Wait);
        }

        [Fact]
        public void ReadAfterCoords_NoBlob_ReturnsOnlyStartRow()
        {
            ROM rom = MakeRom();
            PlantBlock(rom, x: 9, y: 10, ext: 0, afterRecords: null); // P8=0, B7=0

            var list = EventUnitCoordCore.ReadAfterCoords(rom, rom.u16(W4Addr), rom.u8(B7Addr), rom.u32(P8Addr));

            Assert.Single(list);
            Assert.Equal(9u, list[0].X);
            Assert.Equal(10u, list[0].Y);
            Assert.Equal(0xFFu, list[0].Unk1);
        }

        [Fact]
        public void ReadAfterCoords_NullRom_StillReturnsStartRowFromW4()
        {
            // The START row is a pure parse of W4 (no ROM read), so the
            // "always >=1 row" contract holds even with a null ROM — only the
            // blob records need the ROM (Copilot bot review on PR #1073).
            uint w4 = U.MakeFe8UnitPos(7, 12, 0);
            var list = EventUnitCoordCore.ReadAfterCoords(null, w4, b7: 5, p8: 0x08001000);

            Assert.Single(list);
            Assert.Equal(7u, list[0].X);
            Assert.Equal(12u, list[0].Y);
            Assert.Equal(0xFFu, list[0].Unk1);
            Assert.Equal(0xFFu, list[0].Unk2);
        }

        // ---------------------------------------------------------------
        // WRITE — add a coord (append path, grows beyond old size)
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAfterCoords_AddCoord_RoundTrips()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Start: one after-coord planted.
                PlantBlock(rom, x: 1, y: 2, ext: 0, afterRecords: new[]
                {
                    Rec(5, 5, 0, 1, 2, 3, 4, 100),
                });

                // Build list = START + 2 after-coords (one new -> grow).
                var list = new List<Fe8Coord>
                {
                    Rec(1, 2, 0, 0, 0, 0xFF, 0xFF, 0),          // START
                    Rec(5, 5, 0, 1, 2, 3, 4, 100),              // existing
                    Rec(9, 9, 1, 9, 8, 7, 6, 0xBEEF),           // new
                };

                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);
                Assert.Equal("", err);

                // B7 incremented to 2.
                Assert.Equal(2u, rom.u8(B7Addr));
                // W4 = list[0].
                Assert.Equal(U.MakeFe8UnitPos(1, 2, 0), rom.u16(W4Addr));

                // Re-read and compare all 8 fields per record.
                var reread = EventUnitCoordCore.ReadAfterCoords(rom, rom.u16(W4Addr), rom.u8(B7Addr), rom.u32(P8Addr));
                Assert.Equal(3, reread.Count);
                AssertRecordEqual(list[1], reread[1]);
                AssertRecordEqual(list[2], reread[2]);
            }
            finally { CoreState.ROM = saved; }
        }

        // ---------------------------------------------------------------
        // WRITE — in-place vs append
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAfterCoords_SameSize_InPlace_PointerUnchanged()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: new[]
                {
                    Rec(2, 2, 0, 1, 1, 1, 1, 10),
                    Rec(3, 3, 0, 2, 2, 2, 2, 20),
                });
                uint p8Before = rom.u32(P8Addr);

                // Same count (2 after-coords) -> in-place, P8 must NOT change.
                var list = new List<Fe8Coord>
                {
                    Rec(1, 1, 0, 0, 0, 0xFF, 0xFF, 0),
                    Rec(4, 4, 1, 9, 9, 9, 9, 99),  // edited
                    Rec(5, 5, 2, 8, 8, 8, 8, 88),  // edited
                };
                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);
                Assert.Equal("", err);

                Assert.Equal(p8Before, rom.u32(P8Addr)); // in-place: unchanged
                Assert.Equal(2u, rom.u8(B7Addr));

                var reread = EventUnitCoordCore.ReadAfterCoords(rom, rom.u16(W4Addr), rom.u8(B7Addr), rom.u32(P8Addr));
                AssertRecordEqual(list[1], reread[1]);
                AssertRecordEqual(list[2], reread[2]);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void WriteAfterCoords_Grow_Repoints_AndRoundTrips()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: new[]
                {
                    Rec(2, 2, 0, 1, 1, 1, 1, 10),
                });
                uint p8Before = rom.u32(P8Addr);

                // Grow from 1 -> 3 after-coords (beyond old size) -> append + repoint.
                var list = new List<Fe8Coord>
                {
                    Rec(1, 1, 0, 0, 0, 0xFF, 0xFF, 0),
                    Rec(2, 2, 0, 1, 1, 1, 1, 10),
                    Rec(3, 3, 0, 2, 2, 2, 2, 20),
                    Rec(4, 4, 1, 3, 3, 3, 3, 30),
                };
                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);
                Assert.Equal("", err);

                Assert.NotEqual(p8Before, rom.u32(P8Addr)); // repointed
                Assert.True(U.isPointer(rom.u32(P8Addr)));
                Assert.Equal(3u, rom.u8(B7Addr));

                var reread = EventUnitCoordCore.ReadAfterCoords(rom, rom.u16(W4Addr), rom.u8(B7Addr), rom.u32(P8Addr));
                Assert.Equal(4, reread.Count);
                for (int i = 1; i < 4; i++) AssertRecordEqual(list[i], reread[i]);
            }
            finally { CoreState.ROM = saved; }
        }

        // ---------------------------------------------------------------
        // WRITE — count-0 clear
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAfterCoords_RemoveAll_ClearsPointerCountAndFillsOldRegion()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: new[]
                {
                    Rec(2, 2, 0, 1, 1, 1, 1, 10),
                    Rec(3, 3, 0, 2, 2, 2, 2, 20),
                });

                // Only the START row -> count 0 -> clear.
                var list = new List<Fe8Coord> { Rec(1, 1, 0, 0, 0, 0xFF, 0xFF, 0) };
                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);
                Assert.Equal("", err);

                Assert.Equal(0u, rom.u32(P8Addr));
                Assert.Equal(0u, rom.u8(B7Addr));
                // Old region (2 records = 16 bytes at BLOB_ADDR) filled with 0xFF.
                for (uint i = 0; i < 16; i++)
                    Assert.Equal((byte)0xFF, rom.u8(BLOB_ADDR + i));
            }
            finally { CoreState.ROM = saved; }
        }

        // ---------------------------------------------------------------
        // index-0 -> W4 coupling
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAfterCoords_EditStartRow_UpdatesW4()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: null);

                var list = new List<Fe8Coord> { Rec(12, 13, 3, 0, 0, 0xFF, 0xFF, 0) };
                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);
                Assert.Equal("", err);

                Assert.Equal(U.MakeFe8UnitPos(12, 13, 3), rom.u16(W4Addr));
            }
            finally { CoreState.ROM = saved; }
        }

        // ---------------------------------------------------------------
        // Guards — ZERO mutation
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAfterCoords_NullList_NoMutation()
        {
            ROM rom = MakeRom();
            PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: new[] { Rec(2, 2, 0, 1, 1, 1, 1, 10) });
            byte[] before = (byte[])rom.Data.Clone();

            string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, null);

            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void WriteAfterCoords_EmptyList_NoMutation()
        {
            ROM rom = MakeRom();
            PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: new[] { Rec(2, 2, 0, 1, 1, 1, 1, 10) });
            byte[] before = (byte[])rom.Data.Clone();

            string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, new List<Fe8Coord>());

            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void WriteAfterCoords_TooManyAfterRows_NoMutation()
        {
            ROM rom = MakeRom();
            PlantBlock(rom, x: 1, y: 1, ext: 0, afterRecords: null);
            byte[] before = (byte[])rom.Data.Clone();

            // START + 256 after-coords (count-1 = 256 > 255).
            var list = new List<Fe8Coord> { Rec(1, 1, 0, 0, 0, 0xFF, 0xFF, 0) };
            for (int i = 0; i < 256; i++) list.Add(Rec(2, 2, 0, 0, 0, 0, 0, 0));

            string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);

            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void WriteAfterCoords_FreeSpaceExhausted_RestoresByteIdentical()
        {
            var saved = CoreState.ROM;
            try
            {
                // ROM with NO free space: FindFreeSpace treats BOTH 0x00 and
                // 0xFF runs as free, so fill with 0x55 (neither marker) — no
                // contiguous 16-byte free run exists, forcing NOT_FOUND on the
                // append path. (The unit-block field writes touch < a 16-byte
                // contiguous run of one value, so they don't create free space.)
                var rom = new ROM();
                byte[] data = new byte[0x1000];
                Array.Fill(data, (byte)0x55);
                rom.LoadLow("tiny.gba", data, "BE8E01");
                CoreState.ROM = rom;

                // Plant with NO existing blob so the write must take the append
                // path (in-place needs a safe old blob; there is none).
                rom.write_u16(W4Addr, (ushort)U.MakeFe8UnitPos(1, 1, 0));
                rom.write_u8(B7Addr, 0);
                rom.write_p32(P8Addr, 0);
                byte[] before = (byte[])rom.Data.Clone();

                var list = new List<Fe8Coord>
                {
                    Rec(1, 1, 0, 0, 0, 0xFF, 0xFF, 0),
                    Rec(2, 2, 0, 1, 1, 1, 1, 10),
                };
                string err = EventUnitCoordCore.WriteAfterCoords(rom, W4Addr, B7Addr, P8Addr, list);

                Assert.NotEqual("", err);
                // Byte-identical restore: even the W4 write (which DID happen
                // before the append fault) is rolled back by the snapshot.
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = saved; }
        }

        static void AssertRecordEqual(Fe8Coord expected, Fe8Coord actual)
        {
            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(expected.Ext, actual.Ext);
            Assert.Equal(expected.Speed, actual.Speed);
            Assert.Equal(expected.UnitId, actual.UnitId);
            Assert.Equal(expected.Unk1, actual.Unk1);
            Assert.Equal(expected.Unk2, actual.Unk2);
            Assert.Equal(expected.Wait, actual.Wait);
        }
    }
}
