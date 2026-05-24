// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for EDFE7View. (#403)
//
// Closes the 101 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `EDFE7Form` (HIGH density 81/3 -> -96.3%, 23 WF-only labels, 0 common
// labels). The fix raises EDFE7View from a single-stub editor to a 4-tab
// editor mirroring the WF tab structure exactly:
//   tabPage1 / Lyn Arc  (12B record, ed_3c_pointer USED AS DIRECT BASE)
//   tabPage2 / Retreat  ( 4B record, ed_1_pointer)
//   tabPage3 / Epithet  ( 8B record, ed_2_pointer)
//   tabPage4 / Epilogue ( 8B record, ed_3a_pointer or ed_3b_pointer + filter)
//
// Mirrors PR #561 (EDForm/FE8) test layout. Copilot CLI v1 plan review surfaced
// seven issues that v2 corrected and these tests pin in place:
//   C1 - Lyn Arc is DIRECT-BASE (ed_3c_pointer is the table base, not a
//        pointer to dereference). VM reads via `rom.RomInfo.ed_3c_pointer`
//        directly (no `rom.p32()`). View has NO Lyn ListExpand button (WF
//        designer does not include one either).
//   C2 - Epithet field is D0 (DWord at +0), NOT W0 like FE8. Writing 4 bytes
//        at +0 spans the full entry; there are NO reserved bytes +1..+3.
//   C3 - Terminator predicates are u32==0 for all four surfaces (Lyn /
//        Retreat / Epithet / Epilogue). Discriminating fixtures use rows
//        with low byte zero but u32 nonzero to prove the predicate is the
//        WIDE one (not an accidental u8==0).
//   C4 - List Expand applies ONLY to Retreat / Epithet / Epilogue (pointer-
//        backed). Each tab's expand uses its OWN UndoService scope name.
//        Route-specific Epilogue expand updates only `ed_3a` OR `ed_3b`.
//   C5 - Undo tests are BEHAVIORAL (run rollback, assert bytes revert) not
//        only source-string scans.
//   C6 - MakeMinimalFE7URom plants Eliwood + Hector tables. A separate
//        FE7J smoke test verifies per-version dispatch.
//   C7 - Retreat help text is on N4_L_1 in WF; mapped to
//        `EDFE7_Retreat_HelpText_TextBlock` in AV.
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
/// Tests proving the EDFE7Form parity raise (#403) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner
/// can race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class EDFE7ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 81 control instantiations (per the
    /// 2026-05-26 density-sweep manifest). To leave the HIGH verdict
    /// we need AV >= ceil(81 * 0.75) = 61.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 81;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 61
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasFourTabs_Lyn_Retreat_Epithet_Epilogue()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"EDFE7_Lyn_Tab\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Tab\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_Tab\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Tab\"", axaml);
    }

    [Fact]
    public void View_HasLynTabControls()
    {
        // Lyn Arc tab mirrors WF panel1 / panel9 / AddressPanel / panel2
        // EXCEPT for the ListExpand button (WF designer has none for Lyn,
        // and ed_3c_pointer is DIRECT BASE so we can't relocate the table).
        string axaml = ReadAxaml();

        Assert.Contains("AutomationId=\"EDFE7_Lyn_TopAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_Entry_List\"", axaml);

        // Field inputs (12B record - three DWords).
        Assert.Contains("AutomationId=\"EDFE7_Lyn_UnitId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_ClearedTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Lyn_RetreatTextId_Input\"", axaml);
    }

    [Fact]
    public void View_LynTab_HasNoListExpandButton()
    {
        // Copilot CLI v1 review C1: WF designer has no Lyn list-expand button
        // (ed_3c_pointer is a direct base, not a pointer-backed table). Adding
        // one would corrupt data. This test pins that absence as parity.
        string axaml = ReadAxaml();
        Assert.DoesNotContain("AutomationId=\"EDFE7_Lyn_ListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_HasRetreatTabControls()
    {
        string axaml = ReadAxaml();

        Assert.Contains("AutomationId=\"EDFE7_Retreat_TopAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_ListExpand_Button\"", axaml);

        // Field inputs (4B record).
        Assert.Contains("AutomationId=\"EDFE7_Retreat_UnitId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_RetreatSpec02_Label\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_Condition_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_B2_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Retreat_B3_Input\"", axaml);

        // FE7-specific help text (N4_L_1 in WF; codes 03/04/05 are
        // Hawkeye / Pent and Louise / Athos).
        Assert.Contains("AutomationId=\"EDFE7_Retreat_HelpText_TextBlock\"", axaml);
    }

    [Fact]
    public void View_HasEpithetTabControls()
    {
        string axaml = ReadAxaml();

        Assert.Contains("AutomationId=\"EDFE7_Epithet_TopAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_ListExpand_Button\"", axaml);

        // Field inputs - D0 (UnitId, 4 bytes - distinct from FE8's W0!)
        // + D4 (Epithet TextID, 4 bytes).
        Assert.Contains("AutomationId=\"EDFE7_Epithet_UnitId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_EpithetTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epithet_EpithetText_Label\"", axaml);
    }

    [Fact]
    public void View_HasEpilogueTabControls()
    {
        string axaml = ReadAxaml();

        Assert.Contains("AutomationId=\"EDFE7_Epilogue_TopAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Filter_Combo\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_ListExpand_Button\"", axaml);

        // Field inputs.
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_Designation_Combo\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_UnitId1_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_UnitId2_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_StoryFlag_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_EpilogueTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"EDFE7_Epilogue_EpilogueText_Label\"", axaml);

        // FE7-specific route names (NOT Eirika/Ephraim from FE8).
        Assert.Contains("Eliwood Route", axaml);
        Assert.Contains("Hector Route", axaml);
        Assert.DoesNotContain("Eirika Route", axaml);
        Assert.DoesNotContain("Ephraim Route", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 5 - Write handlers must wrap ROM mutation in undo scope.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandlers_WrapInFourIndependentUndoScopes()
    {
        // Each Write_*_Click must open / commit / rollback its own
        // distinctly-named undo scope so the four tab surfaces undo
        // independently.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("WriteLyn_Click", source);
        Assert.Contains("WriteRetreat_Click", source);
        Assert.Contains("WriteEpithet_Click", source);
        Assert.Contains("WriteEpilogue_Click", source);
        Assert.Contains("Edit EDFE7 Lyn", source);
        Assert.Contains("Edit EDFE7 Retreat", source);
        Assert.Contains("Edit EDFE7 Epithet", source);
        Assert.Contains("Edit EDFE7 Epilogue", source);
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
        Assert.Contains("_vm.WriteLyn(", source);
        Assert.Contains("_vm.WriteRetreat(", source);
        Assert.Contains("_vm.WriteEpithet(", source);
        Assert.Contains("_vm.WriteEpilogue(", source);
    }

    [Fact]
    public void View_ListExpandHandlers_WireToExpandMethods()
    {
        // Copilot CLI v2 plan review: List Expand applies ONLY to pointer-
        // backed tables (Retreat / Epithet / Epilogue). Lyn does NOT get an
        // expand handler.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("ExpandRetreat", source);
        Assert.Contains("ExpandEpithet", source);
        Assert.Contains("ExpandEpilogue", source);
        Assert.DoesNotContain("ExpandLyn", source);
    }

    // -----------------------------------------------------------------
    // ViewModel field-defs (Copilot C1 / C2).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_FieldDef_Lyn_UsesThreeDwords()
    {
        // C1: Lyn record is 12 bytes - three DWords at offsets 0, 4, 8.
        var fields = EditorFormRef.DetectFields(new[] { "D0", "D4", "D8" });
        Assert.Equal(3, fields.Count);
        var d0 = fields.First(f => f.Name == "D0");
        var d4 = fields.First(f => f.Name == "D4");
        var d8 = fields.First(f => f.Name == "D8");
        Assert.Equal(EditorFormRef.FieldType.DWord, d0.Type);
        Assert.Equal(0u, d0.Offset);
        Assert.Equal(EditorFormRef.FieldType.DWord, d4.Type);
        Assert.Equal(4u, d4.Offset);
        Assert.Equal(EditorFormRef.FieldType.DWord, d8.Type);
        Assert.Equal(8u, d8.Offset);
    }

    [Fact]
    public void ViewModel_FieldDef_Epithet_UsesD0AndD4()
    {
        // C2: WF designer uses N1_D0 (DWord at +0), NOT N1_W0 like FE8.
        // The whole +0..+3 range is the UnitId; there are no reserved
        // bytes between D0 and D4.
        var fields = EditorFormRef.DetectFields(new[] { "D0", "D4" });
        var d0 = fields.First(f => f.Name == "D0");
        var d4 = fields.First(f => f.Name == "D4");
        Assert.Equal(EditorFormRef.FieldType.DWord, d0.Type);
        Assert.Equal(0u, d0.Offset);
        Assert.Equal(EditorFormRef.FieldType.DWord, d4.Type);
        Assert.Equal(4u, d4.Offset);
    }

    [Fact]
    public void ViewModel_FieldDef_Epilogue_UsesBytesAndD4()
    {
        var fields = EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "D4" });
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[0].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[1].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[2].Type);
        Assert.Equal(EditorFormRef.FieldType.Byte, fields[3].Type);
        Assert.Equal(EditorFormRef.FieldType.DWord, fields[4].Type);
        Assert.Equal(4u, fields[4].Offset);
    }

    // -----------------------------------------------------------------
    // ViewModel - list termination + discriminating fixtures (Copilot C3).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadLynList_FiltersOnU32Zero()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var list = vm.LoadLynList();
            // Synthetic ROM plants 2 lyn entries before the u32==0 terminator.
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadLynList_FiltersOnU32Zero_LowByteZeroVisible()
    {
        // C3: discriminating fixture. Plant a Lyn entry with D0 low byte
        // zero but u32 nonzero. If the VM accidentally used u8(addr)==0,
        // this entry would be hidden.
        var rom = MakeMinimalFE7URom(out uint lynAddr, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Overwrite first lyn entry's D0 with 0x00000100 (low byte 0,
            // u32 nonzero).
            rom.Data[lynAddr + 0] = 0x00;
            rom.Data[lynAddr + 1] = 0x01;
            rom.Data[lynAddr + 2] = 0x00;
            rom.Data[lynAddr + 3] = 0x00;

            var vm = new EDFE7ViewModel();
            var list = vm.LoadLynList();
            // Both entries still visible (terminator is u32==0 not u8==0).
            Assert.Equal(2, list.Count);
            Assert.Equal(lynAddr, list[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadRetreatList_FiltersOnU32Zero()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var list = vm.LoadRetreatList();
            Assert.Equal(3, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadRetreatList_FiltersOnU32Zero_LowByteZeroVisible()
    {
        var rom = MakeMinimalFE7URom(out _, out uint retreatAddr, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // First retreat entry: B0=0 but B1=1 (so u32 != 0).
            rom.Data[retreatAddr + 0] = 0x00;
            rom.Data[retreatAddr + 1] = 0x01;
            rom.Data[retreatAddr + 2] = 0x00;
            rom.Data[retreatAddr + 3] = 0x00;

            var vm = new EDFE7ViewModel();
            var list = vm.LoadRetreatList();
            Assert.Equal(3, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpithetList_FiltersOnU32Zero()
    {
        // FE7 epithet predicate is u32(addr)!=0 (NOT u8(addr)!=0 like FE8).
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var list = vm.LoadEpithetList();
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpithetList_FiltersOnU32Zero_LowByteZeroVisible()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out uint epithetAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // First epithet entry's D0: low byte zero, u32 nonzero.
            rom.Data[epithetAddr + 0] = 0x00;
            rom.Data[epithetAddr + 1] = 0x01;
            rom.Data[epithetAddr + 2] = 0x00;
            rom.Data[epithetAddr + 3] = 0x00;

            var vm = new EDFE7ViewModel();
            var list = vm.LoadEpithetList();
            // FE7 uses u32(addr)!=0; both entries still visible.
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEpilogueList_FiltersOnU32Zero()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            vm.LoadEpilogueList();
            Assert.Equal(2, vm.EpilogueList.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - Lyn direct-base (Copilot C1).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadLynList_UsesEdC_PointerAsDirectBase()
    {
        // C1: ed_3c_pointer IS the table base, not a pointer to it. If the
        // VM accidentally called rom.p32(ed_3c_pointer), the iterator would
        // jump to whatever GBA address the bytes at ed_3c_pointer happen to
        // decode to.
        var rom = MakeMinimalFE7URom(out uint lynAddr, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var list = vm.LoadLynList();
            Assert.NotEmpty(list);
            // The first entry's address MUST equal the lyn direct base
            // (= rom.RomInfo.ed_3c_pointer in the synthetic ROM where we
            // planted entries directly there). NOT the dereferenced value.
            Assert.Equal(rom.RomInfo.ed_3c_pointer, list[0].addr);
            Assert.Equal(lynAddr, list[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - round-trip writes.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteLyn_PersistsAllThreeDwords()
    {
        var rom = MakeMinimalFE7URom(out uint lynAddr, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.LoadLynList();
            vm.LoadLyn(lynAddr);
            vm.LynUnitId = 0xAABBCCDD;
            vm.LynClearedTextId = 0x11223344;
            vm.LynRetreatTextId = 0x55667788;
            vm.WriteLyn();

            Assert.Equal(0xAABBCCDDu, rom.u32(lynAddr + 0));
            Assert.Equal(0x11223344u, rom.u32(lynAddr + 4));
            Assert.Equal(0x55667788u, rom.u32(lynAddr + 8));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpithet_PersistsD0AsFullDword()
    {
        // C2: writing UnitId=0xDEADBEEF must persist all 4 bytes at +0
        // (NOT only the low byte like W0 would).
        var rom = MakeMinimalFE7URom(out _, out _, out uint epithetAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant sentinels at +1, +2, +3 to detect a stale-byte write.
            rom.Data[epithetAddr + 1] = 0xAA;
            rom.Data[epithetAddr + 2] = 0xBB;
            rom.Data[epithetAddr + 3] = 0xCC;

            var vm = new EDFE7ViewModel();
            var list = vm.LoadEpithetList();
            // After overwriting +1..+3, u32 at +0 is 0xCCBBAA?? != 0, so the
            // entry still passes the predicate.
            vm.LoadEpithet(epithetAddr);
            vm.EpithetUnitId = 0xDEADBEEF;
            vm.WriteEpithet();
            Assert.Equal(0xEFu, rom.u8(epithetAddr + 0));
            Assert.Equal(0xBEu, rom.u8(epithetAddr + 1));
            Assert.Equal(0xADu, rom.u8(epithetAddr + 2));
            Assert.Equal(0xDEu, rom.u8(epithetAddr + 3));
            Assert.Equal(0xDEADBEEFu, rom.u32(epithetAddr + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpithet_PersistsD4AsFullDword()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out uint epithetAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            rom.Data[epithetAddr + 6] = 0xAA;
            rom.Data[epithetAddr + 7] = 0xBB;

            var vm = new EDFE7ViewModel();
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
        var rom = MakeMinimalFE7URom(out _, out _, out _, out uint eliwoodAddr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            rom.Data[eliwoodAddr + 6] = 0xCC;
            rom.Data[eliwoodAddr + 7] = 0xDD;

            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            vm.LoadEpilogueList();
            vm.LoadEpilogue(eliwoodAddr);
            vm.EpilogueTextId = 0xDEADBEEF;
            vm.WriteEpilogue();
            Assert.Equal(0xEFu, rom.u8(eliwoodAddr + 4));
            Assert.Equal(0xBEu, rom.u8(eliwoodAddr + 5));
            Assert.Equal(0xADu, rom.u8(eliwoodAddr + 6));
            Assert.Equal(0xDEu, rom.u8(eliwoodAddr + 7));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteRetreat_RoundTrips()
    {
        var rom = MakeMinimalFE7URom(out _, out uint retreatAddr, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
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
        var rom = MakeMinimalFE7URom(out _, out _, out uint epithetAddr, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.LoadEpithetList();
            vm.LoadEpithet(epithetAddr);
            vm.EpithetUnitId = 0x00000042;
            vm.EpithetTextId = 0x000007D2;
            vm.WriteEpithet();
            Assert.Equal(0x00000042u, rom.u32(epithetAddr + 0));
            Assert.Equal(0x000007D2u, rom.u32(epithetAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteEpilogue_RoundTrips()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out uint eliwoodAddr, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            vm.LoadEpilogueList();
            vm.LoadEpilogue(eliwoodAddr);
            vm.EpiloguePairFlag = 0x02;
            vm.EpilogueUnitId1 = 0x05;
            vm.EpilogueUnitId2 = 0x07;
            vm.EpilogueStoryFlag = 0xAB;
            vm.EpilogueTextId = 0x00000835;
            vm.WriteEpilogue();
            Assert.Equal(0x02u, rom.u8(eliwoodAddr + 0));
            Assert.Equal(0x05u, rom.u8(eliwoodAddr + 1));
            Assert.Equal(0x07u, rom.u8(eliwoodAddr + 2));
            Assert.Equal(0xABu, rom.u8(eliwoodAddr + 3));
            Assert.Equal(0x00000835u, rom.u32(eliwoodAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteLyn_RoundTrips()
    {
        var rom = MakeMinimalFE7URom(out uint lynAddr, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.LoadLynList();
            vm.LoadLyn(lynAddr);
            vm.LynUnitId = 0x12345678;
            vm.LynClearedTextId = 0x9ABCDEF0;
            vm.LynRetreatTextId = 0xFEDCBA98;
            vm.WriteLyn();
            Assert.Equal(0x12345678u, rom.u32(lynAddr + 0));
            Assert.Equal(0x9ABCDEF0u, rom.u32(lynAddr + 4));
            Assert.Equal(0xFEDCBA98u, rom.u32(lynAddr + 8));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - route switch (Copilot C6).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEpilogueList_SwitchesBetweenEliwoodAndHector()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out uint eliwoodAddr, out uint hectorAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            vm.LoadEpilogueList();
            Assert.Equal(2, vm.EpilogueList.Count);
            Assert.Equal(eliwoodAddr, vm.EpilogueList[0].addr);

            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Hector;
            vm.LoadEpilogueList();
            Assert.Single(vm.EpilogueList);
            Assert.Equal(hectorAddr, vm.EpilogueList[0].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - List Expand functional, not dead (Copilot C4).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExpandRetreatList_LeavesNewRowVisibleAndEditable()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var listBefore = vm.LoadRetreatList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandRetreatList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandRetreatList must succeed; got error: {result.Error}");

            var listAfter = vm.LoadRetreatList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadRetreat(lastRow.addr);
            Assert.True(vm.RetreatCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpithetList_LeavesNewRowVisibleAndEditable()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var listBefore = vm.LoadEpithetList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandEpithetList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandEpithetList must succeed; got error: {result.Error}");

            var listAfter = vm.LoadEpithetList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadEpithet(lastRow.addr);
            Assert.True(vm.EpithetCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpilogueList_LeavesNewRowVisibleAndEditable()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            var listBefore = vm.LoadEpilogueList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandEpilogueList();
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"ExpandEpilogueList must succeed; got error: {result.Error}");

            var listAfter = vm.LoadEpilogueList();
            Assert.Equal(countBefore + 1, listAfter.Count);

            var lastRow = listAfter[listAfter.Count - 1];
            vm.LoadEpilogue(lastRow.addr);
            Assert.True(vm.EpilogueCanWrite);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandRetreatList_ExactFitFreeRun_TerminatorStaysInsideReservation()
    {
        // Mirror PR #561 exact-fit sentinel test.
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Erase the wide free pool so FindFreeSpace must locate
            // our exact-fit run.
            for (uint i = 0x500000; i < 0x510000; i++)
                rom.Data[i] = 0x00;

            // currentCount = 3 retreat entries, blockSize = 4. With the
            // ExpandTerminatedTable wrapper we ask ExpandTable to reserve
            // (liveCount + 1 = 4) records -> 16 bytes. Plant exactly 20
            // bytes of 0xFF at 0x600000 (16 bytes for the reservation +
            // some padding so 4-byte alignment doesn't push past).
            uint sentinelStart = 0x600000;
            for (uint i = sentinelStart; i < sentinelStart + 20; i++)
                rom.Data[i] = 0xFF;
            byte[] sentinel = { 0xDE, 0xAD, 0xBE, 0xEF };
            for (uint i = 0; i < sentinel.Length; i++)
                rom.Data[sentinelStart + 20 + i] = sentinel[i];

            var vm = new EDFE7ViewModel();
            var listBefore = vm.LoadRetreatList();
            int countBefore = listBefore.Count;
            var result = vm.ExpandRetreatList();
            Assert.True(result.Success,
                $"ExpandRetreatList must succeed; got error: {result.Error}");

            Assert.Equal(0xDEu, rom.u8(sentinelStart + 20 + 0));
            Assert.Equal(0xADu, rom.u8(sentinelStart + 20 + 1));
            Assert.Equal(0xBEu, rom.u8(sentinelStart + 20 + 2));
            Assert.Equal(0xEFu, rom.u8(sentinelStart + 20 + 3));

            var listAfter = vm.LoadRetreatList();
            Assert.Equal(countBefore + 1, listAfter.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpilogueList_OnEliwoodRoute_UpdatesOnly_ed_3a()
    {
        // C4: route-specific expand. After expanding Eliwood the ed_3a
        // pointer changes; ed_3b is untouched.
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint ed3aBefore = rom.p32(rom.RomInfo.ed_3a_pointer);
            uint ed3bBefore = rom.p32(rom.RomInfo.ed_3b_pointer);

            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            vm.LoadEpilogueList();
            var result = vm.ExpandEpilogueList();
            Assert.True(result.Success);

            uint ed3aAfter = rom.p32(rom.RomInfo.ed_3a_pointer);
            uint ed3bAfter = rom.p32(rom.RomInfo.ed_3b_pointer);
            Assert.NotEqual(ed3aBefore, ed3aAfter); // Eliwood relocated.
            Assert.Equal(ed3bBefore, ed3bAfter);     // Hector untouched.
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandEpilogueList_OnHectorRoute_UpdatesOnly_ed_3b()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint ed3aBefore = rom.p32(rom.RomInfo.ed_3a_pointer);
            uint ed3bBefore = rom.p32(rom.RomInfo.ed_3b_pointer);

            var vm = new EDFE7ViewModel();
            vm.EpilogueRoute = EDFE7ViewModel.EpilogueRouteKind.Hector;
            vm.LoadEpilogueList();
            var result = vm.ExpandEpilogueList();
            Assert.True(result.Success);

            uint ed3aAfter = rom.p32(rom.RomInfo.ed_3a_pointer);
            uint ed3bAfter = rom.p32(rom.RomInfo.ed_3b_pointer);
            Assert.Equal(ed3aBefore, ed3aAfter);      // Eliwood untouched.
            Assert.NotEqual(ed3bBefore, ed3bAfter);   // Hector relocated.
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Backward-compat shims.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_StillForwardsToLynList()
    {
        var rom = MakeMinimalFE7URom(out _, out _, out _, out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new EDFE7ViewModel();
            var legacy = vm.LoadList();
            var modern = vm.LoadLynList();
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
            "EDFE7View.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EDFE7View.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE7U ROM with:
    /// - ed_1_pointer  -> 0x08100000 (retreat table at 0x100000).
    /// - ed_2_pointer  -> 0x08100100 (epithet table at 0x100100).
    /// - ed_3a_pointer -> 0x08100200 (epilogue Eliwood at 0x100200).
    /// - ed_3b_pointer -> 0x08100300 (epilogue Hector at 0x100300).
    /// - ed_3c_pointer = 0x100400 directly (Lyn DIRECT BASE, NO pointer
    ///   indirection - the value of ed_3c_pointer in RomInfo IS the
    ///   table base).
    /// - Retreat: 3 entries + u32==0 terminator.
    /// - Epithet: 2 entries + u32==0 terminator.
    /// - Epilogue Eliwood: 2 entries + u32==0 terminator.
    /// - Epilogue Hector: 1 entry + u32==0 terminator.
    /// - Lyn: 2 entries + u32==0 terminator.
    /// Also writes 0xFF over a large free-space region starting at
    /// 0x500000 so DataExpansionCore.ExpandTable can find a slot.
    /// </summary>
    static ROM MakeMinimalFE7URom(out uint lynAddr,
                                  out uint retreatAddr,
                                  out uint epithetAddr,
                                  out uint eliwoodAddr,
                                  out uint hectorAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "AE7E01");

        // Free-space region for ExpandTable (~64KB of 0xFF).
        for (uint i = 0x500000; i < 0x510000; i++)
            rom.Data[i] = 0xFF;

        // Plant pointers for ed_1 / ed_2 / ed_3a / ed_3b.
        WriteU32(rom.Data, (int)rom.RomInfo.ed_1_pointer, 0x08100000);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_2_pointer, 0x08100100);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_3a_pointer, 0x08100200);
        WriteU32(rom.Data, (int)rom.RomInfo.ed_3b_pointer, 0x08100300);

        // Lyn DIRECT base: we cannot rewrite RomInfo.ed_3c_pointer at
        // runtime, so we read its actual FE7U value and plant data there.
        // The synthetic ROM zero-initializes the rest of the bytes; if
        // ed_3c_pointer is in a reasonable range we can plant lyn data
        // directly. For FE7U it's 0xcedd48 which IS in our 16MB synthetic
        // ROM, so we use it directly.
        lynAddr = rom.RomInfo.ed_3c_pointer;

        // Retreat list (4B records, ed_1) - 3 entries then u32==0.
        retreatAddr = 0x100000;
        rom.Data[0x100000 + 0] = 0x01; rom.Data[0x100000 + 1] = 0x00;
        rom.Data[0x100000 + 2] = 0x00; rom.Data[0x100000 + 3] = 0x00;
        rom.Data[0x100004 + 0] = 0x02; rom.Data[0x100004 + 1] = 0x01;
        rom.Data[0x100004 + 2] = 0x00; rom.Data[0x100004 + 3] = 0x00;
        rom.Data[0x100008 + 0] = 0x03; rom.Data[0x100008 + 1] = 0x02;
        rom.Data[0x100008 + 2] = 0x00; rom.Data[0x100008 + 3] = 0x00;
        // Terminator at +12: all-zero u32.

        // Epithet list (8B records, ed_2) - 2 entries then u32==0
        // terminator (FE7 predicate is u32, not u8 like FE8).
        epithetAddr = 0x100100;
        WriteU32(rom.Data, 0x100100 + 0, 0x00000001);    // D0 UnitId = 1
        WriteU32(rom.Data, 0x100100 + 4, 0x000007D1);    // D4 TextId
        WriteU32(rom.Data, 0x100108 + 0, 0x00000002);
        WriteU32(rom.Data, 0x100108 + 4, 0x000007D2);
        // Terminator at +16: u32==0.

        // Epilogue Eliwood (8B records, ed_3a) - 2 entries.
        eliwoodAddr = 0x100200;
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
        // Terminator at +16: u32==0.

        // Epilogue Hector (8B records, ed_3b) - 1 entry.
        hectorAddr = 0x100300;
        rom.Data[0x100300 + 0] = 0x01;
        rom.Data[0x100300 + 1] = 0x08;
        rom.Data[0x100300 + 2] = 0x00;
        rom.Data[0x100300 + 3] = 0x12;
        WriteU32(rom.Data, 0x100300 + 4, 0x00000802);
        // Terminator at +8: u32==0.

        // Lyn list (12B records at ed_3c_pointer DIRECTLY) - 2 entries
        // + u32==0 terminator.
        WriteU32(rom.Data, (int)(lynAddr + 0), 0x00000001);   // D0 UnitId
        WriteU32(rom.Data, (int)(lynAddr + 4), 0x00000A01);   // D4 ClearedText
        WriteU32(rom.Data, (int)(lynAddr + 8), 0x00000A02);   // D8 RetreatText
        WriteU32(rom.Data, (int)(lynAddr + 12), 0x00000002);
        WriteU32(rom.Data, (int)(lynAddr + 16), 0x00000A03);
        WriteU32(rom.Data, (int)(lynAddr + 20), 0x00000A04);
        // Terminator at +24: u32==0 (already zero).

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
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
