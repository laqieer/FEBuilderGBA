// SPDX-License-Identifier: GPL-3.0-or-later
// #1441 — Event Function Pointer editor (Avalonia, FE8 variant) was missing the
// Worldmap (event_function_pointer_table2) filter, so the FE8 worldmap
// event-function pointer table was unreachable in the GUI.
//
// The WinForms ground truth (EventFunctionPointerForm.cs) exposes a FilterCombo
// toggling between table1 (primary) and table2 (worldmap), with a +0x80 id
// offset on the worldmap rows, an isPointer && IsValueOdd Thumb-pointer entry
// validity check, and a metadata-only FE8 gate (table2_pointer != 0).
//
// These headless tests drive EventFunctionPointerViewModel against synthetic
// ROMs that plant BOTH pointer tables and prove:
//   * FilterIndex 0 reads table1; FilterIndex 1 reads table2 (worldmap).
//   * Worldmap rows carry the +0x80 id in BOTH AddrResult.tag and the label.
//   * IsWorldmapAvailable is true on FE8 (metadata slot nonzero), false on
//     FE7/FE6 (slot == 0) — gated on metadata, NOT on table-base validity.
//   * The scan terminates (break) on the first non-pointer / even pointer, so a
//     valid pointer following a bad one is NOT exposed.
//   * The View wires the Primary/Worldmap filter combo + reload.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM.

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EventFunctionPointerWorldmapFilterTests : IDisposable
{
    const uint Table1Base = 0x00900000u; // primary event-function pointer table
    const uint Table2Base = 0x00A00000u; // worldmap (table2) event-function pointer table

    // Thumb (odd) code pointers — valid entries.
    const uint T1Ptr0 = 0x08123457u;
    const uint T1Ptr1 = 0x08123459u;
    const uint T2Ptr0 = 0x08222223u;
    const uint T2Ptr1 = 0x08222225u;

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public EventFunctionPointerWorldmapFilterTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    // ================================================================
    // FE8 — primary vs worldmap (table2) selection + +0x80 offset
    // ================================================================

    [Fact]
    public void Primary_ReadsTable1_FE8()
    {
        ROM rom = MakeFe8RomWithBothTables();
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel { FilterIndex = 0 };
        var list = vm.LoadList();

        Assert.Equal(2, list.Count);
        // Primary rows: addr = table1 base, ids 0..1, no offset.
        Assert.Equal(Table1Base + 0, list[0].addr);
        Assert.Equal(Table1Base + 4, list[1].addr);
        Assert.Equal(0u, list[0].tag);
        Assert.Equal(1u, list[1].tag);
        Assert.StartsWith("0x00 ", list[0].name);
        Assert.StartsWith("0x01 ", list[1].name);
    }

    [Fact]
    public void Worldmap_ReadsTable2_WithPlus0x80Offset_FE8()
    {
        ROM rom = MakeFe8RomWithBothTables();
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel { FilterIndex = 1 };
        var list = vm.LoadList();

        Assert.Equal(2, list.Count);
        // Worldmap rows: addr = table2 base, but the displayed/tagged id is
        // 0x80 + i (WinForms parity).
        Assert.Equal(Table2Base + 0, list[0].addr);
        Assert.Equal(Table2Base + 4, list[1].addr);
        Assert.Equal(0x80u, list[0].tag);
        Assert.Equal(0x81u, list[1].tag);
        Assert.StartsWith("0x80 ", list[0].name);
        Assert.StartsWith("0x81 ", list[1].name);
    }

    [Fact]
    public void LoadEntry_UsesAbsoluteAddress_AcrossFilters_FE8()
    {
        ROM rom = MakeFe8RomWithBothTables();
        CoreState.ROM = rom;

        // Worldmap row's absolute address still loads the raw pointer value
        // (LoadEntry/Write are filter-agnostic — only the list base changes).
        var vm = new EventFunctionPointerViewModel { FilterIndex = 1 };
        var list = vm.LoadList();
        vm.LoadEntry(list[1].addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(T2Ptr1, vm.EventCommandFunctionPointer);
    }

    // ================================================================
    // Metadata gate: IsWorldmapAvailable
    // ================================================================

    [Fact]
    public void IsWorldmapAvailable_True_FE8()
    {
        CoreState.ROM = MakeFe8RomWithBothTables();
        var vm = new EventFunctionPointerViewModel();
        Assert.True(vm.IsWorldmapAvailable);
    }

    [Fact]
    public void IsWorldmapAvailable_False_FE7()
    {
        // FE7 has event_function_pointer_table2_pointer == 0.
        var rom = MakeBareRom("AE7E01");
        Assert.Equal(0u, rom.RomInfo.event_function_pointer_table2_pointer);
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel();
        Assert.False(vm.IsWorldmapAvailable);
    }

    [Fact]
    public void IsWorldmapAvailable_False_FE6()
    {
        var rom = MakeBareRom("AFEJ01");
        Assert.Equal(0u, rom.RomInfo.event_function_pointer_table2_pointer);
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel();
        Assert.False(vm.IsWorldmapAvailable);
    }

    [Fact]
    public void IsWorldmapAvailable_True_EvenWhenTable2BaseInvalid_FE8()
    {
        // Gate is on the METADATA slot, not the pointed-table validity. A
        // corrupt/zeroed table2 base must still expose the option (LoadList then
        // yields an empty list) — WinForms parity.
        ROM rom = MakeFe8RomWithBothTables();
        // Wipe table2 base pointer to 0 (invalid).
        PlantU32(rom.Data, rom.RomInfo.event_function_pointer_table2_pointer, 0u);
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel { FilterIndex = 1 };
        Assert.True(vm.IsWorldmapAvailable);
        Assert.Empty(vm.LoadList());
    }

    // ================================================================
    // Scan terminator semantics: break on first invalid entry
    // ================================================================

    [Fact]
    public void Scan_BreaksOnEvenPointer_DoesNotExposeLaterValidRow_FE8()
    {
        // table1: [valid, EVEN(non-Thumb), valid] — the scan must stop at the
        // even pointer; the trailing valid pointer is NOT exposed.
        ROM rom = MakeFe8RomWithBothTables();
        PlantU32(rom.Data, Table1Base + 0, 0x08123457u); // valid (odd)
        PlantU32(rom.Data, Table1Base + 4, 0x08123456u); // EVEN → terminator
        PlantU32(rom.Data, Table1Base + 8, 0x08123459u); // valid (odd) but unreachable
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel { FilterIndex = 0 };
        var list = vm.LoadList();

        Assert.Single(list);
        Assert.Equal(Table1Base + 0, list[0].addr);
    }

    [Fact]
    public void Scan_BreaksOnNonPointer_FE8()
    {
        ROM rom = MakeFe8RomWithBothTables();
        PlantU32(rom.Data, Table1Base + 0, 0x08123457u); // valid
        PlantU32(rom.Data, Table1Base + 4, 0x00000000u); // non-pointer → terminator
        PlantU32(rom.Data, Table1Base + 8, 0x08123459u); // unreachable
        CoreState.ROM = rom;

        var vm = new EventFunctionPointerViewModel { FilterIndex = 0 };
        Assert.Single(vm.LoadList());
    }

    // ================================================================
    // View source-wiring guard
    // ================================================================

    [Fact]
    public void View_WiresFilterComboAndReload()
    {
        string repoRoot = FindRepoRoot();
        string axaml = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "EventFunctionPointerView.axaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "EventFunctionPointerView.axaml.cs"));

        // The combo exists in the view ...
        Assert.Contains("EventFunctionPointer_Filter_Combo", axaml);
        Assert.Contains("Name=\"FilterComboBox\"", axaml);
        // ... and the code-behind populates + reloads on change.
        Assert.Contains("FilterComboBox.SelectionChanged", code);
        Assert.Contains("IsWorldmapAvailable", code);
        Assert.Contains("_vm.FilterIndex", code);
    }

    // ================================================================
    // Helpers
    // ================================================================

    static ROM MakeFe8RomWithBothTables()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synth-BE8E01-1441.gba", bytes, "BE8E01");

        uint t1Loc = rom.RomInfo.event_function_pointer_table_pointer;
        uint t2Loc = rom.RomInfo.event_function_pointer_table2_pointer;
        Assert.NotEqual(0u, t1Loc);
        Assert.NotEqual(0u, t2Loc); // FE8 has table2

        // Point the metadata slots at the synthetic table bases (GBA pointers).
        PlantU32(bytes, t1Loc, Table1Base | 0x08000000u);
        PlantU32(bytes, t2Loc, Table2Base | 0x08000000u);

        // table1: two valid pointers + terminator.
        PlantU32(bytes, Table1Base + 0, T1Ptr0);
        PlantU32(bytes, Table1Base + 4, T1Ptr1);
        PlantU32(bytes, Table1Base + 8, 0x00000000u);

        // table2 (worldmap): two valid pointers + terminator.
        PlantU32(bytes, Table2Base + 0, T2Ptr0);
        PlantU32(bytes, Table2Base + 4, T2Ptr1);
        PlantU32(bytes, Table2Base + 8, 0x00000000u);

        return rom;
    }

    static ROM MakeBareRom(string header)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow($"synth-{header}-1441.gba", bytes, header);
        return rom;
    }

    static void PlantU32(byte[] bytes, uint addr, uint value)
    {
        int idx = (int)addr;
        bytes[idx + 0] = (byte)(value & 0xFF);
        bytes[idx + 1] = (byte)((value >> 8) & 0xFF);
        bytes[idx + 2] = (byte)((value >> 16) & 0xFF);
        bytes[idx + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return AppContext.BaseDirectory;
    }
}
