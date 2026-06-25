using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless tests for <see cref="UnitCustomBattleAnimeViewModel"/> (#1412 RELEASE-BLOCKER).
///
/// The FE7 custom-battle-anime data is a TWO-level pointer table:
///   level 1 = <c>p32(unit_custom_battle_anime_pointer)</c> → a POINTER TABLE (one u32 per class);
///   level 2 = <c>p32(slot)</c> → an inner weapon-anime list (4-byte records B0/B1/W2, u32==0 terminator).
///
/// The bug: the old flat-list VM did a single dereference and wrote B0/B1/W2 back over the pointer-table
/// slots, silently corrupting the per-class anime pointers. These tests pin the two-level structure and
/// prove a Write mutates ONLY an inner record and NEVER any pointer-table slot.
/// </summary>
[Collection("SharedState")]
public class UnitCustomBattleAnimeViewModelTests
{
    // FE7J pointer-table base + two inner lists, all inside a 0x1000000 ROM.
    const uint TableBase = 0x200000;   // pointer table (one u32 per class)
    const uint Inner0Base = 0x210000;  // class 0's weapon-anime list
    const uint Inner1Base = 0x211000;  // class 1's weapon-anime list

    static void W16(byte[] d, uint off, ushort v)
    {
        d[off + 0] = (byte)(v & 0xFF);
        d[off + 1] = (byte)((v >> 8) & 0xFF);
    }

    static void W32(byte[] d, uint off, uint v)
    {
        d[off + 0] = (byte)(v & 0xFF);
        d[off + 1] = (byte)((v >> 8) & 0xFF);
        d[off + 2] = (byte)((v >> 16) & 0xFF);
        d[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    /// <summary>
    /// Synthetic FE7J ROM. <c>AE7J01</c> sets the FE7J RomInfo
    /// (<c>unit_custom_battle_anime_pointer = 0x530CC</c>, <c>version = 7</c>). The pointer slot points at
    /// <see cref="TableBase"/>; the table has two slots → two inner lists, then a 0 terminator.
    /// inner0 holds 3 records, inner1 holds 1 record.
    /// </summary>
    static ROM MakeSyntheticFE7J(bool shareInner = false)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "AE7J01");
        var d = rom.Data;
        var info = rom.RomInfo;

        // Level 1: the pointer slot → the pointer table.
        W32(d, info.unit_custom_battle_anime_pointer, 0x08000000u | TableBase);

        // The pointer TABLE: slot 0 → inner0, slot 1 → inner0 (shared) or inner1, slot 2 = 0 (terminator).
        W32(d, TableBase + 0, 0x08000000u | Inner0Base);
        W32(d, TableBase + 4, 0x08000000u | (shareInner ? Inner0Base : Inner1Base));
        W32(d, TableBase + 8, 0); // not a pointer → N2 rule terminates the table at count 2

        // Level 2: inner weapon-anime lists (4-byte records: B0/B1/W2, u32==0 terminator).
        W32(d, Inner0Base + 0, 0x11111111);
        W32(d, Inner0Base + 4, 0x22222222);
        W32(d, Inner0Base + 8, 0x33333333);
        W32(d, Inner0Base + 12, 0); // terminator
        W32(d, Inner1Base + 0, 0x44444444);
        W32(d, Inner1Base + 4, 0); // terminator

        return rom;
    }

    static uint Slot(int i) => TableBase + (uint)(i * 4);

    // ===================================================================
    // 1) Top list IS the pointer table (the corruption root cause)
    // ===================================================================

    [Fact]
    public void LoadPointerTable_RowsAreTableSlots_NotInnerRecords()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeSyntheticFE7J();
            var vm = new UnitCustomBattleAnimeViewModel();
            List<AddrResult> table = vm.LoadPointerTable();

            // Two slots counted (slot 0 always, slot 1 is a pointer, slot 2 = 0 terminates).
            Assert.Equal(2, table.Count);
            Assert.Equal(Slot(0), table[0].addr);
            Assert.Equal(Slot(1), table[1].addr);
            // Each row is a table SLOT, NOT an inner record.
            Assert.NotEqual(Inner0Base, table[0].addr);
            // The IDataVerifiable count helper reports the pointer-table count.
            Assert.Equal(2, vm.GetListCount());
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void LoadPointerTable_FirstSlotCounted_EvenWhenNotPointer()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J();
            // Make slot 0 a non-pointer; WinForms N2 rule i==0 ? true counts it anyway, then slot 1
            // (also non-pointer here) terminates at count 1.
            W32(rom.Data, TableBase + 0, 0x00000005);
            W32(rom.Data, TableBase + 4, 0x00000006);
            CoreState.ROM = rom;

            var vm = new UnitCustomBattleAnimeViewModel();
            Assert.Equal(1, vm.LoadPointerTable().Count);
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 2) Inner list from the SECOND dereference
    // ===================================================================

    [Fact]
    public void LoadInnerList_DerefsSlot_AndDecodesFields()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeSyntheticFE7J();
            var vm = new UnitCustomBattleAnimeViewModel();

            List<AddrResult> inner0 = vm.LoadInnerList(Slot(0));
            Assert.Equal(3, inner0.Count);
            Assert.Equal(Inner0Base, inner0[0].addr);
            Assert.Equal(Inner0Base + 4, inner0[1].addr);
            Assert.Equal(Inner0Base + 8, inner0[2].addr);
            Assert.Equal(Inner0Base, vm.InnerBase);

            // Decode B0/B1/W2 of record 0 = 0x11111111 → B0=0x11, W2=0x1111.
            vm.LoadEntry(inner0[0].addr);
            Assert.True(vm.IsLoaded);
            Assert.Equal(0x11u, vm.WeaponType);
            Assert.Equal(0x11u, vm.Special);
            Assert.Equal(0x1111u, vm.AnimeNumber);

            // Slot 1 → inner1 = a single record.
            List<AddrResult> inner1 = vm.LoadInnerList(Slot(1));
            Assert.Single(inner1);
            Assert.Equal(Inner1Base, inner1[0].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 3) Write targets the inner record — NEVER a table slot (regression)
    // ===================================================================

    [Fact]
    public void WriteEntry_MutatesInnerRecord_LeavesPointerTableByteIdentical()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J();
            CoreState.ROM = rom;

            // Snapshot every pointer-table slot BEFORE the edit.
            uint[] tableBefore = { rom.u32(Slot(0)), rom.u32(Slot(1)), rom.u32(Slot(2)) };

            var vm = new UnitCustomBattleAnimeViewModel();
            vm.LoadInnerList(Slot(0));
            vm.LoadEntry(Inner0Base + 4); // record 1 (0x22222222)

            vm.WeaponType = 0xAB;
            vm.Special = 0xCD;
            vm.AnimeNumber = 0xBEEF;
            vm.WriteEntry();

            // The inner record changed exactly as edited.
            Assert.Equal(0xABu, rom.u8(Inner0Base + 4 + 0));
            Assert.Equal(0xCDu, rom.u8(Inner0Base + 4 + 1));
            Assert.Equal(0xBEEFu, rom.u16(Inner0Base + 4 + 2));

            // Every pointer-table slot is byte-identical — NO corruption.
            Assert.Equal(tableBefore[0], rom.u32(Slot(0)));
            Assert.Equal(tableBefore[1], rom.u32(Slot(1)));
            Assert.Equal(tableBefore[2], rom.u32(Slot(2)));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void WriteEntry_RefusesToWriteTableSlot()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J();
            CoreState.ROM = rom;
            uint slotBefore = rom.u32(Slot(0));

            var vm = new UnitCustomBattleAnimeViewModel();
            vm.LoadInnerList(Slot(0));
            // Force CurrentAddr to a TABLE slot (the old corruption path) and attempt a write.
            vm.CurrentAddr = Slot(0);
            vm.WeaponType = 0xFF;
            vm.Special = 0xFF;
            vm.AnimeNumber = 0xFFFF;
            vm.WriteEntry();

            // The table slot is untouched — the IsInnerAddress guard blocked the write.
            Assert.Equal(slotBefore, rom.u32(Slot(0)));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void LoadEntry_OnTableSlot_DoesNotMarkLoaded()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeSyntheticFE7J();
            var vm = new UnitCustomBattleAnimeViewModel();
            vm.LoadInnerList(Slot(0));
            vm.LoadEntry(Slot(1)); // a table slot, not an inner record
            Assert.False(vm.IsLoaded);
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 4) Shared inner list — a write still never touches any pointer slot
    // ===================================================================

    [Fact]
    public void SharedInnerList_WriteMutatesInnerBytesOnly()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J(shareInner: true); // slot 0 and slot 1 both → inner0
            CoreState.ROM = rom;
            uint[] tableBefore = { rom.u32(Slot(0)), rom.u32(Slot(1)) };

            var vm = new UnitCustomBattleAnimeViewModel();
            vm.LoadInnerList(Slot(1)); // reached via the SECOND slot
            vm.LoadEntry(Inner0Base);  // shared inner record
            vm.WeaponType = 0x77;
            vm.WriteEntry();

            Assert.Equal(0x77u, rom.u8(Inner0Base));
            // Both pointer-table slots that share the inner list are byte-identical.
            Assert.Equal(tableBefore[0], rom.u32(Slot(0)));
            Assert.Equal(tableBefore[1], rom.u32(Slot(1)));
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 5) FindOwningSlot resolves an inner address to its owning table slot
    // ===================================================================

    [Fact]
    public void FindOwningSlot_ResolvesInnerAddress()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeSyntheticFE7J();
            var vm = new UnitCustomBattleAnimeViewModel();

            Assert.Equal(Slot(0), vm.FindOwningSlot(Inner0Base + 8)); // last record of inner0
            Assert.Equal(Slot(1), vm.FindOwningSlot(Inner1Base));     // inner1's only record
            Assert.Equal(0u, vm.FindOwningSlot(0x999999));            // not owned by any slot
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 6) Guards: FE6/FE8 short-circuit, near-EOF, parity helper agreement
    // ===================================================================

    [Fact]
    public void LoadPointerTable_NonFE7_ReturnsEmpty()
    {
        var prev = CoreState.ROM;
        try
        {
            // FE8U has unit_custom_battle_anime_pointer == 0 → empty, no throw.
            var rom = new ROM();
            rom.LoadLow("fe8.gba", new byte[0x1000000], "BE8E01");
            CoreState.ROM = rom;

            var vm = new UnitCustomBattleAnimeViewModel();
            Assert.Empty(vm.LoadPointerTable());
            Assert.Equal(0, vm.GetListCount());
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void LoadInnerList_ZeroOrUnsafeSlot_ReturnsEmpty_NoThrow()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeSyntheticFE7J();
            var vm = new UnitCustomBattleAnimeViewModel();
            Assert.Empty(vm.LoadInnerList(0));
            Assert.Equal(0u, vm.InnerBase);
            // Slot whose p32 is 0 → unsafe base → empty.
            Assert.Empty(vm.LoadInnerList(Slot(2)));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void LoadPointerTable_MatchesListParityHelper()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J();
            CoreState.ROM = rom;
            var vm = new UnitCustomBattleAnimeViewModel();

            List<AddrResult> vmList = vm.LoadPointerTable();
            // ListParityHelper drives generic parity tooling — both must agree the list IS the
            // pointer table (same slot addresses, same count) after the #1412 N2-rule fix.
            List<AddrResult> goldList = ListParityHelper.BuildReferenceList("UnitCustomBattleAnimeView");
            Assert.NotNull(goldList);

            Assert.Equal(goldList.Count, vmList.Count);
            for (int i = 0; i < goldList.Count; i++)
                Assert.Equal(goldList[i].addr, vmList[i].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // 7) View wiring — ClassList → EntryList → LoadEntry drills two levels
    // ===================================================================

    [AvaloniaFact]
    public void View_ClassSelect_PopulatesInnerList_AndEditWritesInnerOnly()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeSyntheticFE7J();
            CoreState.ROM = rom;
            uint[] tableBefore = { rom.u32(Slot(0)), rom.u32(Slot(1)) };

            var view = new UnitCustomBattleAnimeView();
            view.Show();
            try
            {
                var classList = view.FindControl<AddressListControl>("ClassList");
                var entryList = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(classList);
                Assert.NotNull(entryList);

                // ClassList is the pointer table (2 slots).
                Assert.Equal(2, classList!.ItemCount);

                // Selecting class 0 drills into inner0 (3 records).
                classList.SelectByIndex(0);
                Assert.Equal(3, entryList!.ItemCount);

                // Selecting an inner record loads it into the editor.
                entryList.SelectByIndex(1); // inner0 record 1 (0x22222222)
                var vm = view.DataViewModel as UnitCustomBattleAnimeViewModel;
                Assert.NotNull(vm);
                Assert.True(vm!.IsLoaded);
                Assert.Equal(Inner0Base + 4, vm.CurrentAddr);

                // Edit + write via the VM (the view's Write_Click path) mutates the inner record only.
                vm.WeaponType = 0x5A;
                vm.WriteEntry();
                Assert.Equal(0x5Au, rom.u8(Inner0Base + 4));
                Assert.Equal(tableBefore[0], rom.u32(Slot(0)));
                Assert.Equal(tableBefore[1], rom.u32(Slot(1)));
            }
            finally { view.Close(); }
        }
        finally { CoreState.ROM = prev; }
    }
}
