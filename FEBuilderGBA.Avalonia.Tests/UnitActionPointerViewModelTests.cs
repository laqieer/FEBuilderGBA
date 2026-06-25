using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="UnitActionPointerViewModel"/> (issue #1173 —
/// port of WinForms <c>UnitActionPointerForm</c>). The table is the per-unit action/behavior
/// function-pointer table at <c>p32(RomInfo.unitaction_function_pointer)</c>, 4 bytes per entry
/// (a single GBA pointer). The editor exposes that pointer (P0) for read/write plus a resolved
/// action-name label.
/// </summary>
[Collection("SharedState")]
public class UnitActionPointerViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public UnitActionPointerViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries_FourBytesApart()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + 4, list[i].addr);
        }
        // Count helper agrees with the list it builds.
        Assert.Equal(list.Count, vm.GetListCount());
    }

    [Fact]
    public void LoadEntry_ReadsFunctionPointer_AndActionId()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        // P0 is the dereferenced function pointer (0x08 prefix stripped).
        Assert.Equal(CoreState.ROM.p32(addr), vm.P0);
        // First row is action id 1 (WinForms non-rework ids start at 1).
        Assert.Equal(1u, vm.ActionId);
    }

    [Fact]
    public void WriteEntry_RoundTripsFunctionPointer()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.P0;

        try
        {
            vm.P0 = 0x1234;
            vm.WriteEntry();

            var vm2 = new UnitActionPointerViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x1234u, vm2.P0);
        }
        finally
        {
            vm.P0 = orig;
            vm.WriteEntry();
        }
    }

    // -----------------------------------------------------------------
    // #1415: the VM list must MATCH the master ListParityHelper list
    // (same addresses + count). Both now route base/predicate/id through
    // UnitActionPointerCore, so a future drift in either path is caught.
    // -----------------------------------------------------------------

    [Fact]
    public void LoadList_MatchesListParityHelper_Addresses()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        List<AddrResult> vmList = vm.LoadList();
        List<AddrResult> refList = ListParityHelper.BuildReferenceList("UnitActionPointerView");

        Assert.Equal(refList.Count, vmList.Count);
        Assert.Equal(
            refList.Select(r => r.addr).ToArray(),
            vmList.Select(r => r.addr).ToArray());
    }

    // -----------------------------------------------------------------
    // #1415: rework-aware behavior. On a UnitActionRework-patched ROM the
    // editor must (a) resolve the RELOCATED table base from ApplyAction.bin,
    // (b) use 0-based action ids, and (c) treat a==0 / masked pointers as
    // valid rows. We build a synthetic FE8U ROM, plant the rework gate +
    // a staged ApplyAction.bin signature, and assert the VM + ListParity
    // both list the relocated table 0-based.
    // -----------------------------------------------------------------

    [Fact]
    public void Rework_RelocatesTableBase_UsesZeroBasedIds_AcceptsNullEntry()
    {
        var savedRom = CoreState.ROM;
        var savedBase = CoreState.BaseDirectory;
        string tempBase = Path.Combine(Path.GetTempPath(), "fe1415av_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rom = MakeReworkedFe8u(tempBase, out uint relocatedTable);
            CoreState.ROM = rom;
            CoreState.BaseDirectory = tempBase;

            Assert.True(UnitActionPointerCore.IsRework(rom));
            Assert.Equal(relocatedTable, UnitActionPointerCore.ResolveBaseAddress(rom));

            var vm = new UnitActionPointerViewModel();
            List<AddrResult> list = vm.LoadList();

            // Two valid rows planted (NULL entry + masked pointer), then a 0xFFFFFFFF terminator.
            Assert.Equal(2, list.Count);
            Assert.Equal(relocatedTable, list[0].addr);          // first row is the relocated table base
            Assert.Equal(relocatedTable + 4, list[1].addr);

            // Rework ids are 0-based: first label shows id 0x00.
            Assert.StartsWith(U.ToHexString(0u), list[0].name);

            // LoadEntry on the first (NULL) row: id 0 is VALID in rework, P0 reads 0.
            vm.LoadEntry(relocatedTable);
            Assert.True(vm.IsLoaded);
            Assert.Equal(0u, vm.ActionId);
            Assert.Equal(0u, vm.P0);
            Assert.False(string.IsNullOrEmpty(vm.ActionName)); // id 0 must still resolve a name

            // VM list must still match the master ListParityHelper list on the patched ROM.
            List<AddrResult> refList = ListParityHelper.BuildReferenceList("UnitActionPointerView");
            Assert.Equal(refList.Select(r => r.addr).ToArray(), list.Select(r => r.addr).ToArray());
        }
        finally
        {
            CoreState.ROM = savedRom;
            CoreState.BaseDirectory = savedBase;
            try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true); } catch { }
        }
    }

    [Fact]
    public void ListParityHelper_UnitActionPointerView_StillRegistered()
    {
        var map = ListParityHelper.GetMapping("UnitActionPointerView");
        Assert.NotNull(map);
        Assert.Equal("UnitActionPointerForm", map!.Value.FormType);
    }

    /// <summary>
    /// Build a synthetic UnitActionRework-patched FE8U ROM: plant the rework gate, stage a temp
    /// <c>ApplyAction.bin</c> under <paramref name="tempBase"/>, plant the same signature in the ROM
    /// (block-4 aligned, ≥ hint) with the relocated table SLOT right after it, and fill the relocated
    /// table with a NULL row + a masked-pointer row + a 0xFFFFFFFF terminator.
    /// </summary>
    static ROM MakeReworkedFe8u(string tempBase, out uint relocatedTable)
    {
        var rom = new ROM();
        var data = new byte[0x200_0000];
        bool ok = rom.LoadLow("fake.gba", data, "BE8E01");
        Assert.True(ok);

        // Rework gate ON.
        uint expected;
        uint hackAddr = rom.RomInfo.patch_unitaction_rework_hack(out expected);
        rom.write_u32(hackAddr, expected);

        // Staged ApplyAction.bin signature.
        byte[] sig = { 0xC0, 0xDE, 0xCA, 0xFE, 0x11, 0x22, 0x33, 0x44 };
        string asmDir = Path.Combine(tempBase, "config", "patch2",
            rom.RomInfo.VersionToFilename, "UnitActionRework", "UnitActionRework", "asm");
        Directory.CreateDirectory(asmDir);
        File.WriteAllBytes(Path.Combine(asmDir, "ApplyAction.bin"), sig);

        uint uafp = rom.RomInfo.unitaction_function_pointer; // FE8U: 0x3205C
        uint sigOffset = 0x40000;                            // > hint, (sigOffset - hint) % 4 == 0
        for (uint k = 0; k < sig.Length; k++) rom.write_u8(sigOffset + k, sig[k]);

        uint slotAddr = sigOffset + (uint)sig.Length;
        relocatedTable = 0x500000;
        rom.write_u32(slotAddr, relocatedTable | 0x08000000u);

        // Relocated table: NULL row (valid in rework), masked-pointer row, terminator.
        rom.write_u32(relocatedTable + 0, 0x00000000);
        rom.write_u32(relocatedTable + 4, 0x18120000); // & 0x0FFFFFFF = 0x08120000 -> safe
        rom.write_u32(relocatedTable + 8, U.NOT_FOUND);
        return rom;
    }
}
