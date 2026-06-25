// SPDX-License-Identifier: GPL-3.0-or-later
// #1424 — Demon-King Summon list count parity.
//
// The Avalonia Demon-King Summon viewer previously forced the count to 20
// (21 rows) whenever summons_demon_king_count_address held 0 or a value >=100,
// fabricating adjacent ROM bytes as editable summon rows. The WinForms ground
// truth (SummonsDemonKingForm.cs:36-42) and the Core canon
// (StructExportCore.cs:975-978) are:
//   - count source missing (count_address == 0)  -> 0 rows
//   - count byte >= 100 (corrupt)                 -> 0 rows
//   - otherwise loop i <= count                   -> count 0 -> 1 row,
//                                                    count 11 (vanilla) -> 12 rows
//
// These tests assert BOTH live code paths now match that canon:
//   (a) SummonsDemonKingViewerViewModel.LoadSummonsDemonKingList()
//   (b) ListParityHelper.BuildReferenceList("SummonsDemonKingViewerView")
//       -> ListParityHelper.BuildSummonsDemonKingList() (the GapSweep reference
//          baseline, documented as "matching SummonsDemonKingViewerViewModel").
// and that the two paths agree row-count-for-row-count (parity assertion).
//
// Synthetic FE8U ROM (header "BE8E01"): pointer planted at
// summons_demon_king_pointer (FE8U 0x7B32C) -> SummonTableBase; count byte at
// summons_demon_king_count_address (FE8U 0x7B2BC).
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM
// (BuildReferenceList + isSafetyOffset read the ambient CoreState.ROM).
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SummonsDemonKingCountParityTests : IDisposable
{
    // FE8U RomInfo addresses (ROMFE8U.cs:321-322).
    const uint PointerAddr = 0x7B32Cu;   // summons_demon_king_pointer
    const uint CountAddr   = 0x7B2BCu;   // summons_demon_king_count_address

    // Summon table base (>= 0x200 so U.isSafetyOffset passes), well clear of the
    // pointer/count addresses and with room for >>21 20-byte entries.
    const uint SummonTableBase = 0x00200000u;
    const uint EntrySize = 20u;

    readonly ROM? _savedRom;

    public SummonsDemonKingCountParityTests()
    {
        _savedRom = CoreState.ROM;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
    }

    // ------------------------------------------------------------------
    // Per-case row-count expectations (the canon).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(11, 12)]    // vanilla FE8 -> 12 rows (regression guard, normal path)
    [InlineData(0, 1)]      // cleared count -> 1 row (was 21)
    [InlineData(1, 2)]      // boundary -> 2 rows
    [InlineData(99, 100)]   // largest valid count -> 100 rows
    [InlineData(100, 0)]    // corrupt (>=100) -> 0 rows (was 21)
    [InlineData(255, 0)]    // 0xFF corrupt -> 0 rows (was 21)
    public void RowCount_MatchesCanon_OnBothPaths(int countByte, int expectedRows)
    {
        ROM rom = MakeRom((byte)countByte);
        CoreState.ROM = rom;

        // (a) The live viewer VM.
        var vm = new SummonsDemonKingViewerViewModel();
        var vmList = vm.LoadSummonsDemonKingList();
        Assert.Equal(expectedRows, vmList.Count);

        // (b) The GapSweep reference baseline (routes to BuildSummonsDemonKingList).
        var refList = ListParityHelper.BuildReferenceList("SummonsDemonKingViewerView");
        Assert.NotNull(refList);
        Assert.Equal(expectedRows, refList!.Count);

        // Parity: both code paths agree exactly.
        Assert.Equal(vmList.Count, refList.Count);
    }

    // ------------------------------------------------------------------
    // Cross-validation against the Core canon: both Avalonia paths must match
    // StructExportCore's registered "summons_demon_king" GetEntryCount(rom) for
    // every count value. This pins the Avalonia behavior directly to the
    // single source of truth (StructExportCore.cs:972-979) rather than to a
    // hand-written expectation table.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(255)]
    public void BothPaths_MatchStructExportCoreCanon(int countByte)
    {
        ROM rom = MakeRom((byte)countByte);
        CoreState.ROM = rom;

        var table = StructExportCore.GetTable("summons_demon_king");
        Assert.NotNull(table);
        int canonRows = (int)table!.GetEntryCount(rom);

        var vm = new SummonsDemonKingViewerViewModel();
        Assert.Equal(canonRows, vm.LoadSummonsDemonKingList().Count);

        var refList = ListParityHelper.BuildReferenceList("SummonsDemonKingViewerView");
        Assert.NotNull(refList);
        Assert.Equal(canonRows, refList!.Count);
    }

    // ------------------------------------------------------------------
    // Direct regression: the old behavior fabricated 21 rows for count 0 / 100 /
    // 255. Assert the new behavior never produces 21 rows for those triggers.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(255)]
    public void TriggerCounts_NeverProduce21Rows(int countByte)
    {
        ROM rom = MakeRom((byte)countByte);
        CoreState.ROM = rom;

        var vm = new SummonsDemonKingViewerViewModel();
        int vmCount = vm.LoadSummonsDemonKingList().Count;
        Assert.NotEqual(21, vmCount);

        var refList = ListParityHelper.BuildReferenceList("SummonsDemonKingViewerView");
        Assert.NotNull(refList);
        Assert.NotEqual(21, refList!.Count);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM (header "BE8E01") with:
    ///   - summons_demon_king_pointer (0x7B32C) -> SummonTableBase (GBA pointer)
    ///   - summons_demon_king_count_address (0x7B2BC) = <paramref name="countByte"/>
    ///   - SummonTableBase filled with non-zero unit bytes so rows render as
    ///     populated (proves we count rows, not -EMPTY- placeholders).
    /// </summary>
    static ROM MakeRom(byte countByte)
    {
        // 16 MB minimum: ROM.LoadLow only assigns the FE8U RomInfo when
        // data.Length >= 0x1000000 (16 MB). SummonTableBase 0x200000 + 256*20
        // bytes fits comfortably, and addresses stay < 0x02000000 for
        // U.isSafetyOffset.
        var bytes = new byte[0x01000000];

        // Pointer at summons_demon_king_pointer -> SummonTableBase.
        WriteU32(bytes, (int)PointerAddr, SummonTableBase | 0x08000000u);

        // Count byte.
        bytes[(int)CountAddr] = countByte;

        // Fill enough summon entries with a non-zero unit id at offset 0 so the
        // viewer renders populated rows (and so a hypothetical 21-row fabrication
        // would still find valid-looking data, making the count-cap the only thing
        // that limits row count).
        for (uint i = 0; i < 0x120; i++)
        {
            int row = (int)(SummonTableBase + i * EntrySize);
            bytes[row + 0] = 0x01;   // unit id (1-based) -> populated
            bytes[row + 1] = 0x02;   // class id
        }

        var rom = new ROM();
        rom.LoadLow("synth-fe8u-1424.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
