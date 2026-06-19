// SPDX-License-Identifier: GPL-3.0-or-later
// Parity / regression tests for FE8SpellMenuExtendsView (issue #1167).
//
// Mirrors the SkillAssignmentUnitSkillSystemParityTests pattern: plant the
// SkillSystems202201 spell-menu signature into a synthetic FE8U ROM, then drive
// the ViewModel through master/detail/expand round-trips. Marked
// [Collection("SharedState")] because the ViewModel reads CoreState.ROM.
using System;
using System.IO;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class FE8SpellMenuExtendsParityTests
{
    static readonly byte[] SpellsGetter202201 = new byte[]
    {
        0x9E, 0x42, 0x04, 0xDA, 0x02, 0x34, 0xEF, 0xE7,
        0x00, 0x9A, 0x9A, 0x42, 0xFA, 0xD1, 0x01, 0x9B,
        0x01, 0x33, 0x03, 0xD1, 0x63, 0x78, 0x2B, 0x70,
        0x01, 0x35, 0xF3, 0xE7, 0x60, 0x78, 0xFF, 0xF7,
        0xBB, 0xFF, 0x01, 0x9B, 0x98, 0x42, 0xED, 0xD1,
        0xF4, 0xE7, 0xC0, 0x46,
    };

    const uint SigPos = 0xB10000;
    const uint UnitTableBase = 0xC00000;
    const uint ListBase = 0xC10000;

    static ROM MakeEmptyFE8URom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        data[0x6E0] = 0xFF;
        rom.LoadLow("synth-empty-fe8spell.gba", data, "BE8E01");
        return rom;
    }

    static ROM MakeScratchRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
        rom.LoadLow("synth-scratch-fe8spell.gba", data, "BE8E01");
        return rom;
    }

    static void WriteBytes(ROM rom, uint addr, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            rom.write_u8(addr + (uint)i, bytes[i]);
    }

    static void WriteU32(ROM rom, uint addr, uint value)
    {
        rom.write_u8(addr + 0, (byte)(value & 0xFF));
        rom.write_u8(addr + 1, (byte)((value >> 8) & 0xFF));
        rom.write_u8(addr + 2, (byte)((value >> 16) & 0xFF));
        rom.write_u8(addr + 3, (byte)((value >> 24) & 0xFF));
    }

    static uint PlantPatch(ROM rom)
    {
        WriteBytes(rom, SigPos, SpellsGetter202201);
        uint slot = SigPos + (uint)SpellsGetter202201.Length;
        WriteU32(rom, slot, UnitTableBase | 0x08000000u);
        return slot;
    }

    static void PlantUnitList(ROM rom, uint unitId, uint listBase, (byte b0, byte b1)[] entries)
    {
        uint slot = UnitTableBase + unitId * 4;
        WriteU32(rom, slot, listBase | 0x08000000u);
        uint cursor = listBase;
        foreach (var (b0, b1) in entries)
        {
            rom.write_u8(cursor + 0, b0);
            rom.write_u8(cursor + 1, b1);
            cursor += 2;
        }
        rom.write_u16(cursor, 0x0000);
    }

    // -----------------------------------------------------------------
    // ViewModel — empty ROM yields empty list (proves stub placeholder gone)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_EmptyRom_ReturnsEmpty()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeEmptyFE8URom();
            var vm = new FE8SpellMenuExtendsViewModel();
            var items = vm.LoadList();
            // No patch planted -> empty (NOT the single synthetic "Spell Menu
            // Extensions" placeholder row the stub used to return).
            Assert.Empty(items);
            Assert.Equal(0u, vm.ReadStartAddress);
            Assert.Equal(0u, vm.ReadCount);
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — planted ROM enumerates units
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PlantedRom_EnumeratesUnits()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            var items = vm.LoadList();

            Assert.True(items.Count > 0, "Expected at least one unit row");
            Assert.Equal(UnitTableBase, vm.ReadStartAddress);
            Assert.True(vm.ReadCount > 0);
            // First row addr is the unit-0 slot (UnitTableBase + 0).
            Assert.Equal(UnitTableBase, items[0].addr);
            // Master block size is the per-unit u32 pointer slot (4 bytes).
            Assert.Equal(4u, vm.MasterBlockSize);
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — LoadEntry reads the per-unit pointer + N1 list (B0/B1 split)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_ReadsN1ListAndSplitsB0()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            // Unit 5: promoted level 10 (0x8A) learns item 0x11; level 20 (0x14) item 0x12.
            PlantUnitList(rom, 5, ListBase, new (byte, byte)[] { (0x8A, 0x11), (0x14, 0x12) });
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            vm.LoadList();

            uint addr = UnitTableBase + 5 * 4;
            vm.LoadEntry(addr);
            Assert.Equal(5u, vm.SelectedUnitId);
            Assert.False(vm.IsZeroPointer);
            Assert.Equal(2, vm.SpellEntries.Count);

            // First entry: B0 0x8A -> level 10 + promoted, B1 0x11.
            vm.LoadN1Entry(vm.SpellEntries[0].Addr);
            Assert.Equal(10u, vm.N1Level);
            Assert.True(vm.N1Promoted);
            Assert.Equal(0x11u, vm.N1SpellId);

            // Second entry: B0 0x14 -> level 20 + unpromoted, B1 0x12.
            vm.LoadN1Entry(vm.SpellEntries[1].Addr);
            Assert.Equal(20u, vm.N1Level);
            Assert.False(vm.N1Promoted);
            Assert.Equal(0x12u, vm.N1SpellId);
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — WriteN1 round-trips MakeB0 (level|promoted) + B1
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteN1_RoundTripsMakeB0()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            PlantUnitList(rom, 1, ListBase, new (byte, byte)[] { (0x05, 0x10) });
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            vm.LoadList();
            vm.LoadEntry(UnitTableBase + 1 * 4);
            vm.LoadN1Entry(vm.SpellEntries[0].Addr);

            // Set promoted level 7 (-> 0x87), spell 0x22, write back.
            vm.N1Level = 7;
            vm.N1Promoted = true;
            vm.N1SpellId = 0x22;
            vm.WriteN1();

            Assert.Equal((byte)0x87, rom.u8(ListBase + 0));
            Assert.Equal((byte)0x22, rom.u8(ListBase + 1));
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — WriteMaster repoints the unit pointer slot
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteMaster_RepointsUnitSlot()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            PlantUnitList(rom, 2, ListBase, new (byte, byte)[] { (0x05, 0x10) });
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            vm.LoadList();
            uint addr = UnitTableBase + 2 * 4;
            vm.LoadEntry(addr);

            uint newOffset = 0xC20000u;
            vm.UnitListPointer = newOffset | 0x08000000u;
            vm.WriteMaster();

            Assert.Equal(newOffset, rom.p32(addr));
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — ExpandN1List re-terminates with 0x0000 and repoints
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExpandN1List_ReTerminatesWithZero()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            PlantUnitList(rom, 4, ListBase, new (byte, byte)[] { (0x05, 0x10), (0x0A, 0x11) });
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            vm.LoadList();
            uint addr = UnitTableBase + 4 * 4;
            vm.LoadEntry(addr);
            Assert.Equal(2, vm.SpellEntries.Count);

            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "expand",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            bool ok = vm.ExpandN1List(4, ud);
            Assert.True(ok);
            // After expand: 4 entries + re-terminated list (the first two preserved).
            Assert.Equal(4, vm.SpellEntries.Count);

            uint newBase = U.toOffset(vm.UnitListPointer);
            Assert.Equal((byte)0x05, rom.u8(newBase + 0));
            Assert.Equal((byte)0x10, rom.u8(newBase + 1));
            Assert.Equal(0x0000u, rom.u16(newBase + 8)); // terminator after 4 entries
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // ViewModel — IsZeroPointer true when the unit slot is null/unset
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_IsZeroPointer_WhenUnitSlotUnset()
    {
        var prev = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            PlantPatch(rom);
            // Unit 7's slot left as 0xFFFFFFFF (unsafe pointer) -> IsZeroPointer.
            CoreState.ROM = rom;

            var vm = new FE8SpellMenuExtendsViewModel();
            vm.LoadList();
            vm.LoadEntry(UnitTableBase + 7 * 4);
            Assert.True(vm.IsZeroPointer);
            Assert.Empty(vm.SpellEntries);
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // View — static source assertions
    // -----------------------------------------------------------------

    [Fact]
    public void View_MasterWriteHandler_WrapsInUndoScope()
    {
        string src = File.ReadAllText(ViewCsPath());
        Assert.Contains("_undoService.Begin(\"Edit Spell Menu Extensions Unit Pointer\")", src);
        Assert.Contains("_undoService.Commit()", src);
        Assert.Contains("_undoService.Rollback()", src);
    }

    [Fact]
    public void View_N1WriteHandler_WrapsInUndoScope()
    {
        string src = File.ReadAllText(ViewCsPath());
        Assert.Contains("_undoService.Begin(\"Edit Spell Menu Extensions Entry\")", src);
    }

    [Fact]
    public void View_ExpandHandler_WrapsInUndoScope()
    {
        string src = File.ReadAllText(ViewCsPath());
        Assert.Contains("_undoService.Begin(\"Expand Spell Menu Extensions List\")", src);
    }

    [Fact]
    public void View_HasMasterAndN1WriteButtons_Wired()
    {
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_Write_Button\"", axaml);
        Assert.Contains("Click=\"MasterWriteButton_Click\"", axaml);
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_N1Write_Button\"", axaml);
        Assert.Contains("Click=\"N1WriteButton_Click\"", axaml);
    }

    [Fact]
    public void View_HasN1EntryListAndExpand()
    {
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_N1Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_N1ListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_SpellIdField_IsHexTextBox_NotHexNud()
    {
        // The spell-id field MUST be a TextBox (parsed via U.atoh), NOT a
        // NumericUpDown with a hex FormatString — that combination throws a
        // FormatException the AvaloniaEditorTests gate catches.
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("Name=\"N1SpellIdBox\"", axaml);
        // No hex FormatString anywhere (X2/X02/X8 etc.) on this view.
        Assert.DoesNotContain("FormatString=\"X", axaml);
    }

    [Fact]
    public void View_PromotedCheckBox_Present()
    {
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_N1Promoted_Check\"", axaml);
    }

    [Fact]
    public void View_N1Label_IsSpell_NotSkill()
    {
        // Copilot #2: B1 is the item/spell id; WF labels it 魔法 (spell), so the
        // Avalonia label must be "Spell" (not "Skill", which would conflate it
        // with the SkillSystems skill editors).
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_N1Spell_Label\"", axaml);
        Assert.Matches(@"FE8SpellMenuExtends_N1Spell_Label[\s\S]{0,160}Content=""Spell""", axaml);
        Assert.DoesNotContain("Content=\"Skill\"", axaml);
    }

    [Fact]
    public void View_HasEditableListPointerField_AndMasterWriteReadsIt()
    {
        // Copilot #3: master Write must actually repoint the per-unit list base
        // (WF N1_ReadStartAddress). The View needs an editable hex field, and
        // MasterWriteButton_Click must read it before WriteMaster.
        string axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("AutomationId=\"FE8SpellMenuExtends_ListPointer_Input\"", axaml);
        string src = File.ReadAllText(ViewCsPath());
        Assert.Contains("_vm.UnitListPointer = U.atoh(ListPointerBox.Text", src);
    }

    [Fact]
    public void ListParityHelper_ClassifiesAsContextDependent()
    {
        // The editor is patch-dependent (empty on vanilla FE8U), same shape as
        // the SkillAssignment* skill editors. It must be context-dependent, NOT
        // a no-list tool/dialog.
        Assert.True(ListParityHelper.IsContextDependentEditor("FE8SpellMenuExtendsView"));
        Assert.False(ListParityHelper.IsNoListEditor("FE8SpellMenuExtendsView"));
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string ViewCsPath() => Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
        "FE8SpellMenuExtendsView.axaml.cs");

    static string AxamlPath() => Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
        "FE8SpellMenuExtendsView.axaml");

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
