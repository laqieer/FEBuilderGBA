// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the #776 MapEventUnitCore additions used by the Avalonia
// EventUnit New(Alloc) feature + the cross-platform reachability fix:
//   1. AllocNewUnitList — WF EventUnitForm.CreateNewData byte-for-byte parity
//      (count * eventunit_data_size + 1 bytes; B0=1 per row; trailing 0x00).
//   2. AppendBinaryDataHeadless — the shared allocation seam (delegate path
//      + freespace fallback), so it is testable with and without a wired
//      CoreState.AppendBinaryData delegate.
//   3. GetUnitGroupsForMap POINTER_UNIT event-script scan — proves a unit
//      list referenced from the map's event script (the WF way a NEW block
//      becomes "booked") is discovered, alongside direct cond-slot lists.
//
// All ROMs are synthetic (rom.LoadLow), [Collection("SharedState")], and
// defensively swap CoreState.ROM in try/finally.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MapEventUnitCoreNewAllocTests
{
    // FE8 eventunit_data_size = 20 (ROMFE8U.cs:261).
    const uint FE8_UNIT_SIZE = 20;

    static ROM MakeFe8uRom()
    {
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", new byte[0x1100000], "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // A. AllocNewUnitList — payload bytes (WF CreateNewData parity)
    // -----------------------------------------------------------------

    [Fact]
    public void AllocNewUnitList_BuildsCountTimesSizePlusOne_WithB0OnePerRow()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null; // force the freespace fallback

            const uint count = 5;
            uint newBase = MapEventUnitCore.AllocNewUnitList(rom, count, null);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // Each of the 5 rows: B0 == 1, bytes 1..19 == 0.
            for (uint i = 0; i < count; i++)
            {
                uint rowAddr = newBase + i * FE8_UNIT_SIZE;
                Assert.Equal((byte)0x01, rom.Data[rowAddr + 0]);
                for (uint b = 1; b < FE8_UNIT_SIZE; b++)
                {
                    Assert.Equal((byte)0x00, rom.Data[rowAddr + b]);
                }
            }

            // Trailing terminator byte at base + count*size == 0x00.
            uint termAddr = newBase + count * FE8_UNIT_SIZE;
            Assert.Equal((byte)0x00, rom.Data[termAddr]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void AllocNewUnitList_BoundaryCounts_MinAndMaxSucceed_ZeroAndOver50Fail()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null;

            // count == 1 (min) succeeds.
            Assert.NotEqual(U.NOT_FOUND, MapEventUnitCore.AllocNewUnitList(rom, 1, null));
            // count == 50 (max) succeeds.
            Assert.NotEqual(U.NOT_FOUND, MapEventUnitCore.AllocNewUnitList(rom, 50, null));
            // count == 0 fails (no write).
            Assert.Equal(U.NOT_FOUND, MapEventUnitCore.AllocNewUnitList(rom, 0, null));
            // count == 51 (over cap) fails.
            Assert.Equal(U.NOT_FOUND, MapEventUnitCore.AllocNewUnitList(rom, 51, null));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void AllocNewUnitList_CountZero_WritesNothing()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null;

            // Snapshot the freespace region that a real alloc would touch.
            uint searchStart = (uint)(rom.Data.Length / 2);
            byte[] before = rom.getBinaryData(searchStart, 256);

            uint result = MapEventUnitCore.AllocNewUnitList(rom, 0, null);
            Assert.Equal(U.NOT_FOUND, result);

            byte[] after = rom.getBinaryData(searchStart, 256);
            Assert.Equal(before, after);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void AllocNewUnitList_NullRom_ReturnsNotFound()
    {
        Assert.Equal(U.NOT_FOUND, MapEventUnitCore.AllocNewUnitList(null, 3, null));
    }

    // -----------------------------------------------------------------
    // B. Allocator-seam wiring (def-of-done #2)
    // -----------------------------------------------------------------

    [Fact]
    public void AppendBinaryDataHeadless_FreespaceFallback_SucceedsWithoutDelegate()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null; // no delegate

            byte[] buffer = new byte[FE8_UNIT_SIZE + 1];
            buffer[0] = 1;
            uint newBase = MapEventUnitCore.AppendBinaryDataHeadless(rom, buffer, null);
            Assert.NotEqual(U.NOT_FOUND, newBase);
            // The bytes really landed at newBase.
            Assert.Equal((byte)0x01, rom.Data[newBase + 0]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void AppendBinaryDataHeadless_DelegateBranch_RoutesThroughDelegate()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;

            const uint knownOffset = 0x00900000u;
            int recordedLen = -1;
            Undo.UndoData? recordedUndo = null;
            var spyUndo = new Undo.UndoData
            {
                name = "spy",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };

            CoreState.AppendBinaryData = (buf, undo) =>
            {
                recordedLen = buf.Length;
                recordedUndo = undo;
                return knownOffset;
            };

            byte[] buffer = new byte[FE8_UNIT_SIZE + 1];
            uint newBase = MapEventUnitCore.AppendBinaryDataHeadless(rom, buffer, spyUndo);

            Assert.Equal(knownOffset, newBase);
            Assert.Equal(buffer.Length, recordedLen);
            Assert.Same(spyUndo, recordedUndo); // the passed undodata was used
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void AllocNewUnitList_DelegateBranch_BuildsCorrectPayloadAndRoutes()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;

            const uint knownOffset = 0x00920000u;
            byte[]? capturedBuffer = null;
            var spyUndo = new Undo.UndoData
            {
                name = "spy",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            CoreState.AppendBinaryData = (buf, undo) =>
            {
                capturedBuffer = buf;
                return knownOffset;
            };

            const uint count = 3;
            uint newBase = MapEventUnitCore.AllocNewUnitList(rom, count, spyUndo);

            Assert.Equal(knownOffset, newBase);
            Assert.NotNull(capturedBuffer);
            // count * size + 1 bytes; B0=1 per row.
            Assert.Equal((int)(count * FE8_UNIT_SIZE + 1), capturedBuffer!.Length);
            for (uint i = 0; i < count; i++)
            {
                Assert.Equal((byte)0x01, capturedBuffer[i * FE8_UNIT_SIZE + 0]);
            }
            Assert.Equal((byte)0x00, capturedBuffer[count * FE8_UNIT_SIZE]); // terminator
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    // -----------------------------------------------------------------
    // C. Undo restoration
    // -----------------------------------------------------------------

    [Fact]
    public void AllocNewUnitList_RolledBackByUndoScope_RestoresFreespaceBytes()
    {
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.AppendBinaryData = null; // freespace fallback captures undo via ambient scope

            uint searchStart = (uint)(rom.Data.Length / 2);
            byte[] before = rom.getBinaryData(searchStart, 512);

            var undodata = CoreState.Undo.NewUndoData("EventUnit NEW rollback test");
            uint newBase;
            using (ROM.BeginUndoScope(undodata))
            {
                newBase = MapEventUnitCore.AllocNewUnitList(rom, 5, undodata);
                Assert.NotEqual(U.NOT_FOUND, newBase);
                // Bytes were written (B0 of row 0 == 1).
                Assert.Equal((byte)0x01, rom.Data[newBase + 0]);
            }
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();

            // After rollback the freespace region is byte-identical to before.
            byte[] after = rom.getBinaryData(searchStart, 512);
            Assert.Equal(before, after);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    // -----------------------------------------------------------------
    // D. Reachability — GetUnitGroupsForMap POINTER_UNIT event-script scan
    //    (the load-bearing Part-2 parity fix). Builds the full FE8 map ->
    //    PLIST -> event cond block chain, plants a direct PlayerUnit cond
    //    slot AND a START_EVENT script that LOADs a unit list via a
    //    POINTER_UNIT arg, then asserts BOTH lists are discovered.
    // -----------------------------------------------------------------

    [Fact]
    public void GetUnitGroupsForMap_DiscoversScriptPointerUnitList_AndDirectCondSlot()
    {
        // Synthetic FE8 event-script definitions: one LOAD command carrying a
        // POINTER_UNIT arg, plus an ENDA terminator. Mirrors the real
        // config/data/event_*.txt schema (POINTER_UNIT arg token).
        var loadScript = EventScript.ParseScriptLine("12000000XXXXXXXX\tLOADUNIT [XXXX:POINTER_UNIT:Units]");
        var endScript = EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
        Assert.NotNull(loadScript);
        Assert.NotNull(endScript);
        Assert.Equal(EventScript.ScriptHas.POINTER_UNIT_OR_EVENT, loadScript!.Has);
        Assert.Equal(EventScript.ScriptHas.TERM, endScript!.Has);

        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEs = CoreState.EventScript;
        var prevComment = CoreState.CommentCache;
        try
        {
            CoreState.ROM = rom;
            CoreState.EventScript = BuildEventScript(loadScript, endScript);
            // EventScript.DisAseemble reads CoreState.CommentCache.At(...).
            CoreState.CommentCache ??= new HeadlessEtcCache();

            const uint mapId = 0;

            // --- 1. map setting table -> map record (event plist byte) ---
            uint mapTableBase = 0x00800000u;
            uint mapSize = rom.RomInfo.map_setting_datasize; // 148
            Write32(rom, rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);
            uint mapRecord = mapTableBase + mapId * mapSize;
            Write32(rom, mapRecord + 0, 0x08123456u); // first dword pointer => valid map
            const byte eventPlist = 7;
            rom.Data[mapRecord + rom.RomInfo.map_setting_event_plist_pos] = eventPlist;

            // --- 2. event pointer table -> event cond block ---
            uint eventTableBase = 0x00810000u;
            Write32(rom, rom.RomInfo.map_event_pointer, eventTableBase | 0x08000000u);
            uint eventCondBlock = 0x00820000u;
            Write32(rom, eventTableBase + eventPlist * 4u, eventCondBlock | 0x08000000u);

            // --- 3. event cond block slots (FE8 layout) ---
            var slots = MapEventUnitCore.GetCondSlots(rom);
            int playerSlotIdx = -1, startEventIdx = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (playerSlotIdx < 0 && slots[i].Type == MapEventUnitCore.CondType.PlayerUnit)
                    playerSlotIdx = i;
                if (startEventIdx < 0 && slots[i].Type == MapEventUnitCore.CondType.StartEvent)
                    startEventIdx = i;
            }
            Assert.True(playerSlotIdx >= 0, "FE8 must have a PlayerUnit slot");
            Assert.True(startEventIdx >= 0, "FE8 must have a StartEvent slot");

            // 3a. Direct cond-slot unit list (the pre-existing path).
            uint directUnitList = 0x00830000u;
            rom.Data[directUnitList + 0] = 0x10; // a unit id (non-terminator row)
            Write32(rom, eventCondBlock + (uint)playerSlotIdx * 4u, directUnitList | 0x08000000u);

            // 3b. START_EVENT slot -> event script that LOADs a script-only
            //     unit list via a POINTER_UNIT arg (the new scan path).
            uint scriptAddr = 0x00840000u;
            Write32(rom, eventCondBlock + (uint)startEventIdx * 4u, scriptAddr | 0x08000000u);

            uint scriptUnitList = 0x00850000u;
            rom.Data[scriptUnitList + 0] = 0x20; // a unit id (non-terminator row)
            // Plant LOADUNIT command bytes at scriptAddr (real FE schema
            // "12000000XXXXXXXX"): opcode 0x12 00 00 00 then a 4-byte
            // POINTER_UNIT at offset 4 = U.toPointer(scriptUnitList).
            rom.Data[scriptAddr + 0] = 0x12;
            rom.Data[scriptAddr + 1] = 0x00;
            rom.Data[scriptAddr + 2] = 0x00;
            rom.Data[scriptAddr + 3] = 0x00;
            Write32(rom, scriptAddr + 4, scriptUnitList | 0x08000000u);
            // Then an ENDA terminator (0x0A 00 00 00) right after the 8-byte cmd.
            rom.Data[scriptAddr + 8] = 0x0A;
            rom.Data[scriptAddr + 9] = 0x00;
            rom.Data[scriptAddr + 10] = 0x00;
            rom.Data[scriptAddr + 11] = 0x00;

            // --- 4. Assert BOTH lists are discovered ---
            var groups = MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);

            Assert.Contains(groups, g => g.addr == directUnitList);   // no regression
            Assert.Contains(groups, g => g.addr == scriptUnitList);   // POINTER_UNIT scan
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.EventScript = prevEs;
            CoreState.CommentCache = prevComment;
        }
    }

    [Fact]
    public void GetUnitGroupsForMap_EndToEnd_AllocThenScriptReferenceThenDiscover()
    {
        // End-to-end Option-A reachability: AllocNewUnitList a block, then
        // simulate the user referencing it from the map's START_EVENT script
        // (POINTER_UNIT arg), then assert GetUnitGroupsForMap discovers the
        // freshly-allocated block — i.e. it is no longer an orphan.
        var loadScript = EventScript.ParseScriptLine("12000000XXXXXXXX\tLOADUNIT [XXXX:POINTER_UNIT:Units]");
        var endScript = EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
        Assert.NotNull(loadScript);
        Assert.NotNull(endScript);

        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevEs = CoreState.EventScript;
        var prevDelegate = CoreState.AppendBinaryData;
        var prevComment = CoreState.CommentCache;
        try
        {
            CoreState.ROM = rom;
            CoreState.EventScript = BuildEventScript(loadScript!, endScript!);
            CoreState.AppendBinaryData = null; // freespace fallback
            CoreState.CommentCache ??= new HeadlessEtcCache();

            const uint mapId = 0;

            // Build the map -> plist -> cond block chain (no direct unit slot).
            uint mapTableBase = 0x00800000u;
            uint mapSize = rom.RomInfo.map_setting_datasize;
            Write32(rom, rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);
            uint mapRecord = mapTableBase + mapId * mapSize;
            Write32(rom, mapRecord + 0, 0x08123456u);
            const byte eventPlist = 5;
            rom.Data[mapRecord + rom.RomInfo.map_setting_event_plist_pos] = eventPlist;

            uint eventTableBase = 0x00810000u;
            Write32(rom, rom.RomInfo.map_event_pointer, eventTableBase | 0x08000000u);
            uint eventCondBlock = 0x00820000u;
            Write32(rom, eventTableBase + eventPlist * 4u, eventCondBlock | 0x08000000u);

            var slots = MapEventUnitCore.GetCondSlots(rom);
            int startEventIdx = -1;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Type == MapEventUnitCore.CondType.StartEvent) { startEventIdx = i; break; }
            Assert.True(startEventIdx >= 0);

            // 1. Allocate a NEW block (Part 1).
            uint newBase = MapEventUnitCore.AllocNewUnitList(rom, 3, null);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // Before the script reference: NOT discovered (orphan).
            var before = MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);
            Assert.DoesNotContain(before, g => g.addr == newBase);

            // 2. Simulate referencing the NEW block from the START_EVENT script.
            uint scriptAddr = 0x00840000u;
            Write32(rom, eventCondBlock + (uint)startEventIdx * 4u, scriptAddr | 0x08000000u);
            // LOADUNIT opcode 0x12 00 00 00 + 4-byte POINTER_UNIT at offset 4.
            rom.Data[scriptAddr + 0] = 0x12;
            rom.Data[scriptAddr + 1] = 0x00;
            rom.Data[scriptAddr + 2] = 0x00;
            rom.Data[scriptAddr + 3] = 0x00;
            Write32(rom, scriptAddr + 4, newBase | 0x08000000u); // POINTER_UNIT = the NEW block
            rom.Data[scriptAddr + 8] = 0x0A; // ENDA terminator at offset 8

            // 3. After the script reference: discovered via the POINTER_UNIT scan.
            var after = MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);
            Assert.Contains(after, g => g.addr == newBase);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.EventScript = prevEs;
            CoreState.AppendBinaryData = prevDelegate;
            CoreState.CommentCache = prevComment;
        }
    }

    [Fact]
    public void GetUnitGroupsForMap_RomNotMatchingCoreStateRom_SkipsScriptScan_NoThrow()
    {
        // Regression for #786 review: the EventScript disasm path is bound to
        // the static CoreState.ROM (+ RomInfo/Data/CommentCache). When
        // GetUnitGroupsForMap is called with a ROM that is NOT CoreState.ROM
        // (or CoreState.ROM is null), the POINTER_UNIT script scan must be
        // SKIPPED — no throw, no mis-scan — while still returning the direct
        // unit-placement cond-slot results.
        var loadScript = EventScript.ParseScriptLine("12000000XXXXXXXX\tLOADUNIT [XXXX:POINTER_UNIT:Units]");
        var endScript = EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
        Assert.NotNull(loadScript);
        Assert.NotNull(endScript);

        // `rom` is a standalone ROM instance, deliberately NOT assigned to
        // CoreState.ROM. We build the full map chain in it (direct cond-slot
        // list + a START_EVENT script referencing a unit list).
        ROM rom = MakeFe8uRom();
        uint directUnitList;
        uint scriptUnitList;
        BuildMapWithDirectAndScriptUnitLists(rom, mapId: 0, out directUnitList, out scriptUnitList);

        var prevRom = CoreState.ROM;
        var prevEs = CoreState.EventScript;
        try
        {
            // CoreState.ROM is null (a different instance would behave the same).
            CoreState.ROM = null;
            // EventScript loaded but the script scan must still be skipped.
            CoreState.EventScript = BuildEventScript(loadScript!, endScript!);

            // Must NOT throw even though CoreState.ROM != rom / is null.
            var groups = MapEventUnitCore.GetUnitGroupsForMap(rom, 0);

            // Direct cond-slot list is still returned (no script scan needed).
            Assert.Contains(groups, g => g.addr == directUnitList);
            // The script-referenced list is NOT discovered (scan skipped).
            Assert.DoesNotContain(groups, g => g.addr == scriptUnitList);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.EventScript = prevEs;
        }
    }

    [Fact]
    public void GetUnitGroupsForMap_RomIsDifferentInstanceFromCoreStateRom_SkipsScriptScan()
    {
        // Same #786 guard, but with CoreState.ROM set to a DIFFERENT non-null
        // ROM instance than the one passed in — the ReferenceEquals guard must
        // still skip the script scan (and not consult the wrong ROM's bytes).
        var loadScript = EventScript.ParseScriptLine("12000000XXXXXXXX\tLOADUNIT [XXXX:POINTER_UNIT:Units]");
        var endScript = EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
        Assert.NotNull(loadScript);
        Assert.NotNull(endScript);

        ROM target = MakeFe8uRom();
        uint directUnitList;
        uint scriptUnitList;
        BuildMapWithDirectAndScriptUnitLists(target, mapId: 0, out directUnitList, out scriptUnitList);

        ROM other = MakeFe8uRom(); // a different instance

        var prevRom = CoreState.ROM;
        var prevEs = CoreState.EventScript;
        var prevComment = CoreState.CommentCache;
        try
        {
            CoreState.ROM = other; // != target
            CoreState.EventScript = BuildEventScript(loadScript!, endScript!);
            CoreState.CommentCache ??= new HeadlessEtcCache();

            var groups = MapEventUnitCore.GetUnitGroupsForMap(target, 0);

            Assert.Contains(groups, g => g.addr == directUnitList);
            Assert.DoesNotContain(groups, g => g.addr == scriptUnitList);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.EventScript = prevEs;
            CoreState.CommentCache = prevComment;
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build the full FE8 map -> PLIST -> event-cond-block chain in
    /// <paramref name="rom"/> for <paramref name="mapId"/>, with (a) a direct
    /// PlayerUnit cond-slot unit list and (b) a START_EVENT script that LOADs
    /// a separate unit list via a POINTER_UNIT arg. Returns both list bases.
    /// </summary>
    static void BuildMapWithDirectAndScriptUnitLists(ROM rom, uint mapId, out uint directUnitList, out uint scriptUnitList)
    {
        uint mapTableBase = 0x00800000u;
        uint mapSize = rom.RomInfo.map_setting_datasize;
        Write32(rom, rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);
        uint mapRecord = mapTableBase + mapId * mapSize;
        Write32(rom, mapRecord + 0, 0x08123456u);
        const byte eventPlist = 7;
        rom.Data[mapRecord + rom.RomInfo.map_setting_event_plist_pos] = eventPlist;

        uint eventTableBase = 0x00810000u;
        Write32(rom, rom.RomInfo.map_event_pointer, eventTableBase | 0x08000000u);
        uint eventCondBlock = 0x00820000u;
        Write32(rom, eventTableBase + eventPlist * 4u, eventCondBlock | 0x08000000u);

        var slots = MapEventUnitCore.GetCondSlots(rom);
        int playerSlotIdx = -1, startEventIdx = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (playerSlotIdx < 0 && slots[i].Type == MapEventUnitCore.CondType.PlayerUnit)
                playerSlotIdx = i;
            if (startEventIdx < 0 && slots[i].Type == MapEventUnitCore.CondType.StartEvent)
                startEventIdx = i;
        }

        directUnitList = 0x00830000u;
        rom.Data[directUnitList + 0] = 0x10;
        Write32(rom, eventCondBlock + (uint)playerSlotIdx * 4u, directUnitList | 0x08000000u);

        uint scriptAddr = 0x00840000u;
        Write32(rom, eventCondBlock + (uint)startEventIdx * 4u, scriptAddr | 0x08000000u);
        scriptUnitList = 0x00850000u;
        rom.Data[scriptUnitList + 0] = 0x20;
        rom.Data[scriptAddr + 0] = 0x12;
        rom.Data[scriptAddr + 1] = 0x00;
        rom.Data[scriptAddr + 2] = 0x00;
        rom.Data[scriptAddr + 3] = 0x00;
        Write32(rom, scriptAddr + 4, scriptUnitList | 0x08000000u);
        rom.Data[scriptAddr + 8] = 0x0A; // ENDA terminator
    }

    static EventScript BuildEventScript(params EventScript.Script[] scripts)
    {
        var es = new EventScript();
        var prop = typeof(EventScript).GetProperty("Scripts");
        prop!.SetValue(es, scripts);
        return es;
    }

    static void Write32(ROM rom, uint offset, uint value)
    {
        rom.Data[offset + 0] = (byte)(value & 0xFF);
        rom.Data[offset + 1] = (byte)((value >> 8) & 0xFF);
        rom.Data[offset + 2] = (byte)((value >> 16) & 0xFF);
        rom.Data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
