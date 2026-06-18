using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Issue #1175 — tests for the SkillSystems "特効リワーク" effectiveness editor's
/// right-pane behaviour (effective-against class-type list, shared-items list,
/// per-entry write round-trip). The Rework on-ROM format is 4-byte entries with
/// a u16 class-type, distinct from the classic single-byte ItemEffectivenessForm,
/// so it is backed by the ItemClassListCore.*Rework* helpers.
///
/// The Rework patch is absent from a vanilla FE8U.gba, so the ROM-backed tests
/// only assert the VM stays safe (no crash, empty/valid output) against real
/// item metadata. The write/expand round-trip is exercised against a synthetic
/// in-memory ROM seeded with a valid rework array, driven through the VM exactly
/// as the view does.
/// </summary>
[Collection("SharedState")]
public class ItemEffectivenessSkillSystemsReworkEditTests
{
    private readonly ITestOutputHelper _output;

    public ItemEffectivenessSkillSystemsReworkEditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // -- ROM-backed safety (FE8U) --------------------------------------------

    [Fact]
    public void LoadInnerEntries_AndSharedOwners_DoNotThrow_OnFE8U()
    {
        RomTestHelper.WithRom("FE8U", () =>
        {
            var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
            var list = vm.LoadList();
            _output.WriteLine($"LoadList returned {list.Count} entries");
            foreach (var row in list)
            {
                vm.LoadEntry(row.addr);
                var inner = vm.LoadInnerEntries();   // must not throw
                var shared = vm.LoadSharedOwners();  // must not throw
                Assert.NotNull(inner);
                Assert.NotNull(shared);
            }
        });
    }

    [Fact]
    public void SetCurrentItemById_PinsOwnerByItemId_OnFE8U()
    {
        // Regression for the shared-array disambiguation (PR #1259 review): when
        // multiple items share one effectiveness array, picking a shared owner
        // must pin CurrentItemAddr to THAT item's struct, not the first owner's
        // (else Expand / Make-Independent rewrite the wrong item's +16 pointer).
        RomTestHelper.WithRom("FE8U", () =>
        {
            ROM rom = CoreState.ROM;
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;

            var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
            var list = vm.LoadList();

            foreach (var row in list)
            {
                vm.LoadEntry(row.addr);
                var owners = vm.LoadSharedOwners();
                if (owners.Count < 2) continue;

                foreach (var owner in owners)
                {
                    vm.SetCurrentItemById(owner.tag);
                    Assert.Equal(itemBase + owner.tag * dataSize, vm.CurrentItemAddr);
                }
                _output.WriteLine($"Disambiguation verified across {owners.Count} shared owners.");
                return; // one genuinely-shared array exercises the path
            }
            _output.WriteLine("No shared effectiveness arrays in this ROM — path not exercised.");
        });
    }

    // -- Synthetic write/expand/independence round-trip ----------------------

    static ROM MakeRom(byte[] data)
    {
        var rom = new ROM();
        typeof(ROM).GetProperty("Data")!.SetValue(rom, data);
        return rom;
    }

    static void WriteReworkEntryBytes(byte[] data, int off, byte coeff, ushort classType)
    {
        data[off + 0] = 0;
        data[off + 1] = coeff;
        data[off + 2] = (byte)(classType & 0xFF);
        data[off + 3] = (byte)((classType >> 8) & 0xFF);
    }

    [Fact]
    public void Vm_LoadInnerEntries_ReadsReworkArray_Synthetic()
    {
        // Synthetic ROM: a rework array of [armor, cavalry] + u32 terminator at
        // offset 256, with the outer-list "address" pointing straight at it.
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
        WriteReworkEntryBytes(data, 256, 6, 0x01); // armor
        WriteReworkEntryBytes(data, 260, 6, 0x02); // cavalry
        data[264] = data[265] = data[266] = data[267] = 0; // terminator

        var rom = MakeRom(data);
        var prevRom = CoreState.ROM;
        CoreState.ROM = rom;
        try
        {
            var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
            vm.LoadEntry(256);
            var inner = vm.LoadInnerEntries();
            Assert.Equal(2, inner.Count);
            // Each row's value (AddrResult.x) is the class-type bitmask.
            Assert.Equal(0x01u, inner[0].tag);
            Assert.Equal(0x02u, inner[1].tag);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void Vm_WriteCurrentEntry_RoundTrips_Synthetic()
    {
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
        WriteReworkEntryBytes(data, 256, 6, 0x01); // armor
        data[260] = data[261] = data[262] = data[263] = 0; // terminator

        var rom = MakeRom(data);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        try
        {
            var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
            vm.LoadEntry(256);
            var inner = vm.LoadInnerEntries();
            Assert.Single(inner);

            // Select the entry, change coefficient + class-type, write.
            vm.LoadEntryFields(inner[0].addr);
            vm.Coefficient = 4;
            vm.ClassType = 0x24; // flying + sword

            var undo = CoreState.Undo!.NewUndoData(this, "test write");
            vm.WriteCurrentEntry(undo);

            // Re-read straight off the ROM.
            Assert.Equal(0x00, rom.Data[256]); // leading byte preserved as 0
            Assert.Equal(0x04, rom.Data[257]); // coefficient
            Assert.Equal(0x24u, rom.u16(258)); // class-type

            // And via a fresh scan through the VM.
            var reread = vm.LoadInnerEntries();
            Assert.Single(reread);
            Assert.Equal(0x24u, reread[0].tag);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void Vm_ExpandCurrentList_AddsEntry_Synthetic()
    {
        byte[] data = new byte[2048];
        for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
        // Item record at offset 512 whose +16 effectiveness pointer → 256.
        // Build a minimal item table so FindItemsSharingPointer resolves the
        // owning item (needed for the pointer rewrite during expand).
        // We point item_pointer (via RomInfo) — but RomInfo is null on a
        // synthetic ROM, so instead drive expand by setting CurrentItemAddr
        // directly through LoadEntry's resolution is not possible. Use the
        // ItemClassListCore directly is covered in Core.Tests; here we verify
        // the VM forwards a known owner pointer.
        WriteReworkEntryBytes(data, 256, 6, 0x01); // armor
        data[260] = data[261] = data[262] = data[263] = 0; // terminator
        // Owner pointer slot at offset 16 of a fake item at 512 → 256.
        uint ptr = 256u | 0x08000000u;
        for (int i = 0; i < 4; i++) data[512 + 16 + i] = (byte)((ptr >> (i * 8)) & 0xFF);

        var rom = MakeRom(data);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        try
        {
            var vm = new ItemEffectivenessSkillSystemsReworkViewModel();
            vm.CurrentAddr = 256;
            vm.CurrentItemAddr = 512; // owner item (its +16 holds the pointer)

            var undo = CoreState.Undo!.NewUndoData(this, "test expand");
            uint newBase = vm.ExpandCurrentList(undo);

            Assert.NotEqual(256u, newBase);
            // Pointer at the item's +16 was repointed to the new array.
            Assert.Equal(newBase | 0x08000000u, rom.u32(512 + 16));

            var inner = vm.LoadInnerEntries();
            Assert.Equal(2, inner.Count); // armor + appended placeholder
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }
}
