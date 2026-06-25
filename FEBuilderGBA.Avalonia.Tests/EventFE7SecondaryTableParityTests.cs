// SPDX-License-Identifier: GPL-3.0-or-later
// #957 W1b — 12-byte-aware list UI for the two FE7 secondary tables.
//
// Covers the secondary tables that the Avalonia editors previously skipped
// (NavigateTo only LOGGED out-of-list hits, assuming the 16-byte main schema):
//   * EventHaikuFE7View       — the N1 tutorial death-quote tables
//       (event_haiku_tutorial_1_pointer / event_haiku_tutorial_2_pointer),
//       12-byte records: [B0 unit][B1 map][B2][B3][P4 event ptr][W8 flag][B10][B11].
//   * EventBattleTalkFE7View  — the secondary battle-talk table
//       (event_ballte_talk2_pointer), 12-byte records:
//       [B0 unit][B1 map][B2][B3][W4 text][B6][B7][W8 flag][B10][B11].
//
// Tests build a synthetic FE7J ROM (deterministic, CI-safe) and ALSO assert
// against the real FE7J ROM when it is available (oracle-style, hand-derived
// ground truth from the raw table bytes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EventFE7SecondaryTableParityTests
{
    // -----------------------------------------------------------------
    // Synthetic FE7J ROM builder
    // -----------------------------------------------------------------

    const uint HaikuMainBase = 0x100000; // 16-byte main haiku table
    const uint HaikuTut1Base = 0x101000; // 12-byte Lyn tutorial table
    const uint HaikuTut2Base = 0x102000; // 12-byte Eliwood tutorial table
    const uint BtMainBase = 0x103000;     // 16-byte main battle-talk table
    const uint BtSecondaryBase = 0x104000; // 12-byte secondary battle-talk table

    static void WriteU16(byte[] d, uint off, ushort v)
    {
        d[off + 0] = (byte)(v & 0xFF);
        d[off + 1] = (byte)((v >> 8) & 0xFF);
    }

    static void WriteU32(byte[] d, uint off, uint v)
    {
        d[off + 0] = (byte)(v & 0xFF);
        d[off + 1] = (byte)((v >> 8) & 0xFF);
        d[off + 2] = (byte)((v >> 16) & 0xFF);
        d[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    /// <summary>
    /// Build a synthetic FE7J ROM with all five tables planted. Each pointer
    /// location holds a GBA pointer to the corresponding base offset.
    /// </summary>
    static ROM MakeSyntheticFE7J()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "AE7J01");
        var d = rom.Data;
        var info = rom.RomInfo;

        WriteU32(d, info.event_haiku_pointer, 0x08000000u | HaikuMainBase);
        WriteU32(d, info.event_haiku_tutorial_1_pointer, 0x08000000u | HaikuTut1Base);
        WriteU32(d, info.event_haiku_tutorial_2_pointer, 0x08000000u | HaikuTut2Base);
        WriteU32(d, info.event_ballte_talk_pointer, 0x08000000u | BtMainBase);
        WriteU32(d, info.event_ballte_talk2_pointer, 0x08000000u | BtSecondaryBase);

        // --- Haiku main (16-byte): 2 rows then 0-unit terminator. ---
        WriteHaikuMainRow(d, HaikuMainBase + 0 * 16, unit: 0x04, map: 0x45, text: 0x07E7, evt: 0u, flag: 0);
        WriteHaikuMainRow(d, HaikuMainBase + 1 * 16, unit: 0x05, map: 0x45, text: 0x07F8, evt: 0u, flag: 0);
        // row 2 unit==0 terminates.

        // --- Haiku tutorial 1 (12-byte): 2 rows then 0-unit terminator. ---
        WriteHaikuTutRow(d, HaikuTut1Base + 0 * 12, unit: 0x03, map: 0x45, evt: 0x08D88DBC, flag: 0x0065);
        WriteHaikuTutRow(d, HaikuTut1Base + 1 * 12, unit: 0x9E, map: 0x45, evt: 0x08D88DF8, flag: 0x0065);

        // --- Haiku tutorial 2 (12-byte): 1 row then 0-unit terminator. ---
        WriteHaikuTutRow(d, HaikuTut2Base + 0 * 12, unit: 0x01, map: 0x45, evt: 0x08D88D94, flag: 0x0065);

        // --- Battle-talk main (16-byte): 2 rows then 0-unit terminator. ---
        WriteBtMainRow(d, BtMainBase + 0 * 16, atk: 0x8E, def: 0x03, map: 0x45, text: 0x08E5, evt: 0u, flag: 0x06);
        WriteBtMainRow(d, BtMainBase + 1 * 16, atk: 0x8E, def: 0x1D, map: 0x45, text: 0x08E6, evt: 0u, flag: 0x07);

        // --- Battle-talk secondary (12-byte): 2 rows then 0-unit terminator. ---
        WriteBtSecondaryRow(d, BtSecondaryBase + 0 * 12, unit: 0x87, map: 0x00, text: 0x0851, flag: 0x0001);
        WriteBtSecondaryRow(d, BtSecondaryBase + 1 * 12, unit: 0x89, map: 0x01, text: 0x088C, flag: 0x0001);

        return rom;
    }

    static void WriteHaikuMainRow(byte[] d, uint a, byte unit, byte map, ushort text, uint evt, ushort flag)
    {
        d[a + 0] = unit; d[a + 1] = map;
        WriteU16(d, a + 4, text);
        WriteU32(d, a + 8, evt);
        WriteU16(d, a + 12, flag);
    }

    static void WriteHaikuTutRow(byte[] d, uint a, byte unit, byte map, uint evt, ushort flag)
    {
        d[a + 0] = unit; d[a + 1] = map;
        WriteU32(d, a + 4, evt);
        WriteU16(d, a + 8, flag);
    }

    static void WriteBtMainRow(byte[] d, uint a, byte atk, byte def, byte map, ushort text, uint evt, byte flag)
    {
        d[a + 0] = atk; d[a + 1] = def; d[a + 2] = map;
        WriteU16(d, a + 4, text);
        WriteU32(d, a + 8, evt);
        d[a + 12] = flag;
    }

    static void WriteBtSecondaryRow(byte[] d, uint a, byte unit, byte map, ushort text, ushort flag)
    {
        d[a + 0] = unit; d[a + 1] = map;
        WriteU16(d, a + 4, text);
        WriteU16(d, a + 8, flag);
    }

    // =================================================================
    // EventHaikuFE7 — tutorial (12-byte) list + schema
    // =================================================================

    [Fact]
    public void Haiku_Tutorial1_List_Uses12ByteStride()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();
            var list = vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);

            Assert.Equal(2, list.Count);
            // 12-byte stride: addr deltas must be 12, not 16.
            Assert.Equal(0x08000000u | HaikuTut1Base, list[0].addr | 0x08000000u);
            Assert.Equal(12u, list[1].addr - list[0].addr);
            Assert.Equal(12u, vm.BlockSize);
            Assert.True(vm.IsTutorialTable);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void Haiku_Tutorial2_List_Uses12ByteStride()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();
            var list = vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial2);

            Assert.Single(list);
            Assert.Equal(HaikuTut2Base, list[0].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void Haiku_Tutorial_LoadEntry_ReadsEventPointerAt0x04AndFlagAt0x08()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();
            vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);
            vm.LoadEntry(HaikuTut1Base);

            Assert.Equal(0x03u, vm.Unit);
            Assert.Equal(0x45u, vm.ChapterID);
            // Event pointer lives at offset 0x04 in the 12-byte schema (decoded by p32).
            Assert.Equal(0x00D88DBCu, vm.EventPointer);
            // Flag lives at offset 0x08 (u16).
            Assert.Equal(0x0065u, vm.AchievementFlag);
            // Tutorial rows carry no text id.
            Assert.Equal(0u, vm.Text);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void Haiku_Main_StaysUnchanged_16ByteSchema()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();
            var list = vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Main);

            Assert.Equal(2, list.Count);
            Assert.Equal(16u, list[1].addr - list[0].addr);
            Assert.Equal(16u, vm.BlockSize);
            Assert.False(vm.IsTutorialTable);

            vm.LoadEntry(HaikuMainBase);
            Assert.Equal(0x04u, vm.Unit);
            Assert.Equal(0x07E7u, vm.Text); // text at 0x04 in the 16-byte schema
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void Haiku_Tutorial_Write_RoundTrips12ByteSchema()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();
            vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);
            vm.LoadEntry(HaikuTut1Base);

            vm.Unit = 0x42;
            vm.ChapterID = 0x07;
            vm.EventPointer = 0x00200000u;
            vm.AchievementFlag = 0x0099;
            vm.Write();

            Assert.Equal(0x42u, rom.u8(HaikuTut1Base + 0));
            Assert.Equal(0x07u, rom.u8(HaikuTut1Base + 1));
            Assert.Equal(0x00200000u | 0x08000000u, rom.u32(HaikuTut1Base + 4)); // pointer-encoded
            Assert.Equal(0x0099u, rom.u16(HaikuTut1Base + 8));
            // Next row must be untouched (12-byte stride, not 16).
            Assert.Equal(0x9Eu, rom.u8(HaikuTut1Base + 12));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void Haiku_Tutorial_GoldenBuilder_Lockstep()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventHaikuFE7ViewModel();

            var vmTut1 = vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);
            var goldTut1 = ListParityHelper.BuildEventHaikuFE7Tutorial1List(rom);
            AssertListsMatch(goldTut1, vmTut1);

            var vmTut2 = vm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial2);
            var goldTut2 = ListParityHelper.BuildEventHaikuFE7Tutorial2List(rom);
            AssertListsMatch(goldTut2, vmTut2);
        }
        finally { CoreState.ROM = prev; }
    }

    // =================================================================
    // EventBattleTalkFE7 — secondary (12-byte) list + schema
    // =================================================================

    [Fact]
    public void BattleTalk_Secondary_List_Uses12ByteStride()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            var list = vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);

            Assert.Equal(2, list.Count);
            Assert.Equal(BtSecondaryBase, list[0].addr);
            Assert.Equal(12u, list[1].addr - list[0].addr);
            Assert.Equal(12u, vm.BlockSize);
            Assert.True(vm.IsSecondaryTable);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalk_Secondary_LoadEntry_ReadsTextAt0x04AndFlagAt0x08()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            vm.LoadEntry(BtSecondaryBase);

            Assert.Equal(0x87u, vm.AttackerUnit);
            Assert.Equal(0x00u, vm.DefenderUnit); // map/chapter id slot
            Assert.Equal(0x0851u, vm.Text);        // text at 0x04
            Assert.Equal(0x0001u, vm.AchievementFlag); // flag at 0x08 (NOT 0x0C)
            Assert.Equal(0u, vm.EventPointer);     // secondary carries no event ptr
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalk_Main_StaysUnchanged_16ByteSchema()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            var list = vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Main);

            Assert.Equal(2, list.Count);
            Assert.Equal(16u, list[1].addr - list[0].addr);
            Assert.Equal(16u, vm.BlockSize);
            Assert.False(vm.IsSecondaryTable);

            vm.LoadEntry(BtMainBase);
            Assert.Equal(0x8Eu, vm.AttackerUnit);
            Assert.Equal(0x03u, vm.DefenderUnit);
            Assert.Equal(0x06u, vm.AchievementFlag); // flag at 0x0C in 16-byte schema
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalk_Secondary_Write_RoundTrips12ByteSchema()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            vm.LoadEntry(BtSecondaryBase);

            vm.AttackerUnit = 0x55;
            vm.DefenderUnit = 0x02;
            vm.Text = 0x0123;
            vm.AchievementFlag = 0x000A;
            vm.Write();

            Assert.Equal(0x55u, rom.u8(BtSecondaryBase + 0));
            Assert.Equal(0x02u, rom.u8(BtSecondaryBase + 1));
            Assert.Equal(0x0123u, rom.u16(BtSecondaryBase + 4));
            Assert.Equal(0x000Au, rom.u16(BtSecondaryBase + 8)); // flag at 0x08, not 0x0C
            // Next row (12-byte stride) untouched.
            Assert.Equal(0x89u, rom.u8(BtSecondaryBase + 12));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalk_Main_LoadEntry_DecodesEventPointerAt0x08()
    {
        // #1437 plan review: offset 0x08 in the 16-byte MAIN schema is a P
        // (pointer) field — LoadEntry must p32-DECODE it (strip 0x08000000), not
        // read a raw u32. Plant a real GBA pointer and assert the decoded offset.
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Overwrite row 0's event-pointer slot with a real GBA pointer.
            WriteU32(rom.Data, BtMainBase + 0 * 16 + 8, 0x08D88DBCu);

            var vm = new EventBattleTalkFE7ViewModel();
            vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Main);
            vm.LoadEntry(BtMainBase);

            // Decoded (0x08000000 stripped), NOT the raw 0x08D88DBC.
            Assert.Equal(0x00D88DBCu, vm.EventPointer);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalk_Main_Write_RoundTrips16ByteSchema_WithPointerEncoding()
    {
        // #1437: round-trip the MAIN 16-byte schema. The event pointer (offset
        // 0x08) must be POINTER-ENCODED on write (0x08000000 re-added), the flag
        // lives at 0x0C (u16), and the trailing bytes at 0x0E/0x0F. The next
        // 16-byte row must stay untouched (stride correctness).
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Main);
            vm.LoadEntry(BtMainBase);

            vm.AttackerUnit = 0x8E;
            vm.DefenderUnit = 0x03;
            vm.Unknown02 = 0x45;
            vm.Unknown03 = 0x11;
            vm.Text = 0x08E5;
            vm.Unknown06 = 0x22;
            vm.Unknown07 = 0x33;
            vm.EventPointer = 0x00D88DBCu; // decoded offset; Write must re-encode
            vm.AchievementFlag = 0x0006;
            vm.Unknown0E = 0xAA;
            vm.Unknown0F = 0xBB;
            vm.Write();

            Assert.Equal(0x8Eu, rom.u8(BtMainBase + 0));
            Assert.Equal(0x03u, rom.u8(BtMainBase + 1));
            Assert.Equal(0x45u, rom.u8(BtMainBase + 2));
            Assert.Equal(0x11u, rom.u8(BtMainBase + 3));
            Assert.Equal(0x08E5u, rom.u16(BtMainBase + 4));
            Assert.Equal(0x22u, rom.u8(BtMainBase + 6));
            Assert.Equal(0x33u, rom.u8(BtMainBase + 7));
            // Pointer-ENCODED on the raw bytes (0x08000000 re-added).
            Assert.Equal(0x08D88DBCu, rom.u32(BtMainBase + 8));
            Assert.Equal(0x0006u, rom.u16(BtMainBase + 12)); // flag at 0x0C, not 0x08
            Assert.Equal(0xAAu, rom.u8(BtMainBase + 14));
            Assert.Equal(0xBBu, rom.u8(BtMainBase + 15));

            // Re-loading must decode the pointer back to the offset.
            var vm2 = new EventBattleTalkFE7ViewModel();
            vm2.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Main);
            vm2.LoadEntry(BtMainBase);
            Assert.Equal(0x00D88DBCu, vm2.EventPointer);

            // Next 16-byte row untouched (the second planted row's attacker byte).
            Assert.Equal(0x8Eu, rom.u8(BtMainBase + 16 + 0));
            Assert.Equal(0x1Du, rom.u8(BtMainBase + 16 + 1));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BattleTalkView_HasFullInputSurfaceAndWriteButton()
    {
        // #1437: the FE7 Battle Dialogue editor must expose real input fields +
        // a Write button (previously display-only — Table/Address labels only).
        string axaml = ReadAxaml("EventBattleTalkFE7View.axaml");
        foreach (string id in new[]
                 {
                     "EventBattleTalkFE7_AttackerUnit_Input",
                     "EventBattleTalkFE7_DefenderUnit_Input",
                     "EventBattleTalkFE7_Unknown02_Input",
                     "EventBattleTalkFE7_Unknown03_Input",
                     "EventBattleTalkFE7_Text_Input",
                     "EventBattleTalkFE7_Unknown06_Input",
                     "EventBattleTalkFE7_Unknown07_Input",
                     "EventBattleTalkFE7_EventPointer_Input",
                     "EventBattleTalkFE7_AchievementFlag_Input",
                     "EventBattleTalkFE7_Unknown0E_Input",
                     "EventBattleTalkFE7_Unknown0F_Input",
                     "EventBattleTalkFE7_Write_Button",
                 })
            Assert.Contains("AutomationId=\"" + id + "\"", axaml);

        // The Write button must route through the VM under an UndoService scope.
        string cs = ReadAxaml("EventBattleTalkFE7View.axaml.cs");
        Assert.Contains("UndoService", cs);
        Assert.Contains("_undoService.Begin", cs);
        Assert.Contains("_undoService.Commit", cs);
        Assert.Contains("_undoService.Rollback", cs);
        Assert.Contains("_vm.Write()", cs);
    }

    [Fact]
    public void BattleTalkView_TrailingUnknownAndDefenderLabels_RelabelPerActiveTable()
    {
        // #1437 plan review: offset 0x01 is the Defender Unit in the 16-byte MAIN
        // schema but a chapter/map id in the 12-byte SECONDARY schema (WinForms
        // N1_J_1_MAP); the trailing bytes sit at 0x0E/0x0F (MAIN) vs 0x0A/0x0B
        // (SECONDARY). All three labels must be named + relabeled per table via R._().
        string axaml = ReadAxaml("EventBattleTalkFE7View.axaml");
        Assert.Contains("Name=\"DefenderOrMapLabel\"", axaml);
        Assert.Contains("Name=\"UnknownTrailing0Label\"", axaml);
        Assert.Contains("Name=\"UnknownTrailing1Label\"", axaml);

        string cs = ReadAxaml("EventBattleTalkFE7View.axaml.cs");
        Assert.Contains("DefenderOrMapLabel.Text = R._(secondary ? \"Chapter/Map ID:\" : \"Defender Unit:\")", cs);
        Assert.Contains("UnknownTrailing0Label.Text = R._(secondary ? \"Unknown (0x0A):\" : \"Unknown (0x0E):\")", cs);
        Assert.Contains("UnknownTrailing1Label.Text = R._(secondary ? \"Unknown (0x0B):\" : \"Unknown (0x0F):\")", cs);
        // Secondary schema has no event pointer — the input must be disabled there.
        Assert.Contains("EventPointerBox.IsEnabled = !secondary", cs);
    }

    [Fact]
    public void BattleTalkView_RelabelStrings_HaveJaAndZhTranslations()
    {
        // The relabel + undo-scope strings the view toggles between must already
        // have ja AND zh entries so the L10n gate stays green (#958 review).
        string repoRoot = RepoRoot();
        foreach (string lang in new[] { "ja", "zh" })
        {
            string txt = "\n" + File.ReadAllText(Path.Combine(repoRoot, "config", "translate", lang + ".txt")).Replace("\r\n", "\n");
            foreach (string key in new[]
                     {
                         "Defender Unit:", "Chapter/Map ID:",
                         "Unknown (0x0E):", "Unknown (0x0F):",
                         "Unknown (0x0A):", "Unknown (0x0B):",
                         "Edit Battle Dialogue (FE7)",
                     })
                Assert.Contains("\n:" + key + "\n", txt);
        }
    }

    [Fact]
    public void BattleTalk_Secondary_GoldenBuilder_Lockstep()
    {
        var rom = MakeSyntheticFE7J();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EventBattleTalkFE7ViewModel();
            var vmList = vm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            var gold = ListParityHelper.BuildEventBattleTalkFE7SecondaryList(rom);
            AssertListsMatch(gold, vmList);
        }
        finally { CoreState.ROM = prev; }
    }

    // =================================================================
    // Real FE7J ROM oracle (skips when FE7J.gba is unavailable)
    // =================================================================

    [Fact]
    public void RealFE7J_TutorialAndSecondary_Are12Byte()
    {
        string? path = TestRomLocator.FindRom("FE7J");
        if (path == null) return; // ROM not present in this environment — covered by synthetic tests.

        var rom = new ROM();
        if (!rom.Load(path, out _)) return;

        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var info = rom.RomInfo;

            // Tutorial tables are 12-byte; main is 16-byte.
            var hvm = new EventHaikuFE7ViewModel();
            var tut1 = hvm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);
            var tut2 = hvm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial2);
            Assert.NotEmpty(tut1);
            Assert.NotEmpty(tut2);
            if (tut1.Count >= 2) Assert.Equal(12u, tut1[1].addr - tut1[0].addr);
            if (tut2.Count >= 2) Assert.Equal(12u, tut2[1].addr - tut2[0].addr);

            // Oracle: hand-decode the first tutorial-1 row from raw bytes.
            uint tut1Base = rom.p32(info.event_haiku_tutorial_1_pointer);
            Assert.Equal(tut1Base, tut1[0].addr);
            hvm.LoadList(EventHaikuFE7ViewModel.HaikuTable.Tutorial1);
            hvm.LoadEntry(tut1Base);
            Assert.Equal(rom.u8(tut1Base + 0), hvm.Unit);
            Assert.Equal(rom.u8(tut1Base + 1), hvm.ChapterID);
            Assert.Equal(rom.p32(tut1Base + 4), hvm.EventPointer);
            Assert.Equal(rom.u16(tut1Base + 8), hvm.AchievementFlag);

            // Secondary battle-talk is 12-byte.
            var bvm = new EventBattleTalkFE7ViewModel();
            var sec = bvm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            Assert.NotEmpty(sec);
            if (sec.Count >= 2) Assert.Equal(12u, sec[1].addr - sec[0].addr);

            uint secBase = rom.p32(info.event_ballte_talk2_pointer);
            Assert.Equal(secBase, sec[0].addr);
            bvm.LoadList(EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            bvm.LoadEntry(secBase);
            Assert.Equal(rom.u8(secBase + 0), bvm.AttackerUnit);
            Assert.Equal(rom.u16(secBase + 4), bvm.Text);
            Assert.Equal(rom.u16(secBase + 8), bvm.AchievementFlag);

            // Golden lockstep on the real ROM too.
            AssertListsMatch(ListParityHelper.BuildEventHaikuFE7Tutorial1List(rom), tut1);
            AssertListsMatch(ListParityHelper.BuildEventHaikuFE7Tutorial2List(rom), tut2);
            AssertListsMatch(ListParityHelper.BuildEventBattleTalkFE7SecondaryList(rom), sec);
        }
        finally { CoreState.ROM = prev; }
    }

    // =================================================================
    // View surface — the Table filter combo must exist with valid IDs
    // =================================================================

    [Fact]
    public void HaikuView_HasThreeWayTableFilterCombo()
    {
        string axaml = ReadAxaml("EventHaikuFE7View.axaml");
        Assert.Contains("AutomationId=\"EventHaikuFE7_Table_Combo\"", axaml);
        Assert.Contains("AutomationId=\"EventHaikuFE7_Table_Label\"", axaml);
        Assert.Contains("Tutorial 1", axaml);
        Assert.Contains("Tutorial 2", axaml);
        // Code-behind must drive the VM with the selected table.
        string cs = ReadAxaml("EventHaikuFE7View.axaml.cs");
        Assert.Contains("LoadList(SelectedTable)", cs);
        Assert.Contains("TableFilter.SelectionChanged", cs);
    }

    [Fact]
    public void BattleTalkView_HasTwoWayTableFilterCombo()
    {
        string axaml = ReadAxaml("EventBattleTalkFE7View.axaml");
        Assert.Contains("AutomationId=\"EventBattleTalkFE7_Table_Combo\"", axaml);
        Assert.Contains("AutomationId=\"EventBattleTalkFE7_Table_Label\"", axaml);
        Assert.Contains("Secondary (12-byte)", axaml);
        string cs = ReadAxaml("EventBattleTalkFE7View.axaml.cs");
        Assert.Contains("LoadList(SelectedTable)", cs);
        Assert.Contains("TableFilter.SelectionChanged", cs);
    }

    [Fact]
    public void HaikuView_TrailingUnknownLabels_RelabelPerActiveTable()
    {
        // #958 review: the two trailing Unknown bytes are at 0x0E/0x0F in the
        // 16-byte MAIN schema but 0x0A/0x0B in the 12-byte tutorial schema. The
        // labels must be named (so UpdateUI can drive them) and the code-behind
        // must relabel them per active table, routed through R._() for ja/zh.
        string axaml = ReadAxaml("EventHaikuFE7View.axaml");
        Assert.Contains("AutomationId=\"EventHaikuFE7_UnknownTrailing0_Label\"", axaml);
        Assert.Contains("AutomationId=\"EventHaikuFE7_UnknownTrailing1_Label\"", axaml);
        Assert.Contains("Name=\"UnknownTrailing0Label\"", axaml);
        Assert.Contains("Name=\"UnknownTrailing1Label\"", axaml);

        string cs = ReadAxaml("EventHaikuFE7View.axaml.cs");
        // MAIN offsets when not tutorial, tutorial offsets otherwise — both
        // routed through R._() so they localize in ja/zh.
        Assert.Contains("UnknownTrailing0Label.Text = R._(tutorial ? \"Unknown (0x0A):\" : \"Unknown (0x0E):\")", cs);
        Assert.Contains("UnknownTrailing1Label.Text = R._(tutorial ? \"Unknown (0x0B):\" : \"Unknown (0x0F):\")", cs);
    }

    [Fact]
    public void HaikuView_RelabelStrings_HaveJaAndZhTranslations()
    {
        // The 4 offset-label strings the relabel toggles between must already
        // have ja AND zh entries so the L10n gate stays green (#958 review).
        string repoRoot = RepoRoot();
        foreach (string lang in new[] { "ja", "zh" })
        {
            string txt = File.ReadAllText(Path.Combine(repoRoot, "config", "translate", lang + ".txt"));
            foreach (string key in new[] { "Unknown (0x0E):", "Unknown (0x0F):", "Unknown (0x0A):", "Unknown (0x0B):" })
                Assert.Contains("\n:" + key + "\n", "\n" + txt.Replace("\r\n", "\n"));
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    static string ReadAxaml(string fileName)
        => File.ReadAllText(Path.Combine(RepoRoot(), "FEBuilderGBA.Avalonia", "Views", fileName));

    static void AssertListsMatch(IReadOnlyList<AddrResult> expected, IReadOnlyList<AddrResult> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].addr, actual[i].addr);
            Assert.Equal(expected[i].name, actual[i].name);
        }
    }
}
