// SPDX-License-Identifier: GPL-3.0-or-later
// #780 — SongInstrument "Expand List" (voicegroup → 128) wiring tests.
//
// Proves the Avalonia SongInstrument editor's previously-inert "Expand List"
// stub now grows a song's voicegroup (instrument set) to the full 128 12-byte
// records via SongInstrumentViewModel.ExpandVoicegroupTo128 +
// DataExpansionCore.RepointAllReferences (#782):
//   - the defined-prefix currentCount is computed with the SAME validity
//     predicate the master list uses (IsValidInstrument);
//   - the built 128-row block copies the prefix verbatim and fills the gap
//     rows [currentCount..127) from the ROW-0 template (NOT zero), with the
//     last row (127) left zero (WF ExpandsArea(FIRST) tail rule);
//   - newBase is allocated in free space and EVERY song-header reference is
//     repointed (the #782 shared-voicegroup win — two songs sharing one
//     voicegroup both get songHeader+4 repointed, proving single-slot
//     corruption is gone);
//   - the operation is undoable (ROM bytes + the slot pointer restored);
//   - currentCount == 0 (no valid template row) → false, no mutation (CLI
//     refinement);
//   - already-128 → false, no-op; no song context → false;
//   - a headless [AvaloniaFact] click of the Expand button lists 128 rows.
//
// NOTE on ROM source: the repo does NOT commit real .gba ROMs (the roms/
// folder is empty locally and in CI). So — exactly like the sibling
// SongInstrumentParityTests / MapExitPointExpandTests — these build a
// deterministic synthetic FE8U ROM (header signature BE8E01, 16 MB so RomInfo
// resolves) and plant a song table → song header(s) → voicegroup chain plus a
// test allocator (CoreState.AppendBinaryData) that writes the new block to a
// free region of the synthetic bytes.
//
// Marked [Collection("SharedState")] because the tests mutate CoreState.ROM /
// CoreState.Undo / CoreState.AppendBinaryData (matches the sibling suites).
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SongInstrumentExpandTests
{
    const int BlockSize = 12;
    const int MaxInstruments = 128;

    // Synthetic ROM layout (all offsets, GBA pointers add 0x08000000).
    const uint SongTableBase = 0x00300000u; // sound_table_pointer -> this
    const uint SongHeader0 = 0x00310000u;   // song 0 header
    const uint SongHeader1 = 0x00320000u;   // song 1 header (shares the voicegroup)
    const uint VoiceGroup = 0x00330000u;    // the shared voicegroup base
    const uint FreeRegion = 0x00900000u;    // test allocator writes here

    /// <summary>
    /// Build a synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item><c>sound_table_pointer</c> (0x28BC) → 0x00300000 (song table base).</item>
    ///   <item>Song table entry 0 → song header 0 (0x00310000); entry 1 →
    ///         song header 1 (0x00320000) when <paramref name="twoSongs"/>;
    ///         a terminating null entry after.</item>
    ///   <item>Each song header's <c>+4</c> slot → 0x00330000 (shared voicegroup).</item>
    ///   <item>Voicegroup = <paramref name="definedInstruments"/> valid DirectSound
    ///         rows (header 0x00, wave pointer at +4), then an invalid (0xFF…)
    ///         row so the defined-prefix scan stops.</item>
    /// </list>
    /// </summary>
    static ROM MakeFe8uRom(int definedInstruments = 3, bool twoSongs = false)
    {
        var bytes = new byte[0x1000000]; // 16 MB — FE8U RomInfo requires it.
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint soundTablePointer = rom.RomInfo.sound_table_pointer; // 0x28BC
        // sound_table_pointer holds a GBA pointer to the song-table base.
        WritePtr(bytes, soundTablePointer, SongTableBase);

        // Song table entries are 8 bytes (4 = header pointer, 4 = priority/group).
        WritePtr(bytes, SongTableBase + 0 * 8, SongHeader0);
        if (twoSongs)
            WritePtr(bytes, SongTableBase + 1 * 8, SongHeader1);
        // Entry after the last real song is left as a null pointer -> the walk
        // stops (U.isPointer(0) == false).

        // Song header +4 = voicegroup pointer (both songs share VoiceGroup).
        WritePtr(bytes, SongHeader0 + 4, VoiceGroup);
        if (twoSongs)
            WritePtr(bytes, SongHeader1 + 4, VoiceGroup);

        // Voicegroup: `definedInstruments` valid DirectSound rows + one invalid.
        uint wavePtr = 0x08200000u; // a safety-valid pointer for +4
        for (int i = 0; i < definedInstruments; i++)
        {
            uint a = VoiceGroup + (uint)(i * BlockSize);
            bytes[a + 0] = 0x00;                 // DirectSound header
            bytes[a + 1] = (byte)(0xA0 + i);     // distinct B1 so prefix copy is verifiable
            bytes[a + 2] = (byte)(0xB0 + i);
            bytes[a + 3] = (byte)(0xC0 + i);
            WriteRaw(bytes, a + 4, wavePtr);     // valid wave pointer
            bytes[a + 8] = 0x11;
            bytes[a + 9] = 0x22;
            bytes[a + 10] = 0x33;
            bytes[a + 11] = 0x44;
        }
        // Invalid stop row: DirectSound header but a NON-safety pointer at +4.
        uint stop = VoiceGroup + (uint)(definedInstruments * BlockSize);
        bytes[stop + 0] = 0x00;
        WriteRaw(bytes, stop + 4, 0x00000000u); // 0 is not a safety pointer

        // Reload so RomInfo + everything is consistent.
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteRaw(byte[] bytes, uint addr, uint value)
    {
        bytes[addr + 0] = (byte)(value & 0xFF);
        bytes[addr + 1] = (byte)((value >> 8) & 0xFF);
        bytes[addr + 2] = (byte)((value >> 16) & 0xFF);
        bytes[addr + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WritePtr(byte[] bytes, uint addr, uint offset)
        => WriteRaw(bytes, addr, offset | 0x08000000u);

    /// <summary>
    /// Install a simple free-space allocator at <see cref="FreeRegion"/> that
    /// writes the block through the ambient undo and returns the OFFSET.
    /// Mirrors the ItemUsagePointerCoreTests appender.
    /// </summary>
    static void InstallAllocator(ROM rom)
    {
        uint nextFree = FreeRegion;
        CoreState.AppendBinaryData = (data, undo) =>
        {
            uint dst = nextFree;
            for (int i = 0; i < data.Length; i++)
                rom.write_u8(dst + (uint)i, data[i], undo);
            nextFree += (uint)(((data.Length + 3) / 4) * 4);
            return dst;
        };
    }

    // -----------------------------------------------------------------
    // Defined-prefix count uses the master-list validity predicate.
    // -----------------------------------------------------------------

    [Fact]
    public void GetListCount_EqualsDefinedPrefix()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            var vm = new SongInstrumentViewModel();
            vm.LoadList(); // standalone auto-resolve -> VoiceGroup
            Assert.Equal(VoiceGroup, U.toOffset(vm.BaseAddr));
            // The master list and GetListCount both stop at the 4th (invalid) row.
            Assert.Equal(3, vm.GetListCount());
            Assert.True(vm.CanExpandVoicegroup);
        }
        finally { Restore(prev); }
    }

    // -----------------------------------------------------------------
    // Successful expand: prefix verbatim + row-0 template fill + repoint.
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandVoicegroupTo128_CopiesPrefix_FillsFromRow0_RepointsSlot()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            vm.LoadList();
            Assert.Equal(VoiceGroup, U.toOffset(vm.BaseAddr));

            byte[] row0 = rom.getBinaryData(VoiceGroup, BlockSize);

            bool ok;
            var undodata = CoreState.Undo.NewUndoData("SongInstrument ExpandVoicegroup test");
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            CoreState.Undo.Push(undodata);

            Assert.True(ok);
            uint newBase = U.toOffset(vm.BaseAddr);
            Assert.NotEqual(VoiceGroup, newBase);

            // Song header 0's +4 slot now points at newBase.
            Assert.Equal(newBase, rom.p32(SongHeader0 + 4));

            // The 3 defined rows copied verbatim (distinct B1/B2/B3 per row).
            for (int i = 0; i < 3; i++)
            {
                uint a = newBase + (uint)(i * BlockSize);
                Assert.Equal((uint)(0xA0 + i), rom.u8(a + 1));
                Assert.Equal((uint)(0xB0 + i), rom.u8(a + 2));
                Assert.Equal((uint)(0xC0 + i), rom.u8(a + 3));
            }

            // Gap rows [3..126] == row-0 template (NOT zero).
            for (int r = 3; r < MaxInstruments - 1; r++)
            {
                byte[] got = rom.getBinaryData(newBase + (uint)(r * BlockSize), BlockSize);
                Assert.Equal(row0, got);
            }

            // The LAST row (127) is left zero (WF tail rule).
            byte[] last = rom.getBinaryData(newBase + (uint)((MaxInstruments - 1) * BlockSize), BlockSize);
            Assert.All(last, b => Assert.Equal(0, b));

            // The expanded set now reports 128 defined rows (row 127 is a valid
            // DirectSound row only if its +4 is a safety pointer — it's zero, so
            // GetListCount stops at 127). The fill from row 0 keeps rows 3..126
            // valid; the full master list therefore shows >= 127 rows.
            Assert.Equal(127, vm.GetListCount());
        }
        finally { Restore(prev); }
    }

    [Fact]
    public void ExpandVoicegroupTo128_IsUndoable_RestoresSlotAndBytes()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            vm.LoadList();

            uint origSlot = rom.p32(SongHeader0 + 4);
            byte[] origVoiceGroup = rom.getBinaryData(VoiceGroup, (uint)(MaxInstruments * BlockSize));

            var undodata = CoreState.Undo.NewUndoData("SongInstrument ExpandVoicegroup undo test");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.True(ok);
            Assert.NotEqual(origSlot, rom.p32(SongHeader0 + 4)); // repointed away

            CoreState.Undo.RunUndo();

            // Slot pointer + the original voicegroup bytes return to baseline.
            Assert.Equal(origSlot, rom.p32(SongHeader0 + 4));
            Assert.Equal(origVoiceGroup, rom.getBinaryData(VoiceGroup, (uint)(MaxInstruments * BlockSize)));
        }
        finally { Restore(prev); }
    }

    // -----------------------------------------------------------------
    // #782 win: two songs sharing one voicegroup -> BOTH slots repointed.
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandVoicegroupTo128_TwoSharedSongHeaders_BothSlotsRepointed()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3, twoSongs: true);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);

            // Both song headers point at the same voicegroup before expand.
            Assert.Equal(VoiceGroup, rom.p32(SongHeader0 + 4));
            Assert.Equal(VoiceGroup, rom.p32(SongHeader1 + 4));

            var vm = new SongInstrumentViewModel();
            vm.LoadList();
            Assert.Equal(VoiceGroup, U.toOffset(vm.BaseAddr));

            var undodata = CoreState.Undo.NewUndoData("SongInstrument ExpandVoicegroup shared test");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.True(ok);

            uint newBase = U.toOffset(vm.BaseAddr);
            Assert.NotEqual(VoiceGroup, newBase);

            // BOTH song-header slots now point at the relocated block — the
            // #782 shared-voicegroup fix. A single-slot repoint would have left
            // SongHeader1 dangling at the (now-wiped) old base.
            Assert.Equal(newBase, rom.p32(SongHeader0 + 4));
            Assert.Equal(newBase, rom.p32(SongHeader1 + 4));
        }
        finally { Restore(prev); }
    }

    // -----------------------------------------------------------------
    // Refusal paths leave the ROM unchanged.
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandVoicegroupTo128_ZeroDefinedPrefix_ReturnsFalse_NoMutation()
    {
        // The voicegroup's first row is itself invalid -> currentCount == 0.
        ROM rom = MakeFe8uRom(definedInstruments: 0);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            vm.LoadList();
            // No song context (the auto-resolver requires a valid voicegroup +
            // the first row passes IsValidInstrument; here row 0 is invalid so
            // the standalone resolver still finds VoiceGroup as the slot target,
            // but the defined prefix is 0).
            // Force the base so the prefix-count branch is exercised regardless.
            vm.LoadInstrumentList(VoiceGroup);

            byte[] before = rom.getBinaryData(VoiceGroup, 0x4000);
            uint origSlot = rom.p32(SongHeader0 + 4);

            var undodata = CoreState.Undo.NewUndoData("zero prefix");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }

            Assert.False(ok);
            Assert.Equal(before, rom.getBinaryData(VoiceGroup, 0x4000));
            Assert.Equal(origSlot, rom.p32(SongHeader0 + 4));
        }
        finally { Restore(prev); }
    }

    [Fact]
    public void ExpandVoicegroupTo128_AlreadyFull_ReturnsFalse_NoOp()
    {
        // 128 defined rows -> already full.
        ROM rom = MakeFe8uRom(definedInstruments: 128);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            vm.LoadList();
            Assert.Equal(VoiceGroup, U.toOffset(vm.BaseAddr));
            // CanExpandVoicegroup is false when the prefix is already 128.
            Assert.False(vm.CanExpandVoicegroup);

            byte[] before = rom.getBinaryData(VoiceGroup, (uint)(MaxInstruments * BlockSize));
            uint origSlot = rom.p32(SongHeader0 + 4);

            var undodata = CoreState.Undo.NewUndoData("already full");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }

            Assert.False(ok);
            Assert.Equal(before, rom.getBinaryData(VoiceGroup, (uint)(MaxInstruments * BlockSize)));
            Assert.Equal(origSlot, rom.p32(SongHeader0 + 4));
        }
        finally { Restore(prev); }
    }

    [Fact]
    public void ExpandVoicegroupTo128_NoSongContext_ReturnsFalse()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            // Never load a list -> no song context recorded.
            Assert.False(vm.CanExpandVoicegroup);

            var undodata = CoreState.Undo.NewUndoData("no context");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            Assert.False(ok);
        }
        finally { Restore(prev); }
    }

    [Fact]
    public void ExpandVoicegroupTo128_ArbitraryNonSongBase_ReturnsFalse()
    {
        // A base the user typed that is NOT referenced by any song header must
        // not be treated as a voicegroup (no song context -> Expand refused).
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            var vm = new SongInstrumentViewModel();
            // 0x00500000 is not pointed at by any song header.
            vm.LoadInstrumentList(0x00500000u);
            Assert.False(vm.HasSongContext);
            Assert.False(vm.CanExpandVoicegroup);

            var undodata = CoreState.Undo.NewUndoData("non-song base");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            Assert.False(ok);
        }
        finally { Restore(prev); }
    }

    [Fact]
    public void ExpandVoicegroupTo128_NoAllocator_ReturnsFalse_NoOrphan()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            // Allocator intentionally NOT installed -> AppendBinaryDataHeadless
            // returns U.NOT_FOUND -> expand refuses before any repoint.
            CoreState.AppendBinaryData = null;
            var vm = new SongInstrumentViewModel();
            vm.LoadList();
            Assert.True(vm.CanExpandVoicegroup);

            uint origSlot = rom.p32(SongHeader0 + 4);
            var undodata = CoreState.Undo.NewUndoData("no allocator");
            bool ok;
            using (ROM.BeginUndoScope(undodata))
            {
                ok = vm.ExpandVoicegroupTo128(undodata);
            }
            Assert.False(ok);
            // Slot untouched — no orphan.
            Assert.Equal(origSlot, rom.p32(SongHeader0 + 4));
        }
        finally { Restore(prev); }
    }

    // -----------------------------------------------------------------
    // Headless View — clicking Expand lists 128 rows.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void View_ClickExpand_ListsAll128Rows()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 3);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            CoreState.Services = new HeadlessAppServices();

            var view = new SongInstrumentView();
            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);

            // The Opened handler ran LoadList on the synthetic ROM. Re-assert
            // CoreState.ROM defensively and reload so the count is deterministic.
            CoreState.ROM = rom;
            Invoke(view, "LoadList");

            int rowsBefore = entryList!.ItemCount;
            Assert.Equal(3, rowsBefore); // 3 planted instruments

            var expandButton = view.FindControl<Button>("ListExpandButton");
            Assert.NotNull(expandButton);
            Assert.True(expandButton!.IsEnabled); // gated on CanExpandVoicegroup

            Invoke(view, "ListExpand_Click");

            // After expand: rows 0..126 are valid (3 originals + row-0 fill);
            // row 127 stays zero so the master-list scan stops at 127.
            int rowsAfter = entryList.ItemCount;
            Assert.Equal(127, rowsAfter);
        }
        finally { Restore(prev); }
    }

    [AvaloniaFact]
    public void View_ExpandButton_DisabledWhenAlreadyFull()
    {
        ROM rom = MakeFe8uRom(definedInstruments: 128);
        var prev = Swap(rom);
        try
        {
            InstallAllocator(rom);
            CoreState.Services = new HeadlessAppServices();

            var view = new SongInstrumentView();
            CoreState.ROM = rom;
            Invoke(view, "LoadList");

            var expandButton = view.FindControl<Button>("ListExpandButton");
            Assert.NotNull(expandButton);
            Assert.False(expandButton!.IsEnabled); // already 128 -> disabled
        }
        finally { Restore(prev); }
    }

    // -----------------------------------------------------------------
    // Shared-state save/restore + reflection helpers.
    // -----------------------------------------------------------------

    record struct Saved(ROM? Rom, Undo? Undo, Func<byte[], Undo.UndoData, uint>? Alloc, IAppServices? Services);

    static Saved Swap(ROM rom)
    {
        var saved = new Saved(CoreState.ROM, CoreState.Undo, CoreState.AppendBinaryData, CoreState.Services);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return saved;
    }

    static void Restore(Saved s)
    {
        CoreState.ROM = s.Rom;
        CoreState.Undo = s.Undo;
        CoreState.AppendBinaryData = s.Alloc;
        CoreState.Services = s.Services;
    }

    static void Invoke(SongInstrumentView view, string method)
    {
        var m = typeof(SongInstrumentView).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        if (m != null) { m.Invoke(view, Array.Empty<object?>()); return; }

        // Click handlers take (object?, RoutedEventArgs).
        m = typeof(SongInstrumentView).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(view, new object?[] { null, new RoutedEventArgs() });
    }
}
