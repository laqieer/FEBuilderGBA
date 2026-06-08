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

            // LoadEntry must not throw and must NOT populate LevelUpEntries
            uint addr = PlantedUnitBase + 1;
            Exception loadEx = Record.Exception(() => vm.LoadEntry(addr));
            Assert.Null(loadEx);
            Assert.Empty(vm.LevelUpEntries);
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
    public void View_NoDisabledStubButtons()
    {
        // The Unit form must NOT have expand/independence/TSV stub buttons (disabled or otherwise).
        // Read-only display fields (NumericUpDown address/blocksize boxes) may carry IsEnabled="False",
        // but there must be no disabled Button elements (no stub buttons).
        string axaml = ReadAxaml();
        // No expand button
        Assert.DoesNotContain("ListExpand", axaml);
        // No independence button
        Assert.DoesNotContain("Independence", axaml);
        // No TSV / bulk-import / bulk-export buttons
        Assert.DoesNotContain("BulkImport", axaml);
        Assert.DoesNotContain("BulkExport", axaml);
        // No "Pending Core extraction" or "#500" stale markers
        Assert.DoesNotContain("Pending Core extraction", axaml);
        Assert.DoesNotContain("tracked by #500", axaml);
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
