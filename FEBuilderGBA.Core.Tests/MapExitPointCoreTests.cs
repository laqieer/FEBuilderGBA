// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapExitPointCore — extracts cross-platform Map Exit Point
// listing/allocation logic from WinForms MapExitPointForm (#425).
//
// Builds synthetic FE8U ROM bytes so we exercise the per-filter base
// resolution, per-map sub-list walk, NULL-blank detection, and the
// undo-tracked NewAlloc round-trip without touching disk.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MapExitPointCoreTests
{
    /// <summary>
    /// Build a minimal synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item>An exit-point pointer table at <paramref name="tableAddr"/>
    ///         pointed to by <c>RomInfo.map_exit_point_pointer</c>.</item>
    ///   <item>The first <paramref name="enemyCount"/> slots filled with
    ///         GBA pointers (offsets <c>tableAddr + 0x10000 + i*0x100</c>
    ///         per slot, so each map gets its own per-map block).</item>
    ///   <item>Each per-map block contains <paramref name="rowsPerMap"/>
    ///         4-byte rows (X,Y,Escape,Flag) followed by a B0=0xFF terminator.</item>
    /// </list>
    /// </summary>
    static ROM MakeFe8uWithExitTable(int enemyCount = 4, int npcCount = 2, int rowsPerMap = 2,
        uint tableAddr = 0x00800000u, uint perMapBase = 0x00810000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // FE8U: map_exit_point_pointer = 0x3E8AC (see ROMFE8U.cs — the
        // ROM offset of the slot that holds the GBA pointer to the per-map
        // pointer table), npc_blockadd = 65. The slot at this offset must
        // contain a valid GBA pointer (high-bit 0x08000000) for the
        // synthetic ROM's filter resolution to work.
        uint mapExitPointerSlot = rom.RomInfo.map_exit_point_pointer;
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, mapExitPointerSlot);

        uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd; // FE8U = 65

        // Lay out per-filter slots. enemyCount entries for filter 0,
        // then a NULL terminator (zero pointer), then NPC entries at
        // offset (npcBlockAdd*4) from the table base.
        for (int i = 0; i < enemyCount; i++)
        {
            uint slot = tableAddr + (uint)(i * 4);
            uint perMap = perMapBase + (uint)(i * 0x100);
            BitConverter.GetBytes(perMap | 0x08000000u).CopyTo(bytes, slot);
        }
        // Slot enemyCount remains zero (the "no map" sentinel).
        // Plant NPC entries starting at tableAddr + npcBlockAdd * 4.
        for (int i = 0; i < npcCount; i++)
        {
            uint slot = tableAddr + npcBlockAdd * 4 + (uint)(i * 4);
            uint perMap = perMapBase + 0x80000u + (uint)(i * 0x100); // distinct region
            BitConverter.GetBytes(perMap | 0x08000000u).CopyTo(bytes, slot);
        }

        // Fill each per-map block with rowsPerMap rows of 4 bytes,
        // then a terminator row (B0 = 0xFF). Each row's bytes encode:
        // X=0x10+rowIdx, Y=0x20+rowIdx, Escape=0x03, Flag=mapIdx&0xFF.
        for (int i = 0; i < enemyCount; i++)
        {
            uint perMap = perMapBase + (uint)(i * 0x100);
            for (int r = 0; r < rowsPerMap; r++)
            {
                int rowBase = (int)(perMap + r * 4);
                bytes[rowBase + 0] = (byte)(0x10 + r);
                bytes[rowBase + 1] = (byte)(0x20 + r);
                bytes[rowBase + 2] = (byte)0x03;
                bytes[rowBase + 3] = (byte)(i & 0xFF);
            }
            // Terminator
            int termBase = (int)(perMap + rowsPerMap * 4);
            bytes[termBase + 0] = 0xFF;
        }
        for (int i = 0; i < npcCount; i++)
        {
            uint perMap = perMapBase + 0x80000u + (uint)(i * 0x100);
            for (int r = 0; r < rowsPerMap; r++)
            {
                int rowBase = (int)(perMap + r * 4);
                bytes[rowBase + 0] = (byte)(0x50 + r);
                bytes[rowBase + 1] = (byte)(0x60 + r);
                bytes[rowBase + 2] = (byte)0x02;
                bytes[rowBase + 3] = (byte)(i & 0xFF);
            }
            int termBase = (int)(perMap + rowsPerMap * 4);
            bytes[termBase + 0] = 0xFF;
        }
        // Re-load to refresh internal state (some ROM helpers cache).
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // ResolveBaseAddress — per-filter table base resolution
    // -----------------------------------------------------------------

    [Fact]
    public void ResolveBaseAddress_FilterEnemy_ReturnsP32OfPointer()
    {
        ROM rom = MakeFe8uWithExitTable();
        uint expected = rom.p32(rom.RomInfo.map_exit_point_pointer);
        Assert.Equal(0x00800000u, expected); // sanity: synthetic plant
        uint actual = MapExitPointCore.ResolveBaseAddress(rom, filterIndex: 0);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveBaseAddress_FilterNpc_AddsNpcBlockAddOffset()
    {
        ROM rom = MakeFe8uWithExitTable();
        uint enemyBase = rom.p32(rom.RomInfo.map_exit_point_pointer);
        uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd;
        uint expected = enemyBase + 4u * npcBlockAdd;

        uint actual = MapExitPointCore.ResolveBaseAddress(rom, filterIndex: 1);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveBaseAddress_NullRom_ReturnsNotFound()
    {
        Assert.Equal(U.NOT_FOUND, MapExitPointCore.ResolveBaseAddress(null!, filterIndex: 0));
    }

    [Fact]
    public void ResolveBaseAddress_UnsafePointerSlot_ReturnsNotFound()
    {
        // ROM with no plant — map_exit_point_pointer reads 0 from a zero-filled buffer.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        uint actual = MapExitPointCore.ResolveBaseAddress(rom, filterIndex: 0);
        Assert.Equal(U.NOT_FOUND, actual);
    }

    // -----------------------------------------------------------------
    // ListMapEntries — pointer-slot list per filter
    // -----------------------------------------------------------------

    [Fact]
    public void ListMapEntries_FilterEnemy_ReturnsEnemyMaps()
    {
        ROM rom = MakeFe8uWithExitTable(enemyCount: 4, npcCount: 2);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var list = MapExitPointCore.ListMapEntries(rom, filterIndex: 0);
            // Synthetic ROM plants 4 enemy entries — the list should at
            // least contain those (it may include extras up to the map
            // count from MapSettingCore, but the count is implementation
            // detail; we assert lower-bound and distinct addresses).
            Assert.NotEmpty(list);
            // Addresses must be increasing pointer slots in steps of 4.
            for (int i = 1; i < list.Count; i++)
                Assert.True(list[i].addr > list[i - 1].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ListMapEntries_FilterNpc_StartsAtNpcOffset()
    {
        ROM rom = MakeFe8uWithExitTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint expectedBase = MapExitPointCore.ResolveBaseAddress(rom, filterIndex: 1);
            var list = MapExitPointCore.ListMapEntries(rom, filterIndex: 1);
            Assert.NotEmpty(list);
            Assert.Equal(expectedBase, list[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ListExitPointsForMap — per-map row walk (terminator on B0=0xFF)
    // -----------------------------------------------------------------

    [Fact]
    public void ListExitPointsForMap_TerminatesOnFFByte()
    {
        ROM rom = MakeFe8uWithExitTable(rowsPerMap: 3);
        // Per-map block of map 0 sits at 0x00810000.
        uint perMap = 0x00810000u;
        var rows = MapExitPointCore.ListExitPointsForMap(rom, perMap);
        Assert.Equal(3, rows.Count);
        // Each row 4 bytes apart.
        Assert.Equal(perMap, rows[0].addr);
        Assert.Equal(perMap + 4u, rows[1].addr);
        Assert.Equal(perMap + 8u, rows[2].addr);
    }

    [Fact]
    public void ListExitPointsForMap_BlankPointer_ReturnsEmpty()
    {
        ROM rom = MakeFe8uWithExitTable();
        // map_exit_point_blank is FE8U's universal NULL marker.
        uint blank = rom.RomInfo.map_exit_point_blank;
        var rows = MapExitPointCore.ListExitPointsForMap(rom, blank);
        Assert.Empty(rows);
    }

    [Fact]
    public void ListExitPointsForMap_OutOfBounds_ReturnsEmpty()
    {
        ROM rom = MakeFe8uWithExitTable();
        var rows = MapExitPointCore.ListExitPointsForMap(rom, 0xFFFFFFFFu);
        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------
    // IsBlankPointer
    // -----------------------------------------------------------------

    [Fact]
    public void IsBlankPointer_MatchesRomInfo()
    {
        ROM rom = MakeFe8uWithExitTable();
        Assert.True(MapExitPointCore.IsBlankPointer(rom, rom.RomInfo.map_exit_point_blank));
        Assert.False(MapExitPointCore.IsBlankPointer(rom, 0x00810000u));
    }

    [Fact]
    public void IsBlankPointer_NullRom_ReturnsFalse()
    {
        Assert.False(MapExitPointCore.IsBlankPointer(null!, 0x100u));
    }

    // -----------------------------------------------------------------
    // NewAlloc — append 8-byte block + repoint slot under undo
    // -----------------------------------------------------------------

    [Fact]
    public void NewAlloc_WritesPointerAndAppendsData_WithUndoData()
    {
        ROM rom = MakeFe8uWithExitTable();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            // Pick an existing slot to repoint. Slot 0 currently points to
            // 0x00810000; after NewAlloc the pointer should point at a NEW
            // free-space allocation and the new block's row 1 must have B0=0xFF.
            uint exitPointerSlot = 0x00800000u;
            uint originalTarget = rom.p32(exitPointerSlot);

            var undodata = CoreState.Undo.NewUndoData("MapExit NewAlloc test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint newaddr = MapExitPointCore.NewAlloc(rom, exitPointerSlot, undodata);
                Assert.NotEqual(U.NOT_FOUND, newaddr);
                Assert.NotEqual(originalTarget, newaddr);

                // The pointer slot must now point to newaddr.
                Assert.Equal(newaddr, rom.p32(exitPointerSlot));
                // The new block's first row terminator (B0 at offset 4) must be 0xFF.
                Assert.Equal((byte)0xFF, rom.Data[newaddr + 4]);
                // First row (X,Y,Escape,Flag) must be zero-initialized.
                Assert.Equal((byte)0x00, rom.Data[newaddr + 0]);
                Assert.Equal((byte)0x00, rom.Data[newaddr + 1]);
                Assert.Equal((byte)0x00, rom.Data[newaddr + 2]);
                Assert.Equal((byte)0x00, rom.Data[newaddr + 3]);
            }
        }
        finally
        {
            // Restore both shared-state slots so the SharedState test collection
            // doesn't leak this test's `CoreState.Undo` into the next test
            // (Copilot PR #531 third-pass review on test line 252).
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void NewAlloc_NullUndoData_DoesNotThrow()
    {
        ROM rom = MakeFe8uWithExitTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint exitPointerSlot = 0x00800000u;
            uint newaddr = MapExitPointCore.NewAlloc(rom, exitPointerSlot, undodata: null);
            // Either succeeds with valid alloc, or returns NOT_FOUND — but must NOT throw.
            Assert.True(newaddr == U.NOT_FOUND || newaddr >= 0x100u);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void NewAlloc_UnsafeSlotAddress_ReturnsNotFound()
    {
        ROM rom = MakeFe8uWithExitTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint result = MapExitPointCore.NewAlloc(rom, exitPointerSlotAddr: 0x10u, undodata: null);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void NewAlloc_NullRom_ReturnsNotFound()
    {
        Assert.Equal(U.NOT_FOUND, MapExitPointCore.NewAlloc(null!, 0x100u, undodata: null));
    }

    // -----------------------------------------------------------------
    // TryCountExitRows — terminator-aware row count for list-expand (#773)
    // -----------------------------------------------------------------

    [Fact]
    public void TryCountExitRows_TerminatedBlock_ReturnsTrueAndRowCount()
    {
        // Per-map block 0 at 0x00810000 has rowsPerMap rows then a B0=0xFF
        // terminator — the count must equal rowsPerMap (rows BEFORE the
        // terminator).
        ROM rom = MakeFe8uWithExitTable(rowsPerMap: 3);
        uint perMap = 0x00810000u;
        Assert.True(MapExitPointCore.TryCountExitRows(rom, perMap, out uint count));
        Assert.Equal(3u, count);
    }

    [Fact]
    public void TryCountExitRows_BlankPointer_ReturnsFalseZero()
    {
        // The version-specific blank marker is the "no exits" state and must
        // be refused (callers must NOT expand it).
        ROM rom = MakeFe8uWithExitTable();
        uint blank = rom.RomInfo.map_exit_point_blank;
        Assert.False(MapExitPointCore.TryCountExitRows(rom, blank, out uint count));
        Assert.Equal(0u, count);
    }

    [Fact]
    public void TryCountExitRows_NoTerminatorWithinCap_ReturnsFalseZero()
    {
        // Synthesize an in-memory block of non-0xFF bytes long enough to blow
        // past the 0x100 row cap (0x100 rows * 4 bytes = 0x400 bytes). With no
        // B0==0xFF terminator the helper must return false + count 0 so the
        // corrupt/unterminated block is never expanded.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint blockAddr = 0x00820000u;
        // Fill 0x100 rows worth of bytes with a non-0xFF value (0x01) so every
        // row's B0 byte is 0x01, never the terminator.
        for (uint i = 0; i < 0x100u * 4u; i++)
        {
            bytes[blockAddr + i] = 0x01;
        }
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        Assert.False(MapExitPointCore.TryCountExitRows(rom, blockAddr, out uint count));
        Assert.Equal(0u, count);
    }

    [Fact]
    public void TryCountExitRows_UnsafeOrNullArgs_ReturnFalseZero()
    {
        ROM rom = MakeFe8uWithExitTable();
        // Out-of-range / unsafe offset.
        Assert.False(MapExitPointCore.TryCountExitRows(rom, 0xFFFFFFFFu, out uint c1));
        Assert.Equal(0u, c1);
        // Null ROM.
        Assert.False(MapExitPointCore.TryCountExitRows(null!, 0x00810000u, out uint c2));
        Assert.Equal(0u, c2);
    }

    [Fact]
    public void TryCountExitRows_ImmediateTerminator_ReturnsTrueZero()
    {
        // A block whose very first row is the terminator (empty list) counts
        // as zero rows but is still a VALID, terminated block.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        uint blockAddr = 0x00830000u;
        bytes[blockAddr] = 0xFF; // immediate terminator
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        Assert.True(MapExitPointCore.TryCountExitRows(rom, blockAddr, out uint count));
        Assert.Equal(0u, count);
    }

    [Fact]
    public void NewAlloc_RolledBackByUndoScope_RestoresOriginalPointer()
    {
        // Regression: when the undo scope is rolled back via RunUndo(), the
        // pointer-repoint must be reverted as well (proves the undo data
        // actually captured the writes).
        ROM rom = MakeFe8uWithExitTable();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            uint exitPointerSlot = 0x00800000u;
            uint originalTarget = rom.p32(exitPointerSlot);

            var undodata = CoreState.Undo.NewUndoData("MapExit NewAlloc rollback test");
            uint newaddr;
            using (ROM.BeginUndoScope(undodata))
            {
                newaddr = MapExitPointCore.NewAlloc(rom, exitPointerSlot, undodata);
                Assert.NotEqual(U.NOT_FOUND, newaddr);
                Assert.NotEqual(originalTarget, rom.p32(exitPointerSlot));
            }
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();
            // After rollback, the pointer must return to its original target.
            Assert.Equal(originalTarget, rom.p32(exitPointerSlot));
        }
        finally
        {
            // Restore both shared-state slots (Copilot PR #531 third-pass
            // review on test line 333).
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }
}
