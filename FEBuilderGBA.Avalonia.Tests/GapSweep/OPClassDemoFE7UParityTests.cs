// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for OPClassDemoFE7UView. (#421)
//
// Closes the 54 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `OPClassDemoFE7UForm` (HIGH density 26/56 == -53.6 %, 24 WF-only labels,
// 0 common labels). Each assertion maps to a concrete acceptance-criterion
// bullet in the issue body and to a Copilot CLI plan-review finding.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the OPClassDemoFE7U parity raise (#421) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class OPClassDemoFE7UParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 56 control instantiations. To leave the HIGH
    /// verdict we need AV >= ceil(56 * 0.75) = 42.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 56;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 42
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasSelectionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasMainEditPanel_AllSeventeenFields()
    {
        // All 17 main-entry fields must have a corresponding AutomationId
        // input. P0/P24 are pointer-aware (round-trip via rom.write_p32).
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_EnglishNamePtr_Input\"", axaml);     // P0
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_DescTextId_Input\"", axaml);          // W4
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_JpNamePtr_Input\"", axaml);           // W8 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_JpNameLen_Input\"", axaml);           // B10
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_ClassId_Input\"", axaml);             // B11
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_AllyEnemyColor_Input\"", axaml);      // B12
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_BattleAnime_Input\"", axaml);         // B13
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_MagicEffect_Input\"", axaml);         // B14
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown15_Input\"", axaml);           // B15 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown16_Input\"", axaml);           // B16 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown17_Input\"", axaml);           // B17 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown19_Input\"", axaml);           // B19 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_TerrainLeft_Input\"", axaml);         // B20
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_TerrainRight_Input\"", axaml);        // B21
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown22_Input\"", axaml);           // B22 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_Unknown23_Input\"", axaml);           // B23 (new)
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_AnimePtr_Input\"", axaml);            // P24
    }

    [Fact]
    public void View_HasN2SubList_AllControls()
    {
        // The N2 (animation command sequence) sub-list mirrors the WF
        // groupBox1: N2 selection bar (Address / BlockSize / SelectedAddress /
        // Write button), N2 ListBox, N2 Command combo, N2 B0/B1 numerics,
        // N2 Command/Wait labels.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_List\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_Command_Combo\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_Argument_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_CommandRaw_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_Write_Button\"", axaml);
        // Labels mirroring the WF "Command" header, "00", "/60 (秒)" labels:
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_CommandHeader_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoFE7U_N2_WaitUnit_Label\"", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 5 - Write/NWrite handlers must wrap ROM mutation in undo scope.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        // Roslyn-static read of the code-behind source - no Avalonia head
        // needed. Write_Click must open / commit / rollback an undo scope.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_NWriteHandler_WrapsInOwnUndoScope()
    {
        // The N2 Write handler must open a SEPARATE undo scope (different
        // scope name) from the main Write handler.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("NWrite_Click", source);
        Assert.Contains("Edit OP Class Demo Anime Command", source);
    }

    [Fact]
    public void View_WriteHandlers_RoundTripThroughViewModel()
    {
        // Neither handler should call rom.SetU* / rom.write_u* directly -
        // all ROM mutation must go through the ViewModel methods so the
        // EditorFormRef pointer-aware codec runs.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
        Assert.Contains("_vm.WriteEntry(", source);
        Assert.Contains("_vm.WriteN2Entry(", source);
    }

    // -----------------------------------------------------------------
    // ViewModel state - pointer-aware fields (Copilot plan review #1).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_FieldDef_UsesPointerForP0AndP24()
    {
        // P0 (English name pointer) and P24 (animation pointer) must round-
        // trip via rom.p32 / write_p32, not raw rom.write_u32. Without the
        // pointer-aware codec, writes would persist the offset form (no
        // 0x08000000 high bit) and corrupt the address (Copilot CLI plan-
        // review finding #1).
        var fields = EditorFormRef.DetectFields(
            new[] { "P0", "W4", "W8", "B10", "B11", "B12", "B13", "B14",
                    "B15", "B16", "B17", "B19", "B20", "B21", "B22", "B23", "P24" });
        var p0 = fields.First(f => f.Name == "P0");
        var p24 = fields.First(f => f.Name == "P24");
        Assert.Equal(EditorFormRef.FieldType.Pointer, p0.Type);
        Assert.Equal(0u, p0.Offset);
        Assert.Equal(EditorFormRef.FieldType.Pointer, p24.Type);
        Assert.Equal(24u, p24.Offset);
    }

    [Fact]
    public void ViewModel_Write_PersistsPointersAsGbaPointers()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);

            // Mutate both pointer fields to a ROM offset (no high bit).
            // Write() must encode them back with the 0x08000000 high bit so
            // the raw u32 read after Write matches.
            uint newOffsetP0 = 0x00200000u;
            uint newOffsetP24 = 0x00300000u;
            vm.EnglishNamePointer = newOffsetP0;
            vm.AnimePointer = newOffsetP24;
            vm.WriteEntry();

            uint rawP0 = rom.u32(entryAddr + 0);
            uint decodedP0 = rom.p32(entryAddr + 0);
            uint rawP24 = rom.u32(entryAddr + 24);
            uint decodedP24 = rom.p32(entryAddr + 24);
            Assert.Equal(newOffsetP0 | 0x08000000u, rawP0);
            Assert.Equal(newOffsetP0, decodedP0);
            Assert.Equal(newOffsetP24 | 0x08000000u, rawP24);
            Assert.Equal(newOffsetP24, decodedP24);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAllSeventeenFields()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);

            Assert.True(vm.CanWrite);
            Assert.Equal(entryAddr, vm.CurrentAddr);
            // Synthetic ROM plants known values for all 17 fields.
            Assert.Equal(0x00100200u, vm.EnglishNamePointer);
            Assert.Equal(0x0042u, vm.DescriptionTextId);
            Assert.Equal(0x1234u, vm.JapaneseNamePointer);
            Assert.Equal(0x10u, vm.JapaneseNameLength);
            Assert.Equal(0x11u, vm.ClassId);
            Assert.Equal(0x02u, vm.AllyEnemyColor);
            Assert.Equal(0x13u, vm.BattleAnime);
            Assert.Equal(0x04u, vm.MagicEffect);
            Assert.Equal(0x15u, vm.Unknown15);
            Assert.Equal(0x16u, vm.Unknown16);
            Assert.Equal(0x17u, vm.Unknown17);
            Assert.Equal(0x19u, vm.Unknown19);
            Assert.Equal(0x20u, vm.TerrainLeft);
            Assert.Equal(0x21u, vm.TerrainRight);
            Assert.Equal(0x22u, vm.Unknown22);
            Assert.Equal(0x23u, vm.Unknown23);
            Assert.Equal(0x00100300u, vm.AnimePointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_PersistsAllFields_RoundTrip()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);

            // Set every field to a new known value.
            vm.EnglishNamePointer = 0x00100500u;
            vm.DescriptionTextId = 0x0100;
            vm.JapaneseNamePointer = 0x4321;
            vm.JapaneseNameLength = 0x20;
            vm.ClassId = 0x33;
            vm.AllyEnemyColor = 0x03;
            vm.BattleAnime = 0x55;
            vm.MagicEffect = 0x06;
            vm.Unknown15 = 0x77;
            vm.Unknown16 = 0x88;
            vm.Unknown17 = 0x99;
            vm.Unknown19 = 0xAA;
            vm.TerrainLeft = 0xBB;
            vm.TerrainRight = 0xCC;
            vm.Unknown22 = 0xDD;
            vm.Unknown23 = 0xEE;
            vm.AnimePointer = 0x00100400u;
            vm.WriteEntry();

            // Re-read and verify.
            Assert.Equal(0x00100500u | 0x08000000u, rom.u32(entryAddr + 0));
            Assert.Equal(0x0100u, rom.u16(entryAddr + 4));
            Assert.Equal(0x4321u, rom.u16(entryAddr + 8));
            Assert.Equal(0x20u, rom.u8(entryAddr + 10));
            Assert.Equal(0x33u, rom.u8(entryAddr + 11));
            Assert.Equal(0x03u, rom.u8(entryAddr + 12));
            Assert.Equal(0x55u, rom.u8(entryAddr + 13));
            Assert.Equal(0x06u, rom.u8(entryAddr + 14));
            Assert.Equal(0x77u, rom.u8(entryAddr + 15));
            Assert.Equal(0x88u, rom.u8(entryAddr + 16));
            Assert.Equal(0x99u, rom.u8(entryAddr + 17));
            Assert.Equal(0xAAu, rom.u8(entryAddr + 19));
            Assert.Equal(0xBBu, rom.u8(entryAddr + 20));
            Assert.Equal(0xCCu, rom.u8(entryAddr + 21));
            Assert.Equal(0xDDu, rom.u8(entryAddr + 22));
            Assert.Equal(0xEEu, rom.u8(entryAddr + 23));
            Assert.Equal(0x00100400u | 0x08000000u, rom.u32(entryAddr + 24));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // N2 sub-list (animation command sequence) coverage.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadN2List_ParsesAnimeBlock()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);
            // P24 points at 0x08100300 - synthetic anime block at 0x100300:
            //   row 0: cmd=0x01 arg=0x10 (ranged attack, wait 16)
            //   row 1: cmd=0x05 arg=0x20 (wait 32 frames)
            //   row 2: cmd=0x08 arg=0x00 (wait for command - terminator)
            // ScanN2 stops when cmd == 0x00, so we should see 3 entries.
            Assert.Equal(3, vm.N2Entries.Count);
            Assert.Equal(0x01u, vm.N2Entries[0].Command);
            Assert.Equal(0x10u, vm.N2Entries[0].Argument);
            Assert.Equal(0x05u, vm.N2Entries[1].Command);
            Assert.Equal(0x20u, vm.N2Entries[1].Argument);
            Assert.Equal(0x08u, vm.N2Entries[2].Command);
            Assert.Equal(0x00u, vm.N2Entries[2].Argument);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteN2Entry_PersistsCommandAndArg()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);
            // Mutate row 0 - change cmd 0x01 -> 0x04 and arg 0x10 -> 0x30.
            vm.SelectedN2Index = 0;
            vm.N2Command = 0x04;
            vm.N2Argument = 0x30;
            bool ok = vm.WriteN2Entry();
            Assert.True(ok);

            uint animeOffset = U.toOffset(vm.AnimePointer);
            Assert.Equal(0x04u, rom.u8(animeOffset + 0));
            Assert.Equal(0x30u, rom.u8(animeOffset + 1));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN2Row_ClearsSelectionStateOnNegativeIndex()
    {
        // Copilot PR #537 review #2: when the ListBox loses its selection
        // (idx == -1), LoadN2Row must clear SelectedN2Index AND the editor
        // command/argument/address state so NWrite_Click doesn't silently
        // write to a stale row.
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.N2Entries.Count > 0);
            // Select row 1 and confirm the state is populated.
            vm.LoadN2Row(1);
            Assert.Equal(1, vm.SelectedN2Index);
            Assert.NotEqual(0u, vm.N2SelectedAddress);
            // Now clear with -1 — every field must reset.
            vm.LoadN2Row(-1);
            Assert.Equal(-1, vm.SelectedN2Index);
            Assert.Equal(0u, vm.N2Command);
            Assert.Equal(0u, vm.N2Argument);
            Assert.Equal(0u, vm.N2SelectedAddress);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN2List_ClearsWhenAnimePointerInvalid()
    {
        var rom = MakeMinimalFE7URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoFE7UViewModel();
            vm.LoadEntry(entryAddr);
            // Now switch the anime pointer to 0 (invalid) and reload.
            vm.AnimePointer = 0;
            vm.LoadN2List();
            Assert.Empty(vm.N2Entries);
            Assert.Equal(-1, vm.SelectedN2Index);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "OPClassDemoFE7UView.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "OPClassDemoFE7UView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE7U ROM with:
    /// - op_class_demo_pointer -> 0x08100000 (entry table at 0x100000).
    /// - One 28-byte entry with known field values; second entry is a
    ///   terminator (classId=0xFF >= 0x42 stops the WF Init iteration but
    ///   our LoadList iterates 0..0x41 so we just need it valid).
    /// - Anime block at 0x100300: 3 rows (cmd 0x01/arg 0x10, cmd 0x05/arg 0x20,
    ///   cmd 0x08/arg 0x00 - terminator).
    /// Returns the first-entry address (0x100000) so tests can call
    /// LoadEntry() directly.
    /// </summary>
    static ROM MakeMinimalFE7URomWithEntry(out uint entryAddr)
    {
        var rom = new ROM();
        // FE7U title is AE7E - synthesize an FE7U ROM.
        rom.LoadLow("synth.gba", new byte[0x1000000], "AE7E01");

        // op_class_demo_pointer -> 0x08100000.
        uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
        WriteU32(rom.Data, (int)ptrAddr, 0x08100000u);

        // Entry at 0x100000.
        entryAddr = 0x100000;
        WriteU32(rom.Data, (int)entryAddr + 0, 0x08100200u);   // P0 EnglishName ptr
        WriteU16(rom.Data, (int)entryAddr + 4, 0x0042);        // W4 DescTextId
        WriteU16(rom.Data, (int)entryAddr + 8, 0x1234);        // W8 JpName ptr
        rom.Data[entryAddr + 10] = 0x10;                       // B10 JpName length
        rom.Data[entryAddr + 11] = 0x11;                       // B11 ClassId
        rom.Data[entryAddr + 12] = 0x02;                       // B12 AllyEnemyColor
        rom.Data[entryAddr + 13] = 0x13;                       // B13 BattleAnime
        rom.Data[entryAddr + 14] = 0x04;                       // B14 MagicEffect
        rom.Data[entryAddr + 15] = 0x15;                       // B15 Unknown15
        rom.Data[entryAddr + 16] = 0x16;                       // B16 Unknown16
        rom.Data[entryAddr + 17] = 0x17;                       // B17 Unknown17
        rom.Data[entryAddr + 18] = 0x18;                       // B18 (padding)
        rom.Data[entryAddr + 19] = 0x19;                       // B19 Unknown19
        rom.Data[entryAddr + 20] = 0x20;                       // B20 TerrainLeft
        rom.Data[entryAddr + 21] = 0x21;                       // B21 TerrainRight
        rom.Data[entryAddr + 22] = 0x22;                       // B22 Unknown22
        rom.Data[entryAddr + 23] = 0x23;                       // B23 Unknown23
        WriteU32(rom.Data, (int)entryAddr + 24, 0x08100300u);  // P24 AnimePointer

        // Anime block at 0x100300.
        WriteAnimeRow(rom.Data, 0x100300, 0x01, 0x10);
        WriteAnimeRow(rom.Data, 0x100302, 0x05, 0x20);
        WriteAnimeRow(rom.Data, 0x100304, 0x08, 0x00);
        // Terminator after row 2 (cmd == 0).
        WriteAnimeRow(rom.Data, 0x100306, 0x00, 0x00);

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    static void WriteAnimeRow(byte[] data, int offset, byte cmd, byte arg)
    {
        data[offset + 0] = cmd;
        data[offset + 1] = arg;
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
