// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for SkillAssignmentUnitSkillSystemView. (#995)
//
// Mirrors the pattern SkillAssignmentClassSkillSystemParityTests established.
// Marked [Collection("SharedState")] because ViewModel tests mutate CoreState.ROM.
using System;
using System.IO;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class SkillAssignmentUnitSkillSystemParityTests
{
    // Synthetic-ROM planting helpers (same byte patterns as SkillSystemPatchScannerTests).
    static readonly byte[] AssignSig1 = new byte[]
    {
        0x01,0x35,0x02,0x36,0xF1,0xE7,0x00,0x20,
        0x28,0x70,0x29,0x1C,0x02,0x48,0x09,0x1A,
    };

    static readonly byte[] LevelUpSig2 = new byte[]
    {
        0x0A,0xD0,0x1A,0x78,0x00,0x2A,0x07,0xD0,
        0x8A,0x42,0x01,0xD0,0x02,0x33,0xF8,0xE7,
        0x5A,0x78,0x22,0x70,0x01,0x34,0xF9,0xE7,
        0x00,0x20,0x20,0x70,0x31,0xBC,0x70,0x47,
    };

    const uint PlantedUnitBase   = 0x80000;
    const uint PlantedLevelUpBase = 0x90000;

    /// <summary>
    /// Build a tiny FE8U-shaped ROM with no SkillSystems signatures planted.
    /// </summary>
    static ROM MakeEmptyFE8URom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        data[0x6E0] = 0xFF; // mask_pointer sentinel
        rom.LoadLow("synth-empty-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>
    /// Build an FE8U-shaped ROM with all 0xFF (so unset pointers stay invalid)
    /// and a size of 16 MiB (enough for the scanner's 0xB00000-0xC00000 range).
    /// </summary>
    static ROM MakeScratchRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];
        for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
        rom.LoadLow("synth-scratch.gba", data, "BE8E01");
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

    /// <summary>
    /// Plant an ASSIGN sig at patternPos and write a GBA pointer to unitBase
    /// at extraSkip=0 past the pattern. Returns the pointer location address.
    /// </summary>
    static uint PlantAssignSig(ROM rom, uint patternPos, uint unitBase)
    {
        WriteBytes(rom, patternPos, AssignSig1);
        // FindAssignPersonalSkillPointerLocation = ASSIGN + extraSkip 0
        // location = patternPos + AssignSig1.Length + sig.Skip(16) + extraSkip(0)
        uint pointerLoc = patternPos + (uint)AssignSig1.Length + 16 + 0;
        WriteU32(rom, pointerLoc, unitBase | 0x08000000u);
        return pointerLoc;
    }

    /// <summary>
    /// Plant a LEVELUP sig (LevelUpSig2, Skip=0) at patternPos and write a GBA pointer
    /// to levelUpBase at extraSkip=4 past the pattern. Returns the pointer location.
    /// The three u32s at levelUpBase must all be null/safe.
    /// </summary>
    static uint PlantLevelUpSig(ROM rom, uint patternPos, uint levelUpBase)
    {
        WriteBytes(rom, patternPos, LevelUpSig2);
        // FindAssignUnitLevelUpSkillPointerLocation = LEVELUP + extraSkip 4
        // location = patternPos + LevelUpSig2.Length + sig.Skip(0) + extraSkip(4)
        uint pointerLoc = patternPos + (uint)LevelUpSig2.Length + 0 + 4;
        WriteU32(rom, pointerLoc, levelUpBase | 0x08000000u);
        // Post-pointer validation: first 3 u32s at levelUpBase must be safe/null
        WriteU32(rom, levelUpBase + 0, 0u);
        WriteU32(rom, levelUpBase + 4, 0u);
        WriteU32(rom, levelUpBase + 8, 0u);
        return pointerLoc;
    }

    // -----------------------------------------------------------------
    // ViewModel — empty ROM yields empty list (proves stub placeholder gone)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_EmptyRom_ReturnsEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeEmptyFE8URom();
            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            var items = vm.LoadList();
            // No SkillSystems pattern planted -> must be empty (not a single placeholder).
            Assert.Empty(items);
            Assert.Equal(0u, vm.ReadStartAddress);
            Assert.Equal(0u, vm.ReadCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — planted ROM enumerates units
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PlantedRom_EnumeratesUnits()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            // Write some safe data at PlantedUnitBase so isSafetyOffset passes
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            var items = vm.LoadList();

            // FE8U has unit_maxcount = 255; we should get at least 1 entry
            Assert.True(items.Count > 0, "Expected at least one unit row");
            // Count equals unit_maxcount (bounded by rom safety)
            Assert.Equal(PlantedUnitBase, vm.ReadStartAddress);
            Assert.True(vm.ReadCount > 0);
            // First row address should be PlantedUnitBase + 0
            Assert.Equal(PlantedUnitBase, items[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — LoadEntry + WriteMaster round-trip (UnitSkill u8)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntryWriteMaster_RoundTrip()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();

            // Select unit 2
            uint unitId = 2;
            uint addr = PlantedUnitBase + unitId;
            rom.write_u8(addr, 0x05);
            vm.LoadEntry(addr);
            Assert.Equal(0x05u, vm.UnitSkill);

            // Mutate and write back
            vm.UnitSkill = 0x42;
            vm.WriteMaster();

            Assert.Equal(0x42u, (uint)rom.u8(PlantedUnitBase + unitId));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Copilot review #1: WriteMaster repoints level-up slot via write_p32
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteMaster_RepointsLevelUpSlot()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();

            uint unitId = 3;
            uint addr = PlantedUnitBase + unitId;
            vm.LoadEntry(addr);

            // Set a new target GBA pointer
            uint targetOffset = 0xA0000u;
            uint targetGbaPtr = targetOffset | 0x08000000u;
            vm.XLevelUpAddr = targetGbaPtr;
            vm.WriteMaster();

            // The level-up slot at AssignLevelUpBase + unitId*4 should hold write_p32 result.
            // write_p32(slot, offset) stores the GBA pointer form of offset.
            uint levelUpSlot = PlantedLevelUpBase + unitId * 4;
            uint written = rom.p32(levelUpSlot); // p32 reads back as offset (U.toOffset)
            Assert.Equal(targetOffset, written);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Copilot review #2: old patch (ASSIGN but no LEVELUP) — master loads, N1 hidden
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_OldPatch_NoLevelUp_MasterStillLoads_N1Hidden()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            // Do NOT plant a LEVELUP signature
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            var items = vm.LoadList();

            // Master list populated
            Assert.True(items.Count > 0, "Master list should enumerate units");

            // LEVELUP base should be 0 (not found)
            Assert.Equal(0u, vm.AssignLevelUpBaseAddress);
            // The N1-group visibility flag must be false (drives the View hide).
            Assert.False(vm.HasLevelUpTable, "HasLevelUpTable must be false when LEVELUP is absent.");

            // LoadEntry must not throw and must NOT populate LevelUpEntries
            uint addr = PlantedUnitBase + 1;
            Exception loadEx = Record.Exception(() => vm.LoadEntry(addr));
            Assert.Null(loadEx);
            Assert.Empty(vm.LevelUpEntries);
            // Still false after LoadEntry recompute.
            Assert.False(vm.HasLevelUpTable);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // HasLevelUpTable == true when the LEVELUP table is present
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasLevelUpTable_TrueWhenLevelUpPresent()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();

            Assert.NotEqual(0u, vm.AssignLevelUpBaseAddress);
            Assert.True(vm.HasLevelUpTable, "HasLevelUpTable must be true when LEVELUP resolves.");

            // Still true after a LoadEntry.
            vm.LoadEntry(PlantedUnitBase + 1);
            Assert.True(vm.HasLevelUpTable);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Unit name 1-based mapping (row 0x01 == Eirika sentinel, 0x00 == empty)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_UsesOneBasedNameMapping_SentinelRowEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            var items = vm.LoadList();

            // Row 0x00 is the empty WF sentinel (uid 0 -> ""); its label must be
            // just "0x00" with NO trailing space and NO resolved name.
            Assert.True(items.Count > 1);
            Assert.Equal("0x00", items[0].name);

            // Row 0x01 (uid 1) resolves via GetUnitNameByOneBasedId(1) — same as
            // WF UnitForm.GetUnitName(1). On a synthetic ROM with no text data the
            // resolver returns a non-empty fallback string, so the label has a name
            // segment (never the off-by-one "0x01 <name of unit 2>").
            string expectedName = FEBuilderGBA.NameResolver.GetUnitNameByOneBasedId(1);
            string expectedLabel = string.IsNullOrEmpty(expectedName)
                ? "0x01"
                : "0x01 " + expectedName;
            Assert.Equal(expectedLabel, items[1].name);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — planted LEVELUP table loads and WriteLevelUp round-trips
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LevelUpList_LoadsAndWrites()
    {
        var prevRom = CoreState.ROM;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);
            CoreState.ROM = rom;

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();

            // Set up a per-unit level-up list for unit 1
            uint unitId = 1;
            uint unitLevelUpSlot = PlantedLevelUpBase + unitId * 4;
            const uint listBase = 0xC0000u;
            // Write 2 entries + terminator
            WriteU32(rom, unitLevelUpSlot, listBase | 0x08000000u);
            rom.write_u8(listBase + 0, 0x05); // level
            rom.write_u8(listBase + 1, 0x02); // skill
            rom.write_u8(listBase + 2, 0x0A); // level
            rom.write_u8(listBase + 3, 0x03); // skill
            rom.write_u16(listBase + 4, 0x0000); // terminator

            uint addr = PlantedUnitBase + unitId;
            vm.LoadEntry(addr);

            // N1 sub-list should have 2 entries
            Assert.Equal(2, vm.LevelUpEntries.Count);

            // Select first entry and write-back
            vm.LoadLevelUpEntry(listBase + 0);
            Assert.Equal(0x05u, vm.LevelUpRaw);
            Assert.Equal(0x02u, vm.LevelUpSkill);

            vm.LevelUpRaw = 0x10;
            vm.LevelUpSkill = 0x07;
            vm.WriteLevelUp();

            Assert.Equal(0x10u, (uint)rom.u8(listBase + 0));
            Assert.Equal(0x07u, (uint)rom.u8(listBase + 1));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #1604 ViewModel functional — N1 list expand (grow), SINGLE-slot
    // repoint, and byte-identical rollback.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExpandLevelUpList_Grows_RepointsOnlyThisUnit_RollbackByteIdentical()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            ROM rom = MakeScratchRom(); // all 0xFF == all free space
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);

            // unit 1 and unit 2 SHARE the same 1-row level-up table.
            const uint unitA = 1, unitB = 2;
            const uint sharedBase = 0xC0000u;
            WriteU32(rom, PlantedLevelUpBase + unitA * 4, sharedBase | 0x08000000u);
            WriteU32(rom, PlantedLevelUpBase + unitB * 4, sharedBase | 0x08000000u);
            rom.write_u8(sharedBase + 0, 0x05); // row 0: lv
            rom.write_u8(sharedBase + 1, 0x02); // row 0: skill
            rom.write_u16(sharedBase + 2, 0x0000); // terminator

            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();
            vm.LoadEntry(PlantedUnitBase + unitA);
            Assert.Equal(1, vm.LevelUpEntries.Count);

            uint slotA = PlantedLevelUpBase + unitA * 4;
            uint slotB = PlantedLevelUpBase + unitB * 4;
            uint origGbaPtr = rom.u32(slotA);

            var undodata = CoreState.Undo.NewUndoData("unit expand test");
            SkillAssignmentUnitSkillSystemViewModel.LevelUpExpandResult result;
            using (ROM.BeginUndoScope(undodata))
            {
                result = vm.ExpandLevelUpList();
            }
            Assert.True(result.Success, result.Error);

            // Grew to 2 visible entries.
            Assert.Equal(2, vm.LevelUpEntries.Count);
            // SLOT A repointed to the new clone; SLOT B untouched (single-slot).
            Assert.NotEqual(origGbaPtr, rom.u32(slotA));
            Assert.Equal(origGbaPtr, rom.u32(slotB));

            // Rollback restores slot A byte-identically.
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();
            Assert.Equal(origGbaPtr, rom.u32(slotA));
            Assert.Equal(origGbaPtr, rom.u32(slotB));
        }
        finally { CoreState.ROM = prevRom; CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void ViewModel_ExpandLevelUpList_AllocatesOnNullPointer()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);

            // unit 5 has a NULL level-up pointer.
            const uint unitId = 5;
            WriteU32(rom, PlantedLevelUpBase + unitId * 4, 0u);

            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();
            vm.LoadEntry(PlantedUnitBase + unitId);
            Assert.Empty(vm.LevelUpEntries);

            var undodata = CoreState.Undo.NewUndoData("unit alloc test");
            SkillAssignmentUnitSkillSystemViewModel.LevelUpExpandResult result;
            using (ROM.BeginUndoScope(undodata))
            {
                result = vm.ExpandLevelUpList();
            }
            Assert.True(result.Success, result.Error);
            Assert.NotEqual(0u, result.NewBaseAddress);
            // Pointer now non-null and the list has exactly 1 row.
            Assert.NotEqual(0u, rom.u32(PlantedLevelUpBase + unitId * 4));
            Assert.Equal(1, vm.LevelUpEntries.Count);
        }
        finally { CoreState.ROM = prevRom; CoreState.Undo = prevUndo; }
    }

    // -----------------------------------------------------------------
    // #1604 ViewModel functional — Make-Independent clones + repoints ONLY
    // this unit, leaving a SHARED sibling unit UNTOUCHED.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_MakeIndependent_RepointsOnlyThisUnit_SiblingUntouched()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);

            const uint unitA = 1, unitB = 2;
            const uint sharedBase = 0xC0000u;
            WriteU32(rom, PlantedLevelUpBase + unitA * 4, sharedBase | 0x08000000u);
            WriteU32(rom, PlantedLevelUpBase + unitB * 4, sharedBase | 0x08000000u);
            rom.write_u8(sharedBase + 0, 0x05);
            rom.write_u8(sharedBase + 1, 0x22);
            rom.write_u8(sharedBase + 2, 0x0A);
            rom.write_u8(sharedBase + 3, 0x33);
            rom.write_u16(sharedBase + 4, 0x0000);

            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();
            vm.LoadEntry(PlantedUnitBase + unitA);

            uint slotA = PlantedLevelUpBase + unitA * 4;
            uint slotB = PlantedLevelUpBase + unitB * 4;
            uint sharedGbaPtr = rom.u32(slotA);
            // The table is shared, so the panel must be visible.
            Assert.True(vm.IsIndependenceVisible);
            Assert.False(vm.IsSelectedLevelUpListEmpty());

            var undodata = CoreState.Undo.NewUndoData("unit independence test");
            uint newPointer;
            using (ROM.BeginUndoScope(undodata))
            {
                newPointer = vm.MakeIndependent(undodata);
            }
            Assert.NotEqual(0u, newPointer);

            // Slot A moved to the clone; slot B untouched (the inverse of an
            // all-reference repoint).
            Assert.Equal(newPointer, rom.u32(slotA));
            Assert.NotEqual(sharedGbaPtr, rom.u32(slotA));
            Assert.Equal(sharedGbaPtr, rom.u32(slotB));

            // The clone is byte-verbatim (rows + terminator).
            uint cloneBase = newPointer & 0x01FFFFFFu;
            Assert.Equal((byte)0x05, rom.u8(cloneBase + 0));
            Assert.Equal((byte)0x22, rom.u8(cloneBase + 1));
            Assert.Equal((byte)0x0A, rom.u8(cloneBase + 2));
            Assert.Equal((byte)0x33, rom.u8(cloneBase + 3));
        }
        finally { CoreState.ROM = prevRom; CoreState.Undo = prevUndo; }
    }

    // -----------------------------------------------------------------
    // #1604 ViewModel functional — bulk TSV Export/Import round-trip.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_TsvExportImport_RoundTrip()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            ROM rom = MakeScratchRom();
            rom.write_u8(PlantedUnitBase, 0x00);
            PlantAssignSig(rom, 0xB10000, PlantedUnitBase);
            PlantLevelUpSig(rom, 0xB20000, PlantedLevelUpBase);

            // Give a couple of units distinct B0 skill bytes + one level-up table.
            rom.write_u8(PlantedUnitBase + 1, 0x10);
            rom.write_u8(PlantedUnitBase + 2, 0x11);
            const uint listBase = 0xC0000u;
            WriteU32(rom, PlantedLevelUpBase + 1 * 4, listBase | 0x08000000u);
            rom.write_u8(listBase + 0, 0x05);
            rom.write_u8(listBase + 1, 0x02);
            rom.write_u16(listBase + 2, 0x0000);

            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SkillAssignmentUnitSkillSystemViewModel();
            vm.LoadList();

            string path = Path.GetTempFileName();
            try
            {
                Assert.True(vm.ExportAllData(path));
                string[] lines = File.ReadAllLines(path);
                Assert.True(lines.Length > 2);
                Assert.Equal("10", lines[1].Split('\t')[0]);

                // Mutate the ROM, then re-import the exported TSV and confirm the
                // original B0 byte + level-up row are restored.
                rom.write_u8(PlantedUnitBase + 1, 0x77);
                rom.write_u8(listBase + 0, 0x44);
                Assert.True(vm.ImportAllData(path));

                Assert.Equal((byte)0x10, rom.u8(PlantedUnitBase + 1));
                Assert.Equal((byte)0x05, rom.u8(listBase + 0));
                Assert.Equal((byte)0x02, rom.u8(listBase + 1));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally { CoreState.ROM = prevRom; CoreState.Undo = prevUndo; }
    }

    // -----------------------------------------------------------------
    // View — static source assertions
    // -----------------------------------------------------------------

    [Fact]
    public void View_MasterWriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Edit Skill Assignment (Unit)\")", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_HasN1WriteButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_N1Write_Button\"", axaml);
        Assert.Contains("Click=\"N1WriteButton_Click\"", axaml);
    }

    [Fact]
    public void View_N1WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitSkillSystemView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(\"Edit Skill Assignment Unit Level-up Entry\")", source);
    }

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact]
    public void View_HasMasterWriteButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_Write_Button\"", axaml);
        Assert.Contains("Click=\"MasterWriteButton_Click\"", axaml);
    }

    [Fact]
    public void View_HasZeroPointerPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"ZeroPointerPanel\"", axaml);
    }

    [Fact]
    public void View_HasN1EntryList()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_N1Entry_List\"", axaml);
    }

    [Fact]
    public void View_N1LevelUpGroup_BindsVisibilityToHasLevelUpTable()
    {
        // The entire N1 level-up group must hide on old patches without the
        // unit-based level-up table. Assert the wrapping group binds IsVisible
        // to the VM's HasLevelUpTable flag so the hide can't silently regress.
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"LevelUpGroup\"", axaml);
        Assert.Contains("IsVisible=\"{Binding HasLevelUpTable}\"", axaml);
    }

    // -----------------------------------------------------------------
    // #1604: the 3 new features (parity with the sibling Class editor) —
    // N1 list-expand, Make-Independent, bulk TSV Export/Import — must be
    // wired (mirror SkillAssignmentClassSkillSystemParityTests).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasN1ListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_N1ListExpand_Button\"", axaml);
        Assert.Contains("Click=\"N1ListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasIndependenceButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_Independence_Button\"", axaml);
        Assert.Contains("Click=\"Independence_Click\"", axaml);
    }

    [Fact]
    public void View_HasIndependencePanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"IndependencePanel\"", axaml);
    }

    [Fact]
    public void View_HasBulkImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_BulkImport_Button\"", axaml);
        Assert.Contains("Click=\"BulkImport_Click\"", axaml);
    }

    [Fact]
    public void View_HasBulkExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillAssignmentUnitSkillSystem_BulkExport_Button\"", axaml);
        Assert.Contains("Click=\"BulkExport_Click\"", axaml);
    }

    [Fact]
    public void View_N1ListExpandHandler_WrapsInUndoScope()
    {
        string source = ReadCodeBehind();
        Assert.Contains("void N1ListExpand_Click", source);
        Assert.Contains("_undoService.Begin(\"Expand Skill Assignment Unit Level-up Table\")", source);
        Assert.Contains("_vm.ExpandLevelUpList()", source);
    }

    [Fact]
    public void View_IndependenceHandler_WrapsInUndoScope()
    {
        string source = ReadCodeBehind();
        Assert.Contains("void Independence_Click", source);
        Assert.Contains("_undoService.Begin(\"Make Skill Assignment Unit Independent\")", source);
        Assert.Contains("_vm.MakeIndependent(", source);
    }

    [Fact]
    public void View_BulkImportHandler_WrapsInUndoScope()
    {
        string source = ReadCodeBehind();
        Assert.Contains("void BulkImport_Click", source);
        Assert.Contains("_undoService.Begin(\"Bulk Import Skill Assignment (Unit) data\")", source);
        Assert.Contains("_vm.ImportAllData(", source);
    }

    [Fact]
    public void ListParityHelper_DeclaresWfAvFormPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("SkillAssignmentUnitSkillSystemView"),
            "ListParityHelper.KnownExtraCrossViewMappings must declare SkillAssignmentUnitSkillSystemView");
        Assert.Equal("SkillAssignmentUnitSkillSystemForm", extras["SkillAssignmentUnitSkillSystemView"]);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitSkillSystemView.axaml");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string ReadCodeBehind()
    {
        string repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillAssignmentUnitSkillSystemView.axaml.cs"));
    }

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
