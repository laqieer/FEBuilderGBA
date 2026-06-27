// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1597 — the SkillSystems / Venno patched-ROM
// Item Stat Bonuses editors.
//
// Before the fix, ItemStatBonusesSkillSystemsViewModel.LoadList() and
// ItemStatBonusesVennoViewModel.LoadList() returned a single synthetic
// AddrResult(0, "...") placeholder instead of walking the item table.
// Nothing was editable/writable: Write() early-returned on CurrentAddr==0
// and the parity harness skipped both editors (ContextDependentEditors).
//
// These hermetic synthetic-ROM tests prove the fix: each VM now walks the
// item table (deref each item's +12 stat-booster pointer), yields real rows,
// reads the booster block, and persists writes (Venno preserving its
// non-editable +0x0F padding byte). The parity helper now maps both editors
// and no longer classifies them as context-dependent.
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ItemStatBonusesPatchedParityTests : System.IDisposable
{
    // The item table base and the stat-booster block base used by the
    // synthetic ROMs. Distinct regions so a stray write to one is visible.
    const uint TableAddr = 0x200000;
    const uint BlockAddr = 0x210000;

    // Snapshot/restore the shared CoreState.ROM (xUnit news up one instance
    // per test). Without this, later SharedState-collection parity tests see
    // our throwaway synthetic ROM and fail.
    readonly ROM? _prevRom = CoreState.ROM;

    public void Dispose()
    {
        CoreState.ROM = _prevRom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>
    /// Build a synthetic FE8U ROM with an item table at <see cref="TableAddr"/>
    /// and exactly ONE valid item (index 0) whose +12 stat-booster pointer
    /// targets a planted booster <paramref name="block"/> at
    /// <see cref="BlockAddr"/>. Item index 1's +12 is set to a non-pointer,
    /// non-null sentinel (0x00000001) so isPointerOrNULL fails and LoadList
    /// breaks after the first item — giving a deterministic count of 1.
    /// </summary>
    static ROM MakeRomWithOneStatBoosterItem(byte[] block)
    {
        var rom = new ROM();
        // FE8U needs 16 MB to register as BE8E01.
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // RomInfo.item_pointer is the ROM OFFSET of the pointer SLOT (set by
        // LoadLow). Write our table's GBA pointer INTO that slot.
        uint itemPtrSlot = rom.RomInfo.item_pointer;
        WriteU32(rom.Data, (int)itemPtrSlot, 0x08000000u | TableAddr);

        uint dataSize = rom.RomInfo.item_datasize; // 36 on FE8U

        // Plant the booster block.
        for (int i = 0; i < block.Length; i++)
            rom.Data[BlockAddr + i] = block[i];

        // Item index 0: +12 -> GBA pointer to the booster block. +16 stays 0
        // (null) which is a valid pointer-or-null.
        WriteU32(rom.Data, (int)(TableAddr + 0 * dataSize + 12), 0x08000000u | BlockAddr);

        // Item index 1: +12 = 0x00000001 — neither a valid pointer nor null,
        // so isPointerOrNULL is false and the table walk breaks here.
        WriteU32(rom.Data, (int)(TableAddr + 1 * dataSize + 12), 0x00000001u);

        return rom;
    }

    // ------------------------------------------------------------------
    // SkillSystems VM
    // ------------------------------------------------------------------

    [Fact]
    public void SkillSystems_LoadList_WalksItemTable_YieldsRealRows()
    {
        CoreState.ROM = MakeRomWithOneStatBoosterItem(new byte[20]);
        var vm = new ItemStatBonusesSkillSystemsViewModel();

        var rows = vm.LoadList();

        Assert.Single(rows);
        Assert.Equal(BlockAddr, rows[0].addr);
        Assert.NotEqual(0u, rows[0].addr);
    }

    [Fact]
    public void SkillSystems_LoadEntry_ReadsBlock()
    {
        // HP=5 @ +0, Str=-3 @ +1 (signed bytes).
        var block = new byte[20];
        block[0] = 5;
        block[1] = unchecked((byte)(-3));
        CoreState.ROM = MakeRomWithOneStatBoosterItem(block);

        var vm = new ItemStatBonusesSkillSystemsViewModel();
        vm.LoadEntry(BlockAddr);

        Assert.Equal(BlockAddr, vm.CurrentAddr);
        Assert.Equal(5, vm.HP);
        Assert.Equal(-3, vm.Str);
    }

    [Fact]
    public void SkillSystems_Write_PersistsToBlock()
    {
        var rom = MakeRomWithOneStatBoosterItem(new byte[20]);
        CoreState.ROM = rom;

        var vm = new ItemStatBonusesSkillSystemsViewModel();
        vm.LoadEntry(BlockAddr);
        vm.HP = 7;     // +0
        vm.Def = -9;   // +4 (per GetFieldOffsetMap)
        vm.Write();

        Assert.Equal(7u, rom.u8(BlockAddr + 0));
        Assert.Equal(-9, (sbyte)rom.u8(BlockAddr + 4));
    }

    // ------------------------------------------------------------------
    // Venno VM
    // ------------------------------------------------------------------

    [Fact]
    public void Venno_LoadList_WalksItemTable_YieldsRealRows()
    {
        CoreState.ROM = MakeRomWithOneStatBoosterItem(new byte[16]);
        var vm = new ItemStatBonusesVennoViewModel();

        var rows = vm.LoadList();

        Assert.Single(rows);
        Assert.Equal(BlockAddr, rows[0].addr);
        Assert.NotEqual(0u, rows[0].addr);
    }

    [Fact]
    public void Venno_Write_PreservesPaddingByteAt0x0F()
    {
        var block = new byte[16];
        block[15] = 0xAB; // sentinel at +0x0F — Venno edits only bytes 0..14.
        var rom = MakeRomWithOneStatBoosterItem(block);
        CoreState.ROM = rom;

        var vm = new ItemStatBonusesVennoViewModel();
        vm.LoadEntry(BlockAddr);
        vm.HP = 3; // +0
        vm.Write();

        // +0x0F untouched (non-editable padding) and HP persisted.
        Assert.Equal(0xABu, rom.u8(BlockAddr + 15));
        Assert.Equal(3u, rom.u8(BlockAddr + 0));
    }

    // ------------------------------------------------------------------
    // Truncated-ROM guard (#1597 Copilot review): the 4-byte p32(item_pointer)
    // read is bounds-checked so a ROM whose item_pointer slot falls within the
    // last 3 bytes returns an empty list instead of throwing IndexOutOfRange.
    //
    // Hermetic exercise of the branch: LoadLow a full FE8U image (gives a valid
    // RomInfo whose item_pointer is a real deep offset ~0x16xxx), then swap in a
    // tiny Data array so item_pointer + 4 > Data.Length. SwapNewROMDataDirect
    // replaces only Data and leaves RomInfo intact, so the guard fires.
    // ------------------------------------------------------------------

    static ROM MakeRomWithTruncatedItemPointerSlot()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        // item_pointer is deep in the image (well past 16 bytes); shrink Data so
        // the full 4-byte read at the slot would run past EOF.
        Assert.True(rom.RomInfo.item_pointer + 4 > 16,
            "Test premise: FE8U item_pointer must be past the truncated length.");
        rom.SwapNewROMDataDirect(new byte[16]);
        return rom;
    }

    [Fact]
    public void SkillSystems_LoadList_TruncatedItemPointerSlot_ReturnsEmpty_NoThrow()
    {
        CoreState.ROM = MakeRomWithTruncatedItemPointerSlot();
        var vm = new ItemStatBonusesSkillSystemsViewModel();

        var rows = vm.LoadList(); // must NOT throw IndexOutOfRange
        Assert.Empty(rows);
    }

    [Fact]
    public void Venno_LoadList_TruncatedItemPointerSlot_ReturnsEmpty_NoThrow()
    {
        CoreState.ROM = MakeRomWithTruncatedItemPointerSlot();
        var vm = new ItemStatBonusesVennoViewModel();

        var rows = vm.LoadList(); // must NOT throw IndexOutOfRange
        Assert.Empty(rows);
    }

    // ------------------------------------------------------------------
    // Parity helper classification
    // ------------------------------------------------------------------

    [Fact]
    public void ParityHelper_PatchedViews_AreMappedAndNotContextDependent()
    {
        Assert.True(ListParityHelper.HasMapping("ItemStatBonusesSkillSystemsView"));
        Assert.True(ListParityHelper.HasMapping("ItemStatBonusesVennoView"));
        Assert.False(ListParityHelper.IsContextDependentEditor("ItemStatBonusesSkillSystemsView"));
        Assert.False(ListParityHelper.IsContextDependentEditor("ItemStatBonusesVennoView"));
    }
}
