// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for MonsterItemViewerView. (#394)
//
// The WinForms `MonsterItemForm` has 3 lists in one form (Item / Probability /
// Holdings). Before this PR the Avalonia counterpart `MonsterItemViewerView`
// only covered the 5-byte item list, giving HIGH density (-89.1 %, AV 14 vs
// WF 129) and 37 WF-only labels. This test suite locks in the 3-tab rebuild
// per the plan accepted on issue #394.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the MonsterItemForm parity raise (#394) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap.
/// </summary>
[Collection("SharedState")]
public class MonsterItemParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the LOW verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 129 control instantiations (per 2026-05-21
    /// density sweep). To enter the LOW band we need
    /// AV >= ceil(129 * 0.75) = 97.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveLowVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 129;
        int lowThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 97
        Assert.True(avCount >= lowThreshold,
            $"AV control count {avCount} must be >= {lowThreshold} " +
            $"(75 % of WF={WfControlCount}) to enter the LOW verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - tab structure assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTabControl_With3Tabs()
    {
        string axaml = ReadAxaml();
        // Must declare TabControl with 3 TabItems.
        Assert.Contains("TabControl", axaml);
        var tabItems = Regex.Matches(axaml, @"<TabItem\b");
        Assert.True(tabItems.Count >= 3,
            $"Expected >= 3 TabItem elements, got {tabItems.Count}");
    }

    // -----------------------------------------------------------------
    // Tab 1 — Item Table surface.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasItemTab_Surface()
    {
        string axaml = ReadAxaml();
        // Address list (legacy name preserved so existing test
        // `MonsterItemView_HasSelectFirstItem` keeps passing).
        Assert.Contains("Name=\"EntryList\"", axaml);
        // 5 item ID pickers (Tab 1).
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Item_Item{i}_Input\"",
                axaml);
        }
        // Item-tab write button.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Item_Write_Button\"",
            axaml);
        // Item-tab address controls (Address + BlockSize + SelectedAddress).
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Item_Addr_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Item_BlockSize_Label\"",
            axaml);
    }

    [Fact]
    public void View_HasItemExpandButton()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Item_Expand_Button\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // Tab 2 — Probability Table surface.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasProbabilityTab_Surface()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Prob_Entry_List\"",
            axaml);
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Prob_Prob{i}_Input\"",
                axaml);
        }
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Prob_Write_Button\"",
            axaml);
        // Tab 2 must show a Sum total label so the user can see when
        // probabilities sum to 100% (mirrors WF `N1_SUM` + `label64`).
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Prob_Sum_Label\"",
            axaml);
    }

    [Fact]
    public void View_HasProbabilityExpandButton()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Prob_Expand_Button\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // Tab 3 — Holdings Table surface.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasHoldingsTab_Surface()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Entry_List\"",
            axaml);
        // Set 1 (items 1..5 + 5 prob + 5 itemprob).
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Item{i}_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Prob{i}_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_ItemProb{i}_Input\"",
                axaml);
        }
        // Set 2 (items 6..10 + 5 prob + 5 itemprob).
        for (int i = 6; i <= 10; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Item{i}_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Prob{i}_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_ItemProb{i}_Input\"",
                axaml);
        }
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Sum1_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Sum2_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Write_Button\"",
            axaml);
    }

    [Fact]
    public void View_HasHoldingsExpandButton()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_Expand_Button\"",
            axaml);
    }

    [Fact]
    public void View_HasHoldingClassPicker()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_ClassId_Input\"",
            axaml);
    }

    /// <summary>
    /// B31 is the trailing unknown byte in the 32-byte holdings record.
    /// WF labels it with the literal "00" (per designer.cs label56). The
    /// AV view must surface it as an editable NumericUpDown AND must
    /// render the "00" label literal so the labels-sweep "00" entry is
    /// covered (Copilot CLI v1 review item #2).
    /// </summary>
    [Fact]
    public void View_HasHoldingB31_Surface()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MonsterItemViewer_Holding_B31_Input\"",
            axaml);
        // The "00" literal must appear in the AXAML so the labels-sweep
        // scanner picks it up.
        Assert.Matches(new Regex(@"Text=""00"""), axaml);
    }

    // -----------------------------------------------------------------
    // Cross-list selection parity (Copilot v1 review item #3).
    // -----------------------------------------------------------------

    /// <summary>
    /// WF wires `N2_B1..N2_B10` value-changed/Enter to select the same-id
    /// row in the Item list. The AV equivalent is the per-IdField
    /// `JumpRequested` event firing `HoldingItem*_Jump` handlers that
    /// switch the Item tab and call `EntryList.SelectByIndex(...)`.
    /// </summary>
    [Fact]
    public void View_HoldingItemFields_JumpToItemTab()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // Each of the 10 holding-item fields must wire a Jump handler
        // that ultimately calls SelectByIndex on the item list.
        for (int i = 1; i <= 10; i++)
        {
            Assert.Contains($"HoldingItem{i}_Jump", code);
        }
        // The handler body must dispatch via the shared
        // `JumpToItemRow(uint)` helper so the test surface is small.
        Assert.Contains("void JumpToItemRow(", code);
    }

    /// <summary>
    /// WF wires `N2_B21..N2_B30` value-changed/Enter to select the same-id
    /// row in the Probability list. AV mirrors via `HoldingItemProb*_Jump`
    /// handlers.
    /// </summary>
    [Fact]
    public void View_HoldingItemProbFields_JumpToProbTab()
    {
        string code = File.ReadAllText(CodeBehindPath());
        for (int i = 1; i <= 10; i++)
        {
            Assert.Contains($"HoldingItemProb{i}_Jump", code);
        }
        Assert.Contains("void JumpToProbRow(", code);
    }

    // -----------------------------------------------------------------
    // Code-behind / write-handler assertions.
    // -----------------------------------------------------------------

    /// <summary>
    /// Each of the 3 write handlers MUST wrap the VM write call in
    /// `_undoService.Begin / Commit / Rollback` so the ROM mutation is
    /// undoable atomically.
    /// </summary>
    [Fact]
    public void View_AllWriteHandlers_UseUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // 3 distinct write handlers (Item / Probability / Holdings).
        Assert.Matches(new Regex(
            @"void ItemWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteMonsterItem\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
        Assert.Matches(new Regex(
            @"void ProbabilityWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteMonsterItemProbability\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
        Assert.Matches(new Regex(
            @"void HoldingsWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteMonsterItemHoldings\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic ROM).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadItemList_StopsAt0xFF()
    {
        var (rom, _, _, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            var list = vm.LoadMonsterItemList();
            // 3 entries seeded — terminator at offset 15 (0xFF).
            Assert.Equal(3, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadProbabilityList_StopsAt0xFF()
    {
        var (rom, _, _, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            var list = vm.LoadMonsterItemProbabilityList();
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadHoldingsList_StopsAt0xFF()
    {
        var (rom, _, _, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            var list = vm.LoadMonsterItemHoldingsList();
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteItem_RoundTrips()
    {
        var (rom, itemBase, _, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            vm.LoadMonsterItem(itemBase + 5); // row #1
            // Mutate every field.
            vm.ItemId = 0x42;
            vm.DropRate = 0x33;
            vm.Unknown1 = 0x11;
            vm.Unknown2 = 0x22;
            vm.Unknown3 = 0x55;
            vm.WriteMonsterItem();

            // Verify byte layout.
            Assert.Equal((uint)0x42, rom.u8(itemBase + 5 + 0));
            Assert.Equal((uint)0x33, rom.u8(itemBase + 5 + 1));
            Assert.Equal((uint)0x11, rom.u8(itemBase + 5 + 2));
            Assert.Equal((uint)0x22, rom.u8(itemBase + 5 + 3));
            Assert.Equal((uint)0x55, rom.u8(itemBase + 5 + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteProbability_RoundTrips()
    {
        var (rom, _, probBase, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            vm.LoadMonsterItemProbability(probBase); // row #0
            vm.Prob1 = 10;
            vm.Prob2 = 20;
            vm.Prob3 = 30;
            vm.Prob4 = 25;
            vm.Prob5 = 15;
            vm.WriteMonsterItemProbability();

            Assert.Equal((uint)10, rom.u8(probBase + 0));
            Assert.Equal((uint)20, rom.u8(probBase + 1));
            Assert.Equal((uint)30, rom.u8(probBase + 2));
            Assert.Equal((uint)25, rom.u8(probBase + 3));
            Assert.Equal((uint)15, rom.u8(probBase + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteHoldings_RoundTrips_AllBytes()
    {
        var (rom, _, _, holdBase) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            vm.LoadMonsterItemHoldings(holdBase); // row #0

            // Class + 10 items + 10 probs + 10 item-probs + 1 unknown = 32 bytes.
            vm.ClassId = 0x05;
            vm.HoldingItem1 = 0x11;
            vm.HoldingItem2 = 0x12;
            vm.HoldingItem3 = 0x13;
            vm.HoldingItem4 = 0x14;
            vm.HoldingItem5 = 0x15;
            vm.HoldingItem6 = 0x16;
            vm.HoldingItem7 = 0x17;
            vm.HoldingItem8 = 0x18;
            vm.HoldingItem9 = 0x19;
            vm.HoldingItem10 = 0x1A;
            vm.HoldingProb1 = 21;
            vm.HoldingProb2 = 22;
            vm.HoldingProb3 = 23;
            vm.HoldingProb4 = 24;
            vm.HoldingProb5 = 25;
            vm.HoldingProb6 = 26;
            vm.HoldingProb7 = 27;
            vm.HoldingProb8 = 28;
            vm.HoldingProb9 = 29;
            vm.HoldingProb10 = 30;
            vm.HoldingItemProb1 = 41;
            vm.HoldingItemProb2 = 42;
            vm.HoldingItemProb3 = 43;
            vm.HoldingItemProb4 = 44;
            vm.HoldingItemProb5 = 45;
            vm.HoldingItemProb6 = 46;
            vm.HoldingItemProb7 = 47;
            vm.HoldingItemProb8 = 48;
            vm.HoldingItemProb9 = 49;
            vm.HoldingItemProb10 = 50;
            vm.B31 = 0xAB;
            vm.WriteMonsterItemHoldings();

            // B0 = class.
            Assert.Equal((uint)0x05, rom.u8(holdBase + 0));
            // B1..B10 = items.
            for (uint i = 0; i < 10; i++)
                Assert.Equal((uint)(0x11 + i), rom.u8(holdBase + 1 + i));
            // B11..B20 = probs.
            for (uint i = 0; i < 10; i++)
                Assert.Equal((uint)(21 + i), rom.u8(holdBase + 11 + i));
            // B21..B30 = item-probs.
            for (uint i = 0; i < 10; i++)
                Assert.Equal((uint)(41 + i), rom.u8(holdBase + 21 + i));
            // B31 = unknown.
            Assert.Equal((uint)0xAB, rom.u8(holdBase + 31));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Helper that maps a holdings-item field index 1..10 to the matching
    /// row address in the Item list. Used by `JumpToItemRow`.
    /// </summary>
    [Fact]
    public void ViewModel_GetItemRowAddressFromIndex()
    {
        var (rom, itemBase, _, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            // Row #0 = itemBase, row #1 = itemBase + 5, row #2 = itemBase + 10.
            Assert.Equal(itemBase + 0, vm.GetItemRowAddress(0));
            Assert.Equal(itemBase + 5, vm.GetItemRowAddress(1));
            Assert.Equal(itemBase + 10, vm.GetItemRowAddress(2));
            // Out-of-range row returns 0.
            Assert.Equal(0u, vm.GetItemRowAddress(999));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_GetProbRowAddressFromIndex()
    {
        var (rom, _, probBase, _) = MakeMonsterRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterItemViewerViewModel();
            Assert.Equal(probBase + 0, vm.GetProbabilityRowAddress(0));
            Assert.Equal(probBase + 5, vm.GetProbabilityRowAddress(1));
            Assert.Equal(0u, vm.GetProbabilityRowAddress(999));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ListParityHelper coverage (Copilot v1 review item #4).
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_DiscoversProbabilityList()
    {
        // The probability + holdings list builders must be registered so
        // the generic list-parity sweep sees three triplets, not one.
        string code = File.ReadAllText(ListParityHelperPath());
        Assert.Contains("BuildMonsterItemProbabilityList", code);
        Assert.Contains("BuildMonsterItemHoldingsList", code);
    }

    [Fact]
    public void ListParityHelper_ProbabilityList_UsesProbabilityPointer()
    {
        string code = File.ReadAllText(ListParityHelperPath());
        Assert.Matches(new Regex(
            @"BuildMonsterItemProbabilityList[\s\S]{0,400}monster_item_probability_pointer",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void ListParityHelper_HoldingsList_UsesTablePointer()
    {
        string code = File.ReadAllText(ListParityHelperPath());
        Assert.Matches(new Regex(
            @"BuildMonsterItemHoldingsList[\s\S]{0,400}monster_item_table_pointer",
            RegexOptions.Singleline), code);
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with three small Monster Item tables.
    /// </summary>
    /// <returns>
    /// (rom, itemBase, probBase, holdBase) — the three table bases (ROM
    /// offsets, not GBA pointers).
    /// </returns>
    static (ROM rom, uint itemBase, uint probBase, uint holdBase) MakeMonsterRom()
    {
        var rom = new ROM();
        // FE8U needs 16 MB to register as BE8E01.
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Place 3 small tables in a free area.
        uint itemBase = 0x200000;
        uint probBase = 0x210000;
        uint holdBase = 0x220000;

        // Item table — 5-byte rows, 3 valid entries then 0xFF terminator.
        for (uint i = 0; i < 3; i++)
        {
            uint addr = itemBase + i * 5;
            rom.Data[addr + 0] = (byte)(0x10 + i); // item id
            rom.Data[addr + 1] = (byte)(0x20 + i); // drop rate
            rom.Data[addr + 2] = 0x00;
            rom.Data[addr + 3] = 0x00;
            rom.Data[addr + 4] = 0x00;
        }
        rom.Data[itemBase + 15] = 0xFF;

        // Probability table — 5-byte rows, 2 valid entries.
        for (uint i = 0; i < 2; i++)
        {
            uint addr = probBase + i * 5;
            rom.Data[addr + 0] = (byte)(20 + i);
            rom.Data[addr + 1] = (byte)(25 + i);
            rom.Data[addr + 2] = (byte)(30 + i);
            rom.Data[addr + 3] = (byte)(15 + i);
            rom.Data[addr + 4] = (byte)(10 + i);
        }
        rom.Data[probBase + 10] = 0xFF;

        // Holdings table — 32-byte rows, 2 valid entries.
        for (uint i = 0; i < 2; i++)
        {
            uint addr = holdBase + i * 32;
            rom.Data[addr + 0] = (byte)(0x01 + i); // class id (non-zero, non-0xFF)
            for (uint b = 1; b < 32; b++)
                rom.Data[addr + b] = (byte)((0x10 + b) & 0xFE); // ensure not 0xFF
        }
        rom.Data[holdBase + 2 * 32] = 0xFF;

        // Patch the three pointer slots at FE8U offsets so
        // rom.p32(...) resolves to our synthetic bases.
        WriteU32(rom.Data, 0x783f0, 0x08000000u | itemBase); // monster_item_item_pointer
        WriteU32(rom.Data, 0x783ec, 0x08000000u | probBase); // monster_item_probability_pointer
        WriteU32(rom.Data, 0x78360, 0x08000000u | holdBase); // monster_item_table_pointer

        return (rom, itemBase, probBase, holdBase);
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string AxamlPath() => Path.Combine(AvaloniaDir, "Views", "MonsterItemViewerView.axaml");
    static string CodeBehindPath() => Path.Combine(AvaloniaDir, "Views", "MonsterItemViewerView.axaml.cs");
    static string ViewModelPath() => Path.Combine(AvaloniaDir, "ViewModels", "MonsterItemViewerViewModel.cs");
    static string ListParityHelperPath() => Path.Combine(AvaloniaDir, "Services", "ListParityHelper.cs");

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string AvaloniaDir
    {
        get
        {
            // Walk up from the test binary location until we find the
            // FEBuilderGBA.Avalonia source directory.
            string baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "FEBuilderGBA.Avalonia");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                $"Could not locate FEBuilderGBA.Avalonia/ from base {baseDir}");
        }
    }
}
