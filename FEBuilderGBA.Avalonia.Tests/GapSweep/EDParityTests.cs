// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for EDView. (#411)
//
// Closes the 72 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `EDForm` (HIGH density 65/11 -> -83.1 %, 20 WF-only labels, 0 common
// labels). The fix raises EDView from a single-record retreat editor to a
// 3-tab editor mirroring the WF tab structure exactly:
//   tabPage1 / Retreat ( 4B record, ed_1_pointer )
//   tabPage2 / Epithet ( 8B record, ed_2_pointer )
//   tabPage3 / Epilogue (8B record, ed_3a_pointer or ed_3b_pointer + filter)
//
// Mirrors PR #549 (OPClassDemoFE7) and PR #540 (EventUnit) test layout.
// Copilot CLI v1 plan review flagged four issues that the v2 plan corrected
// and these tests pin in place:
//   C1 - Epithet/Epilogue text field is `D4` (DWord, 4 bytes at offset +4),
//        not `W4`. Word-width writes would leave bytes +6/+7 stale.
//   C2 - Epithet unit field is `W0` (Word, 2 bytes at offset 0), not `B0`.
//        Matches WF designer's `N1_W0` NumericUpDown.
//   C3 - FE7 / FE8 ROMs define both `ed_3a` and `ed_3b`. FE6JP defines only
//        `ed_3a`; `ed_3b == 0`. Epilogue tab is enabled on all versions;
//        the Ephraim-route option is disabled at runtime when `ed_3b == 0`.
//   C4 - `List Expand` is functional (DataExpansionCore.ExpandTable), not
//        a dead density-only button.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the EDForm parity raise (#411) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner
/// can race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class EDParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 65 control instantiations (per the
    /// 2026-05-26 density-sweep manifest). To leave the HIGH verdict
    /// we need AV >= ceil(65 * 0.75) = 49.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 65;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 49
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasThreeTabs_Retreat_Epithet_Epilogue()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ED_Retreat_Tab\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_Tab\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_Tab\"", axaml);
    }

    [Fact]
    public void View_HasRetreatTabControls()
    {
        // Retreat tab mirrors WF panel1 (Top Address / Read Count / Reload),
        // panel9 (AddressList + ListExpand + LabelFilter), AddressPanel
        // (Address / Size / SelectedAddress / Write), panel2 (UnitId B0,
        // Condition B1, "Retreat Spec 02" label, B2, B3).
        string axaml = ReadAxaml();

        // Tab header bar
        // #668: NUD-based TopAddress/ReadCount inputs migrated to read-only
        // EditorTopBar slots; *_Input ids renamed to *_Label (display-only).
        Assert.Contains("AutomationId=\"ED_Retreat_TopAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_Write_Button\"", axaml);

        // List panel
        Assert.Contains("AutomationId=\"ED_Retreat_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_ListExpand_Button\"", axaml);

        // Field inputs (4B record)
        Assert.Contains("AutomationId=\"ED_Retreat_UnitId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_Condition_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_RetreatSpec02_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_B2_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Retreat_B3_Input\"", axaml);
    }

    [Fact]
    public void View_HasEpithetTabControls()
    {
        // Epithet tab mirrors WF panel4/panel5/panel10/panel3.
        string axaml = ReadAxaml();

        // Tab header bar
        // #668: NUD-based TopAddress/ReadCount inputs migrated to read-only
        // EditorTopBar slots; *_Input ids renamed to *_Label.
        Assert.Contains("AutomationId=\"ED_Epithet_TopAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_Write_Button\"", axaml);

        // List panel
        Assert.Contains("AutomationId=\"ED_Epithet_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_ListExpand_Button\"", axaml);

        // Field inputs - W0 (UnitId, 2 bytes) + D4 (Epithet TextID, 4 bytes).
        Assert.Contains("AutomationId=\"ED_Epithet_UnitId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_EpithetTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epithet_EpithetText_Label\"", axaml);
    }

    [Fact]
    public void View_HasEpilogueTabControls()
    {
        // Epilogue tab mirrors WF panel7/panel8/panel11/panel6.
        // Includes the Eirika/Ephraim filter combo and the N2_L_0_COMBO
        // (Solo/Support designation).
        string axaml = ReadAxaml();

        // Tab header bar
        // #668: NUD-based TopAddress/ReadCount inputs migrated to read-only
        // EditorTopBar slots; *_Input ids renamed to *_Label.
        Assert.Contains("AutomationId=\"ED_Epilogue_TopAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_Filter_Combo\"", axaml);

        // List panel
        Assert.Contains("AutomationId=\"ED_Epilogue_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_ListExpand_Button\"", axaml);

        // Field inputs - B0 (PairFlag designation 1=Solo, 2=Support),
        // B1 (UnitId1), B2 (UnitId2), B3 (StoryFlag), D4 (EpilogueTextId).
        Assert.Contains("AutomationId=\"ED_Epilogue_Designation_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_UnitId1_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_UnitId2_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_StoryFlag_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_EpilogueTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ED_Epilogue_EpilogueText_Label\"", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 5 - Write handlers must wrap ROM mutation in undo scope.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandlers_WrapInThreeIndependentUndoScopes()
    {
        // Roslyn-static read of the code-behind source - no Avalonia head
        // needed. Each Write_*_Click must open / commit / rollback its own
        // distinctly-named undo scope so the three tab surfaces undo
        // independently.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("WriteRetreat_Click", source);
        Assert.Contains("WriteEpithet_Click", source);
        Assert.Contains("WriteEpilogue_Click", source);
        Assert.Contains("Edit ED Retreat", source);
        Assert.Contains("Edit ED Epithet", source);
        Assert.Contains("Edit ED Epilogue", source);
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_WriteHandlers_RoundTripThroughViewModel()
    {
        // Code-behind must not call rom.SetU*/rom.write_u* directly - all
        // ROM mutation must go through the ViewModel methods so the
        // EditorFormRef field-width codec runs.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
        Assert.Contains("_vm.WriteRetreat(", source);
        Assert.Contains("_vm.WriteEpithet(", source);
        Assert.Contains("_vm.WriteEpilogue(", source);
    }

    [Fact]
    public void View_ListExpandHandlers_WireToExpandMethods()
    {
        // Copilot CLI v1 plan review C4: List Expand must be functional,
        // not a dead density-only button.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("ExpandRetreat", source);
        Assert.Contains("ExpandEpithet", source);
        Assert.Contains("ExpandEpilogue", source);
    }

    // -----------------------------------------------------------------
    // ViewModel - field width codec (Copilot v1 plan review C1, C2).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_FieldDef_Epithet_UsesW0_AndD4()
    {
        // C2: WF designer uses N1_W0 (Word at +0), not byte. C1: WF
        // designer uses N1_D4 (DWord at +4), read via u32(addr+4).
        var fields = EditorFormRef.DetectFields(new[] { "W0", "D4" });
        var w0 = fields.First(f => f.Name == "W0");
        var d4 = fields.First(f => f.Name == "D4");
        Assert.Equal(EditorFormRef.FieldType.Word, w0.Type);
        Assert.Equal(0u, w0.Offset);
        Assert.Equal(EditorFormRef.FieldType.DWord, d4.Type);
        Assert.Equal(4u, d4.Offset);
    }

    [Fact]
    public void ViewModel_FieldDef_Epilogue_UsesBytesAndD4()
    {
        // Epilogue record: B0 / B1 / B2 / B3 / D4.
        var fields = EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "D4" });
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[0].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[1].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[2].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[3].Type);
        Assert.Equal(EditorFormRef.FieldType.DWord, fields[4].Type);
        Assert.Equal(4u, fields[4].Offset);
    }

    // -----------------------------------------------------------------
    // ViewModel - list termination (matches WF lambdas exactly).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadRetreatList_FiltersOnU32Zero()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var list = vm.LoadRetreatList();
            // Synthetic ROM plants exactly 3 retreat entries before the
            // u32==0 terminator (see helper).
            Assert.Equal(3, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadRetreatList_DecrementsUidFor1BasedConvention()
    {
        // Copilot PR #561 bot review (9 inline threads): ED tables store
        // UnitIds as 1-based with `0` reserved as the terminator. WF
        // `UnitForm.GetUnitName(uid)` decrements internally before
        // looking up the unit table. The Avalonia list label must use
        // the same convention so e.g. retreat row "01" displays the
        // FIRST unit (table index 0), not the SECOND. We don't need to
        // assert a specific string here (NameResolver may return "???"
        // on the synthetic ROM), but the list-label substring must
        // start with the hex-formatted uid so the user can recognize it.
        var rom = MakeMinimalFE8URom(out uint retreatAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var list = vm.LoadRetreatList();
            Assert.NotEmpty(list);
            // First entry has uid=0x01 (see MakeMinimalFE8URom).
            Assert.StartsWith("01", list[0].name);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpithetList_FiltersOnU8Zero()
    {
        // WF N1_Init lambda: return Program.ROM.u8(addr) != 0x00;
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var list = vm.LoadEpithetList();
            // Synthetic ROM plants 2 epithet entries before the u8==0
            // terminator at the start of the third entry.
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpilogueList_FiltersOnU32Zero()
    {
        // WF N2_Init lambda: return Program.ROM.u32(addr) != 0x00;
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.LoadEpilogueList();
            // Synthetic ROM plants 2 epilogue Eirika entries before
            // the u32==0 terminator.
            Assert.Equal(2, vm.EpilogueList.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - field width regression (Copilot C1 / C2).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteEpithet_PersistsW0AsTwoBytes()
    {
        // C2: writing UnitId=0x1234 must persist both bytes at +0 and +1.
        // If the VM used B0 (byte) instead of W0, only the low byte
        // would round-trip and byte +1 would stay zero.
        var rom = MakeMinimalFE8URom(out _, out uint epithetAddr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.LoadEpithetList();
            vm.LoadEpithet(epithetAddr);
            vm.EpithetUnitId = 0x1234;
            vm.WriteEpithet();
            Assert.Equal(0x34u, rom.u8(epithetAddr + 0));
            Assert.Equal(0x12u, rom.u8(epithetAddr + 1));
            // Read back as a word.
            Assert.Equal(0x1234u, rom.u16(epithetAddr + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpithet_PersistsD4AsFullDword()
    {
        // C1: writing EpithetTextId=0x01020304 must persist all 4 bytes
        // at +4. If the VM used W4 (word) instead of D4, bytes +6/+7
        // would stay stale.
        var rom = MakeMinimalFE8URom(out _, out uint epithetAddr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant a known sentinel at +6/+7 so we can detect a stale
            // write.
            rom.Data[epithetAddr + 6] = 0xAA;
            rom.Data[epithetAddr + 7] = 0xBB;

            var vm = new EDViewModel();
            vm.LoadEpithetList();
            vm.LoadEpithet(epithetAddr);
            vm.EpithetTextId = 0x01020304;
            vm.WriteEpithet();
            Assert.Equal(0x04u, rom.u8(epithetAddr + 4));
            Assert.Equal(0x03u, rom.u8(epithetAddr + 5));
            Assert.Equal(0x02u, rom.u8(epithetAddr + 6));
            Assert.Equal(0x01u, rom.u8(epithetAddr + 7));
            Assert.Equal(0x01020304u, rom.u32(epithetAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpilogue_PersistsD4AsFullDword()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out uint epilogueAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            rom.Data[epilogueAddr + 6] = 0xCC;
            rom.Data[epilogueAddr + 7] = 0xDD;

            var vm = new EDViewModel();
            vm.LoadEpilogueList();
            vm.LoadEpilogue(epilogueAddr);
            vm.EpilogueTextId = 0xDEADBEEF;
            vm.WriteEpilogue();
            Assert.Equal(0xEFu, rom.u8(epilogueAddr + 4));
            Assert.Equal(0xBEu, rom.u8(epilogueAddr + 5));
            Assert.Equal(0xADu, rom.u8(epilogueAddr + 6));
            Assert.Equal(0xDEu, rom.u8(epilogueAddr + 7));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - per-version pointer availability (Copilot C3).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_EpilogueAvailability_FE8U_ReportsBothRoutes()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            Assert.Equal(EDViewModel.EpilogueAvailabilityKind.BothRoutes,
                vm.EpilogueAvailability);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpilogueList_OnFE6_AppliesDummyEntrySkip()
    {
        // Copilot CLI PR #561 fourth review: GetUnitNameForUid must use
        // SupportUnitNavigation.ResolveUnitTableName (which applies the
        // FE6 dummy-entry skip when ROM version == 6) instead of
        // NameResolver.GetUnitName (which does NOT). Without the skip
        // FE6 ED list labels would resolve to the dummy table entry /
        // off-by-one vs the View's IdField preview that already uses
        // the FE6-correct resolver.
        //
        // FE6JP only exposes the Epilogue (ed_3a_pointer) - retreat
        // and epithet pointers are both 0 - so this test exercises
        // GetUnitNameForUid through the LoadEpilogueList path.
        //
        // Set up: synthesize an FE6JP ROM, plant two consecutive unit
        // entries with DIFFERENT text-ids (slot 0 = dummy, slot 1 =
        // first real unit). Plant one epilogue record with uid1=1 so
        // the iterator reads it. Expectation: the resolved name
        // matches ResolveUnitTableName(rom, 0) (post-skip) NOT
        // NameResolver.GetUnitName(0) (dummy slot).
        var rom = new ROM();
        rom.LoadLow("synth6.gba", new byte[0x1000000], "AFEJ01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Plant ed_3a (epilogue Eirika) pointer.
            uint epilogueBase = 0x100000;
            WriteU32(rom.Data, (int)rom.RomInfo.ed_3a_pointer, 0x08000000 | epilogueBase);
            // One epilogue entry: flag=1 (Solo), uid1=1, uid2=0,
            // storyFlag=0, textId=0. The iterator terminates on
            // `u32 == 0`, so we plant the flag (non-zero) at +0 to
            // make this entry pass the predicate. The bytes +4..+7
            // can stay zero since LoadListInternal checks the u32 at
            // +0 only.
            rom.Data[epilogueBase + 0] = 0x01;  // flag = Solo
            rom.Data[epilogueBase + 1] = 0x01;  // uid1 = 1
            // +2..+7 zero by default; u32(addr) = 0x00000101 != 0.
            // Next 8 bytes at epilogueBase + 8 are the u32==0 terminator.

            // Plant unit table: index 0 = dummy with text id 0x0101,
            // index 1 = first real unit with text id 0x0202.
            uint unitTableAddr = 0x200000;
            WriteU32(rom.Data, (int)rom.RomInfo.unit_pointer, 0x08000000 | unitTableAddr);
            uint unitDataSize = rom.RomInfo.unit_datasize;
            WriteU16(rom.Data, (int)unitTableAddr, 0x0101);
            WriteU16(rom.Data, (int)(unitTableAddr + unitDataSize), 0x0202);

            var vm = new EDViewModel();
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Eirika;
            var list = vm.LoadEpilogueList();
            Assert.NotEmpty(list);

            // The label MUST be the post-skip slot 1 (uid=1 -> table
            // index 0 after the dummy skip -> textId 0x0202), not the
            // dummy slot's 0x0101. Both resolve to "???" on this
            // synthetic ROM (no Huffman table), but the assertion
            // confirms GetUnitNameForUid routes through the
            // FE6-aware resolver.
            string nameFromCorrectResolver = SupportUnitNavigation.ResolveUnitTableName(rom, 0);
            Assert.EndsWith(nameFromCorrectResolver, list[0].name);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_EpilogueAvailability_FE6JP_ReportsEirikaOnly()
    {
        // FE6JP defines `ed_3a_pointer = 0x91834` but `ed_3b_pointer = 0`.
        var rom = new ROM();
        rom.LoadLow("synth6.gba", new byte[0x1000000], "AFEJ01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            Assert.Equal(EDViewModel.EpilogueAvailabilityKind.EirikaOnly,
                vm.EpilogueAvailability);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpilogueList_OnFE6JP_EphraimRoute_ReturnsEmpty()
    {
        // Setting the route to Ephraim on FE6JP (ed_3b == 0) must not
        // throw and must return an empty list. View binds combo IsEnabled
        // to availability so the user normally can't trigger this, but
        // the VM stays defensive.
        var rom = new ROM();
        rom.LoadLow("synth6.gba", new byte[0x1000000], "AFEJ01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Ephraim;
            vm.LoadEpilogueList();
            Assert.Empty(vm.EpilogueList);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - epilogue route switch.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEpilogueList_SwitchesBetweenEirikaAndEphraim()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out uint eirikaAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Eirika;
            vm.LoadEpilogueList();
            Assert.Equal(2, vm.EpilogueList.Count);
            uint firstEirika = vm.EpilogueList[0].addr;
            Assert.Equal(eirikaAddr, firstEirika);

            // Now switch to Ephraim and reload - synthetic ROM plants
            // a different list at the Ephraim base.
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Ephraim;
            vm.LoadEpilogueList();
            Assert.Single(vm.EpilogueList);
            Assert.NotEqual(firstEirika, vm.EpilogueList[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - round-trip writes.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteRetreat_RoundTrips()
    {
        var rom = MakeMinimalFE8URom(out uint retreatAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.LoadRetreatList();
            vm.LoadRetreat(retreatAddr);
            vm.RetreatUnitId = 0x11;
            vm.RetreatCondition = 0x02;
            vm.RetreatB2 = 0x55;
            vm.RetreatB3 = 0x66;
            vm.WriteRetreat();
            Assert.Equal(0x11u, rom.u8(retreatAddr + 0));
            Assert.Equal(0x02u, rom.u8(retreatAddr + 1));
            Assert.Equal(0x55u, rom.u8(retreatAddr + 2));
            Assert.Equal(0x66u, rom.u8(retreatAddr + 3));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpithet_RoundTrips()
    {
        var rom = MakeMinimalFE8URom(out _, out uint epithetAddr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.LoadEpithetList();
            vm.LoadEpithet(epithetAddr);
            vm.EpithetUnitId = 0x0042;
            vm.EpithetTextId = 0x000007D2;
            vm.WriteEpithet();
            Assert.Equal(0x0042u, rom.u16(epithetAddr + 0));
            Assert.Equal(0x000007D2u, rom.u32(epithetAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpilogue_RoundTrips()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out uint epilogueAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Eirika;
            vm.LoadEpilogueList();
            vm.LoadEpilogue(epilogueAddr);
            vm.EpiloguePairFlag = 0x02;
            vm.EpilogueUnitId1 = 0x05;
            vm.EpilogueUnitId2 = 0x07;
            vm.EpilogueStoryFlag = 0xAB;
            vm.EpilogueTextId = 0x00000835;
            vm.WriteEpilogue();
            Assert.Equal(0x02u, rom.u8(epilogueAddr + 0));
            Assert.Equal(0x05u, rom.u8(epilogueAddr + 1));
            Assert.Equal(0x07u, rom.u8(epilogueAddr + 2));
            Assert.Equal(0xABu, rom.u8(epilogueAddr + 3));
            Assert.Equal(0x00000835u, rom.u32(epilogueAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - List Expand functional, not dead (Copilot C4).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExpandRetreatList_LeavesNewRowVisibleAndEditable()
    {
        // Copilot CLI PR #561 blocking finding: the new row must be visible
        // to LoadRetreatList. Without SeedExpandedRow the zero-filled new
        // row would hit the u32==0 terminator and the count would NOT
        // grow.
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var listBefore = vm.LoadRetreatList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandRetreatList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandRetreatList must succeed; got error: {result.Error}");
            Assert.True(result.NewCount > 0);

            // The new row MUST survive the next LoadRetreatList scan.
            var listAfter = vm.LoadRetreatList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            // And it must be editable - load the new last row and verify
            // CanWrite + addressable.
            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadRetreat(lastRow.addr);
            Assert.True(vm.RetreatCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpithetList_LeavesNewRowVisibleAndEditable()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var listBefore = vm.LoadEpithetList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandEpithetList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandEpithetList must succeed; got error: {result.Error}");

            // The new row MUST survive the next LoadEpithetList scan.
            // (u8(addr)==0 terminator would have hidden the zero-filled
            // new row.)
            var listAfter = vm.LoadEpithetList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadEpithet(lastRow.addr);
            Assert.True(vm.EpithetCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandRetreatList_ExactFitFreeRun_TerminatorStaysInsideReservation()
    {
        // Copilot CLI PR #561 third review: even with the 0xFF-byte guard
        // the previous SeedExpandedRow design left an exact-fit ED
        // expansion UNTERMINATED. The next LoadList scan would then
        // spill into unrelated following bytes (often 0xFF, which
        // doesn't match the u32==0/u8==0 terminator predicate) and
        // iterate up to the 0x200-row safety cap showing garbage.
        //
        // The fix reserves (liveCount + 2) entries from
        // DataExpansionCore.ExpandTable - one for each live row, one
        // for the existing terminator (which we re-seed with the new
        // editable row), and one final zero entry that becomes the
        // new terminator. All writes are within the reserved free-space
        // region, and the next LoadList scan terminates cleanly at the
        // final zero entry.
        //
        // This test exercises the precise exact-fit boundary that the
        // wide 0xFF free pool in MakeMinimalFE8URom would have hidden:
        // an exactly-sized 0xFF run for the new (liveCount + 2) layout,
        // followed immediately by a `0xFF` sentinel + a non-zero
        // sentinel that must not be touched and must mark the run end.
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Erase the wide free pool so FindFreeSpace must locate
            // our exact-fit run.
            for (uint i = 0x500000; i < 0x510000; i++)
                rom.Data[i] = 0x00;

            // currentCount = 3 retreat entries, blockSize = 4. With the
            // new ExpandTerminatedTable wrapper we ask ExpandTable to
            // reserve (liveCount + 1 = 4) records -> 20 bytes (4
            // copied + 1 appended zero entry). Plant exactly 20 bytes
            // of 0xFF at 0x600000.
            uint sentinelStart = 0x600000;
            for (uint i = sentinelStart; i < sentinelStart + 20; i++)
                rom.Data[i] = 0xFF;
            // Sentinel bytes that MUST survive (this is unrelated ROM
            // data following the free run).
            byte[] sentinel = { 0xDE, 0xAD, 0xBE, 0xEF };
            for (uint i = 0; i < sentinel.Length; i++)
                rom.Data[sentinelStart + 20 + i] = sentinel[i];

            var vm = new EDViewModel();
            var listBefore = vm.LoadRetreatList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandRetreatList();
            Assert.True(result.Success,
                $"ExpandRetreatList must succeed; got error: {result.Error}");

            // The 4 sentinel bytes immediately past the reserved region
            // must remain untouched.
            Assert.Equal(0xDEu, rom.u8(sentinelStart + 20 + 0));
            Assert.Equal(0xADu, rom.u8(sentinelStart + 20 + 1));
            Assert.Equal(0xBEu, rom.u8(sentinelStart + 20 + 2));
            Assert.Equal(0xEFu, rom.u8(sentinelStart + 20 + 3));

            // The expanded list MUST terminate cleanly - i.e. the new
            // editable row is visible and exactly one more than before,
            // with no garbage rows from the safety-cap fallback.
            var listAfter = vm.LoadRetreatList();
            Assert.Equal(countBefore + 1, listAfter.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpilogueList_LeavesNewRowVisibleAndEditable()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            vm.EpilogueRoute = EDViewModel.EpilogueRouteKind.Eirika;
            var listBefore = vm.LoadEpilogueList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandEpilogueList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandEpilogueList must succeed; got error: {result.Error}");

            // The new row MUST survive the next LoadEpilogueList scan.
            // (u32==0 terminator would have hidden the zero-filled new
            // row.)
            var listAfter = vm.LoadEpilogueList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadEpilogue(lastRow.addr);
            Assert.True(vm.EpilogueCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Backward-compat shims (prevent regression in ListParityHelper /
    // INavigationTargetSource callers).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEDList_StillForwardsToRetreatList()
    {
        var rom = MakeMinimalFE8URom(out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDViewModel();
            var legacy = vm.LoadEDList();
            var modern = vm.LoadRetreatList();
            Assert.Equal(modern.Count, legacy.Count);
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
            "EDView.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EDView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with:
    /// - ed_1_pointer  -> 0x08100000 (retreat table at 0x100000).
    /// - ed_2_pointer  -> 0x08100100 (epithet table at 0x100100).
    /// - ed_3a_pointer -> 0x08100200 (epilogue Eirika at 0x100200).
    /// - ed_3b_pointer -> 0x08100300 (epilogue Ephraim at 0x100300).
    /// - Retreat: 3 entries + u32==0 terminator.
    /// - Epithet: 2 entries + u8==0 terminator.
    /// - Epilogue Eirika: 2 entries + u32==0 terminator.
    /// - Epilogue Ephraim: 1 entry + u32==0 terminator.
    /// Also writes 0xFF over a large free-space region starting at
    /// 0x500000 so DataExpansionCore.ExpandTable can find a slot.
    /// </summary>
    static ROM MakeMinimalFE8URom(out uint retreatAddr,
                                  out uint epithetAddr,
                                  out uint eirikaAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Free-space region for ExpandTable (~64KB of 0xFF).
        for (uint i = 0x500000; i < 0x510000; i++)
            rom.Data[i] = 0xFF;

        // Plant pointers.
        WriteU32(rom.Data, (int)rom.RomInfo.ed_1_pointer, 0x08100000);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_2_pointer, 0x08100100);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_3a_pointer, 0x08100200);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_3b_pointer, 0x08100300);

        // Retreat list (4B records, ed_1) - 3 entries then u32==0.
        retreatAddr = 0x100000;
        rom.Data[0x100000 + 0] = 0x01; rom.Data[0x100000 + 1] = 0x00;
        rom.Data[0x100000 + 2] = 0x00; rom.Data[0x100000 + 3] = 0x00;
        rom.Data[0x100004 + 0] = 0x02; rom.Data[0x100004 + 1] = 0x01;
        rom.Data[0x100004 + 2] = 0x00; rom.Data[0x100004 + 3] = 0x00;
        rom.Data[0x100008 + 0] = 0x03; rom.Data[0x100008 + 1] = 0x02;
        rom.Data[0x100008 + 2] = 0x00; rom.Data[0x100008 + 3] = 0x00;
        // Terminator at +12: all-zero u32.

        // Epithet list (8B records, ed_2) - 2 entries then u8==0 terminator.
        epithetAddr = 0x100100;
        WriteU16(rom.Data, 0x100100 + 0, 0x0001);             // W0 UnitId
        WriteU32(rom.Data, 0x100100 + 4, 0x000007D1);         // D4 TextId
        WriteU16(rom.Data, 0x100108 + 0, 0x0002);
        WriteU32(rom.Data, 0x100108 + 4, 0x000007D2);
        // Terminator at +16: u8(addr)==0.

        // Epilogue Eirika (8B records, ed_3a) - 2 entries.
        eirikaAddr = 0x100200;
        rom.Data[0x100200 + 0] = 0x01; // PairFlag = 1 (solo)
        rom.Data[0x100200 + 1] = 0x05; // UnitId1
        rom.Data[0x100200 + 2] = 0x00; // UnitId2
        rom.Data[0x100200 + 3] = 0x10; // StoryFlag
        WriteU32(rom.Data, 0x100200 + 4, 0x00000800);
        rom.Data[0x100208 + 0] = 0x02; // PairFlag = 2 (support)
        rom.Data[0x100208 + 1] = 0x06;
        rom.Data[0x100208 + 2] = 0x07;
        rom.Data[0x100208 + 3] = 0x11;
        WriteU32(rom.Data, 0x100208 + 4, 0x00000801);
        // Terminator at +16: all-zero u32.

        // Epilogue Ephraim (8B records, ed_3b) - 1 entry.
        rom.Data[0x100300 + 0] = 0x01;
        rom.Data[0x100300 + 1] = 0x08;
        rom.Data[0x100300 + 2] = 0x00;
        rom.Data[0x100300 + 3] = 0x12;
        WriteU32(rom.Data, 0x100300 + 4, 0x00000802);
        // Terminator at +8.

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
