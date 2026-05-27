// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for MapStyleEditorView. (#391)
//
// The WinForms `MapStyleEditorForm` shipped 153 controls vs the Avalonia
// `MapStyleEditorView` stub of 8 (HIGH density -94.8%) and 45 WF-only
// labels (per the 2026-05-27 sweep). This test suite locks in the
// 3-tab rebuild per the v3 plan accepted on issue #391:
//
//   - Tab 1 Map Style: ObjAddress / Pointer displays + functional Write
//   - Tab 2 Palette:   16 read-only RGB rows + KnownGap palette buttons
//   - Tab 3 Chipset:   4 tile-slot TSA grids (split + raw W round-trip) +
//                      KnownGap chipset buttons
//
// All non-functional buttons must be disabled with a #374 tooltip
// (Copilot CLI v1 review item 4); split TSA fields must stay
// synchronized with the raw word (review item 2). LZ77-backed CONFIG
// writes and PLIST-aware palette writes are deliberately out of scope
// (review items 1 + 3).

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
/// Tests proving the MapStyleEditorForm parity raise (#391) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests
/// mutate CoreState.ROM. Without serialization, xUnit's per-class
/// parallel runner can race a sibling test's ROM swap.
/// </summary>
[Collection("SharedState")]
public class MapStyleEditorParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 153 control instantiations (per 2026-05-27
    /// density sweep). To leave the HIGH band we need
    /// AV >= ceil(153 * 0.5) + 1 = 77 so the actual delta
    /// is (77-153)/153 = -49.67% which is below the HIGH boundary at -50.0%.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // Strict MEDIUM boundary: 77 gives -49.67% (MEDIUM); 76 gives
        // -50.33% (HIGH). Use 77 as the minimum gate.
        const int MinAvControls = 77;
        Assert.True(avCount >= MinAvControls,
            $"AV control count {avCount} must be >= {MinAvControls} (strict MEDIUM cutoff, WF=153)");
    }

    // -----------------------------------------------------------------
    // Tab structure (3 tabs).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTabControl_With3Tabs()
    {
        string axaml = ReadAxaml();
        Assert.Contains("TabControl", axaml);
        var tabItems = Regex.Matches(axaml, @"<TabItem\b");
        Assert.True(tabItems.Count >= 3,
            $"Expected >= 3 TabItem elements, got {tabItems.Count}");
    }

    // -----------------------------------------------------------------
    // Tab 1 - Map Style surface.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasMapStyleTab_Surface()
    {
        string axaml = ReadAxaml();
        // MapStyle combo box (mirrors WF MapStyle ComboBox).
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_MapStyle_Combo\"",
            axaml);
        // Pointer displays.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ObjAddress_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ObjAddress2_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_PaletteAddress_Label\"",
            axaml);
        // Functional OBJ Tile Pointer Write surface.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_Write_Button\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ObjPtr_Input\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // Tab 2 - Palette surface (16 RGB rows + selectors + buttons).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasPaletteTab_Surface()
    {
        string axaml = ReadAxaml();
        // PaletteCombo + PaletteTypeCombo (mirrors WF).
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_Palette_Combo\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_PaletteType_Combo\"",
            axaml);
        // 16 RGB rows -> 16 R, 16 G, 16 B NumericUpDowns.
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Color{i}_R_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Color{i}_G_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Color{i}_B_Input\"",
                axaml);
        }
    }

    // -----------------------------------------------------------------
    // Tab 3 - Chipset surface (4 tile-slot TSA grids + buttons).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasChipsetTab_Surface()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ConfigNo_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ConfigTerrain_Combo\"",
            axaml);
        // 4 slots (0/2/4/6) x 5 fields (TSA_X, TSA_Y, TSA_PALETTE, TSA_FLIP, W).
        int[] slots = { 0, 2, 4, 6 };
        foreach (int slot in slots)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Slot{slot}_TSA_X_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Slot{slot}_TSA_Y_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Slot{slot}_TSA_PALETTE_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Slot{slot}_TSA_FLIP_Input\"",
                axaml);
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Slot{slot}_W_Input\"",
                axaml);
        }
    }

    // -----------------------------------------------------------------
    // KnownGap buttons - the disabled / non-functional ROM-mutating
    // controls (Copilot CLI v1 review item 4).
    // -----------------------------------------------------------------

    /// <summary>
    /// Single authoritative list of every KnownGap-disabled button.
    /// Each button MUST appear with IsEnabled="False" AND a tooltip
    /// referencing #374 in the same element. Adding/removing a disabled
    /// button without updating this list (or via View_NoExtraDisabledButtons)
    /// breaks the test (Copilot v2 review item 4 enumeration cleanup).
    /// </summary>
    static readonly string[] KnownGapButtonIds =
    {
        // 1 button remains deferred after #704 partial slice. ObjImport
        // tracks the new follow-up #710 (needs MapStyleEditorImportImageOptionForm
        // port + 4bpp encoding + FE7 obj2 split handling).
        // PaletteExport/Import/Clipboard/ObjExport/Undo became functional
        // in #672 Slice A; Redo and MapChip Export became functional in
        // #692 partial slice; MapChip Import became functional in #704
        // (see View_<Name>_Button_IsEnabled +
        // View_<Name>_Click_HandlerWired tests below).
        //
        // PaletteWrite is functional via #660 first slice (tracked positively
        // by View_FunctionalPaletteWriteButton_IsEnabled); CopyTile / CopyType /
        // Paste / ConfigWrite became functional in #671 (tracked positively
        // by View_ChipsetTab_FunctionalControls_AreEnabled_WhenCanEdit).
        "MapStyleEditor_ObjImport_Button",
    };

    /// <summary>
    /// Per-button mapping of remaining KnownGap buttons to the follow-up
    /// issue their tooltip must reference. OBJ Import was promoted to its
    /// own follow-up #710 in #704 because it needs substantially more work
    /// (option dialog + 4bpp encoding + FE7 obj2) than the original #692
    /// bucket assumed.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, string> KnownGapTooltipIssue = new()
    {
        ["MapStyleEditor_ObjImport_Button"] = "#710",
    };

    [Fact]
    public void View_KnownGapButtons_AreDisabledAndTagged()
    {
        string axaml = ReadAxaml();
        foreach (string id in KnownGapButtonIds)
        {
            // Each button must appear with IsEnabled="False" AND a
            // tooltip referencing its tracked follow-up issue in the same
            // element block. Allow attributes in any order via DOTALL flag.
            var idAttr = $"AutomationProperties.AutomationId=\"{id}\"";
            Assert.Contains(idAttr, axaml);

            // Locate the <Button ... /> element containing this id and
            // verify the element body has IsEnabled="False" and the tracked
            // follow-up tooltip. Per #704: OBJ Import moved from #692 to
            // the new #710; KnownGapTooltipIssue is the per-button source
            // of truth so future additions can carry their own issue.
            var elementPattern = new Regex(
                @"<Button(?:\s[^/>]*?)?" + Regex.Escape(idAttr) + @"[\s\S]*?/?>",
                RegexOptions.None);
            Match m = elementPattern.Match(axaml);
            Assert.True(m.Success, $"Cannot locate <Button> element for {id}");

            string elementText = m.Value;
            Assert.True(
                elementText.Contains("IsEnabled=\"False\""),
                $"KnownGap button {id} must have IsEnabled=\"False\"; element was: {elementText}");

            string expectedIssue = KnownGapTooltipIssue[id];
            Assert.True(
                elementText.Contains(expectedIssue),
                $"KnownGap button {id} must have a {expectedIssue} follow-up tooltip; element was: {elementText}");
        }
    }

    /// <summary>
    /// Proves no disabled <Button> slips in that isn't on the KnownGap
    /// list. The count of IsEnabled="False" Buttons must equal exactly
    /// the KnownGap list length — adding a new disabled button without
    /// adding it to <see cref="KnownGapButtonIds"/> breaks this test
    /// (Copilot v2 review item 4 enumeration cleanup).
    /// </summary>
    [Fact]
    public void View_NoExtraDisabledButtons()
    {
        string axaml = ReadAxaml();
        var disabledButtonPattern = new Regex(
            @"<Button[^>]*IsEnabled=""False""[^>]*",
            RegexOptions.None);
        int disabledCount = disabledButtonPattern.Matches(axaml).Count;
        Assert.Equal(KnownGapButtonIds.Length, disabledCount);
    }

    /// <summary>The functional OBJ Tile Pointer Write button is enabled.</summary>
    [Fact]
    public void View_FunctionalWriteButton_IsEnabled()
    {
        string axaml = ReadAxaml();
        // The Write button is the one functional ROM-mutating control;
        // it must NOT carry IsEnabled="False".
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""MapStyleEditor_Write_Button""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, "MapStyleEditor_Write_Button must exist");
        Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
    }

    /// <summary>
    /// The single functional Write handler wraps its VM call in
    /// `_undoService.Begin / Commit / Rollback` so the ROM mutation is
    /// undoable atomically (gap-sweep undo acceptance criterion).
    /// </summary>
    [Fact]
    public void View_Write_Click_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void Write_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?_vm\.Write\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// Palette Write button became functional in the #660 first slice.
    /// Mirror View_FunctionalWriteButton_IsEnabled: the button MUST NOT
    /// carry `IsEnabled="False"`. Catches regressions that would re-disable
    /// the control without updating KnownGapButtonIds.
    /// </summary>
    [Fact]
    public void View_FunctionalPaletteWriteButton_IsEnabled()
    {
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""MapStyleEditor_PaletteWrite_Button""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, "MapStyleEditor_PaletteWrite_Button must exist");
        Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
    }

    /// <summary>
    /// Palette Write handler must wrap the VM call in
    /// `_undoService.Begin -> _vm.WritePalette() -> Commit / Rollback`
    /// so the 32-byte palette mutation is atomically undoable.
    /// Mirrors View_Write_Click_UsesUndoService (#660 review item 3).
    ///
    /// The shape check requires BOTH a Commit AND a Rollback path
    /// somewhere in the method body (Copilot inline review v1: the
    /// previous "Commit|Rollback" regex would pass even if Commit
    /// were deleted as long as Rollback survived). We verify Begin
    /// precedes WritePalette, and that Commit AND Rollback both
    /// appear after the call site.
    /// </summary>
    [Fact]
    public void View_PaletteWrite_Click_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // Extract the PaletteWrite_Click method body so we don't
        // accidentally match Commit/Rollback from a neighbouring method.
        var bodyMatch = Regex.Match(code,
            @"void PaletteWrite_Click[\s\S]*?(?=\n\s{8}(static\s|public\s|void\s|/// ))",
            RegexOptions.Singleline);
        Assert.True(bodyMatch.Success, "PaletteWrite_Click method not found");
        string body = bodyMatch.Value;

        Assert.Matches(new Regex(
            @"_undoService\.Begin\([^)]*\)[\s\S]*?_vm\.WritePalette\(\)",
            RegexOptions.Singleline), body);
        Assert.Contains("_undoService.Commit()", body);
        Assert.Contains("_undoService.Rollback()", body);
    }

    /// <summary>
    /// The 48 palette RGB NumericUpDown controls must no longer carry
    /// `IsEnabled="False"` (they were disabled placeholders before the
    /// #660 first slice). Catches accidental re-disabling.
    /// </summary>
    [Fact]
    public void View_PaletteNUDs_AreEnabled()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            foreach (char ch in new[] { 'R', 'G', 'B' })
            {
                var pattern = new Regex(
                    $"<NumericUpDown[^>]*MapStyleEditor_Color{i}_{ch}_Input[^>]*",
                    RegexOptions.None);
                Match m = pattern.Match(axaml);
                Assert.True(m.Success, $"NUD MapStyleEditor_Color{i}_{ch}_Input must exist");
                Assert.False(m.Value.Contains("IsEnabled=\"False\""),
                    $"NUD MapStyleEditor_Color{i}_{ch}_Input must not be disabled");
            }
        }
    }

    /// <summary>
    /// The palette tab must include a swatch column showing the current
    /// color for each of the 16 rows (#660 first slice: visual feedback
    /// for color editing).
    /// </summary>
    [Fact]
    public void View_HasPaletteSwatches()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"MapStyleEditor_Color{i}_Swatch_Image\"",
                axaml);
        }
    }

    // -----------------------------------------------------------------
    // Chipset Tab — functional controls (#671).
    // -----------------------------------------------------------------

    /// <summary>
    /// #671 acceptance: the 4 chipset buttons (CopyTile / CopyType /
    /// Paste / ConfigWrite) must NOT carry IsEnabled="False" anymore.
    /// The view enables/disables them at runtime via SetChipsetEditingEnabled
    /// based on CanEditChipsetConfig.
    /// </summary>
    [Fact]
    public void View_ChipsetTab_FunctionalControls_AreEnabled_WhenCanEdit()
    {
        string axaml = ReadAxaml();
        string[] ids =
        {
            "MapStyleEditor_CopyTile_Button",
            "MapStyleEditor_CopyType_Button",
            "MapStyleEditor_Paste_Button",
            "MapStyleEditor_ConfigWrite_Button",
        };
        foreach (string id in ids)
        {
            var pattern = new Regex(
                @"<Button[^>]*AutomationProperties\.AutomationId=""" + id + @"""[^>]*",
                RegexOptions.None);
            Match m = pattern.Match(axaml);
            Assert.True(m.Success, $"{id} must exist");
            Assert.False(m.Value.Contains("IsEnabled=\"False\""),
                $"{id} must not carry IsEnabled=\"False\" anymore (#671 made it functional)");
        }
    }

    /// <summary>
    /// #671: terrain combo + 20 slot NUDs must NOT carry IsEnabled="False"
    /// in AXAML. (The view toggles them at runtime via SetChipsetEditingEnabled.)
    /// </summary>
    [Fact]
    public void View_ChipsetTab_SlotNUDsAndTerrainCombo_AreEnabled()
    {
        string axaml = ReadAxaml();
        // Terrain combo.
        var combo = Regex.Match(
            axaml,
            "<ComboBox[^>]*MapStyleEditor_ConfigTerrain_Combo[^>]*");
        Assert.True(combo.Success, "ConfigTerrain combo must exist");
        Assert.False(combo.Value.Contains("IsEnabled=\"False\""),
            "ConfigTerrain combo must not be statically disabled");

        // 20 slot NUDs (4 slots × 5 fields: X/Y/PALETTE/FLIP/W).
        int[] slots = { 0, 2, 4, 6 };
        string[] fields = { "TSA_X", "TSA_Y", "TSA_PALETTE", "TSA_FLIP", "W" };
        foreach (int s in slots)
        {
            foreach (string f in fields)
            {
                var nud = Regex.Match(
                    axaml,
                    "<NumericUpDown[^>]*MapStyleEditor_Slot" + s + "_" + f + "_Input[^>]*");
                Assert.True(nud.Success, $"NUD Slot{s}_{f} must exist");
                Assert.False(nud.Value.Contains("IsEnabled=\"False\""),
                    $"NUD Slot{s}_{f} must not be statically disabled");
            }
        }
    }

    /// <summary>
    /// #671 KnownGap cleanup: the 4 chipset buttons + terrain combo +
    /// 20 slot NUDs must no longer advertise the legacy `#374` tooltip.
    /// </summary>
    [Fact]
    public void View_ChipsetTab_FunctionalControls_HaveNoStaleKnownGapTooltips()
    {
        string axaml = ReadAxaml();
        string[] elementIds =
        {
            "MapStyleEditor_CopyTile_Button",
            "MapStyleEditor_CopyType_Button",
            "MapStyleEditor_Paste_Button",
            "MapStyleEditor_ConfigWrite_Button",
            "MapStyleEditor_ConfigTerrain_Combo",
            "MapStyleEditor_Slot0_TSA_X_Input",
            "MapStyleEditor_Slot0_W_Input",
            "MapStyleEditor_Slot6_TSA_FLIP_Input",
        };
        foreach (string id in elementIds)
        {
            var element = Regex.Match(
                axaml,
                "<(Button|ComboBox|NumericUpDown)[^>]*" + id + "[^>]*");
            Assert.True(element.Success, $"{id} must exist");
            Assert.False(element.Value.Contains("#374"),
                $"{id} must not carry the legacy #374 tooltip");
        }
    }

    /// <summary>
    /// #671 plan WU5: the (now-neutral) help label must NOT advertise
    /// the KnownGap any more. Mirrors the wording requirement in the v6
    /// plan. The legacy AutomationId is preserved.
    /// </summary>
    [Fact]
    public void View_ChipsetTSAKnownGapLabel_HasNeutralText()
    {
        string axaml = ReadAxaml();
        var element = Regex.Match(
            axaml,
            "<TextBlock[^>]*MapStyleEditor_ChipsetTSA_KnownGap_Label[^>]*[/]>",
            RegexOptions.None);
        Assert.True(element.Success, "ChipsetTSA_KnownGap_Label must still exist");
        Assert.False(element.Value.Contains("#374"),
            "ChipsetTSA_KnownGap_Label must not advertise #374 anymore");
    }

    /// <summary>
    /// #671 plan WU5: code-behind wires Alt+T / Alt+C / Alt+V hotkeys
    /// via the KeyDown event (the simplest portable approach in Avalonia).
    /// </summary>
    [Fact]
    public void View_ChipsetTab_HasAltHotkeys()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Contains("KeyDown +=", code);
        Assert.Contains("Key.T:", code);
        Assert.Contains("Key.C:", code);
        Assert.Contains("Key.V:", code);
        Assert.Contains("KeyModifiers.Alt", code);
    }

    /// <summary>
    /// #671: ConfigNoLabel is replaced with a NumericUpDown editor named
    /// ChipsetNoInput. The hidden TextBlock keeps the legacy AutomationId
    /// for parity scans.
    /// </summary>
    [Fact]
    public void View_HasChipsetNoInput_NumericUpDown()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"ChipsetNoInput\"", axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ChipsetNo_Input\"",
            axaml);
        // Legacy label retained.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ConfigNo_Label\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // ViewModel - palette WRITE semantics (#660 first slice).
    // -----------------------------------------------------------------

    /// <summary>
    /// Round-trip: load a palette, mutate every channel, write, re-read,
    /// expect the written bytes to exactly match the in-memory state.
    /// </summary>
    [Fact]
    public void ViewModel_WritePalette_RoundTrips()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            Assert.True(vm.LoadPalette(paletteBase, paletteIndex: 1, isFog: false));

            // Mutate every row with a distinct pattern.
            for (int i = 1; i <= 16; i++)
            {
                vm.SetColorR(i, (ushort)((31 - i) & 0x1F));
                vm.SetColorG(i, (ushort)((i + 7) & 0x1F));
                vm.SetColorB(i, (ushort)((i * 2) & 0x1F));
            }

            Assert.True(vm.WritePalette());

            // Re-read the bytes and check they match.
            uint addr = paletteBase + 1 * 0x20;
            for (int i = 0; i < 16; i++)
            {
                ushort packed = (ushort)rom.u16(addr + (uint)(i * 2));
                int row = i + 1;
                int r = packed & 0x1F;
                int g = (packed >> 5) & 0x1F;
                int b = (packed >> 10) & 0x1F;
                Assert.Equal((31 - row) & 0x1F, r);
                Assert.Equal((row + 7) & 0x1F, g);
                Assert.Equal((row * 2) & 0x1F, b);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// 5-bit clamping: passing values > 0x1F into Set* must be masked to
    /// 5 bits before packing. Confirms the WritePalette path does not
    /// emit garbage halfwords for over-range inputs.
    /// </summary>
    [Fact]
    public void ViewModel_WritePalette_ClampsTo5Bits()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            Assert.True(vm.LoadPalette(paletteBase, paletteIndex: 1, isFog: false));

            // Inject over-range values (Set*R masks to 0x1F internally).
            vm.SetColorR(1, 0x3F); // expect 0x1F packed
            vm.SetColorG(1, 0x20); // expect 0x00 packed (low 5 bits)
            vm.SetColorB(1, 0x21); // expect 0x01 packed
            Assert.True(vm.WritePalette());

            uint addr = paletteBase + 1 * 0x20;
            ushort packed = (ushort)rom.u16(addr);
            Assert.Equal(0x1F, packed & 0x1F);
            Assert.Equal(0x00, (packed >> 5) & 0x1F);
            Assert.Equal(0x01, (packed >> 10) & 0x1F);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Safety: WritePalette must refuse to write when PaletteAddress
    /// is zero (no entry loaded) and must NOT touch ROM bytes.
    /// </summary>
    [Fact]
    public void ViewModel_WritePalette_RejectsZeroAddress()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        // Seed a non-zero sentinel at offset 0x200000 so we can detect
        // any accidental write.
        rom.Data[0x200000] = 0xAB;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // PaletteAddress is 0 (never loaded).
            Assert.False(vm.WritePalette());
            // Sentinel must be untouched.
            Assert.Equal(0xAB, rom.Data[0x200000]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Safety: WritePalette must refuse to write when the resolved
    /// palette slice would extend past the ROM end. Mirrors LoadPalette
    /// bounds check.
    /// </summary>
    [Fact]
    public void ViewModel_WritePalette_RejectsOutOfBounds()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // Force an unsafe PaletteAddress (last 10 bytes of ROM).
            vm.PaletteAddress = (uint)rom.Data.Length - 10;
            Assert.False(vm.WritePalette());
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Stale-state regression (#660 Copilot PR v2 review item 3): after
    /// a failed reload (out-of-bounds slice), the VM must clear its
    /// write target so a subsequent <see cref="MapStyleEditorViewModel.WritePalette"/>
    /// is refused -- otherwise it would write the new RGB values to the
    /// previous (valid) slice address, corrupting that slice.
    /// </summary>
    [Fact]
    public void ViewModel_ClearPaletteState_RefusesSubsequentWrite()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // Load a valid palette first.
            Assert.True(vm.LoadPalette(paletteBase, paletteIndex: 1, isFog: false));
            uint validAddr = vm.PaletteAddress;
            Assert.NotEqual((uint)0, validAddr);

            // Mutate the in-memory channels (simulating user edits).
            vm.SetColorR(1, 31);
            vm.SetColorG(1, 31);
            vm.SetColorB(1, 31);

            // Snapshot the original halfword at the valid slice so we can
            // verify it isn't overwritten by the stale path.
            uint originalHalfword = rom.u16(validAddr);

            // Simulate a failed reload: clear the VM's write target.
            // (The view calls this when LoadPalette returns false.)
            vm.ClearPaletteState();

            // WritePalette must refuse.
            Assert.False(vm.WritePalette());

            // The valid slice must be untouched.
            Assert.Equal(originalHalfword, rom.u16(validAddr));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Undo regression (#660 Copilot v1 review item 4): write a palette
    /// inside an ambient undo scope, then run undo and assert the original
    /// halfword is restored.
    /// </summary>
    [Fact]
    public void ViewModel_WritePalette_UndoableRestoresOriginal()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            Assert.True(vm.LoadPalette(paletteBase, paletteIndex: 1, isFog: false));

            // Capture the original halfword for row 5 (offset paletteBase + 0x20 + 8).
            uint addr5 = paletteBase + 1 * 0x20 + 4 * 2;
            ushort originalRow5 = (ushort)rom.u16(addr5);

            // Mutate row 5 to all zero.
            vm.SetColorR(5, 0);
            vm.SetColorG(5, 0);
            vm.SetColorB(5, 0);

            var undoData = CoreState.Undo.NewUndoData("test palette write");
            using (ROM.BeginUndoScope(undoData))
            {
                Assert.True(vm.WritePalette());
            }
            CoreState.Undo.Push(undoData);

            // Confirm mutation hit ROM.
            Assert.Equal((uint)0, rom.u16(addr5));

            // Run undo and verify the original halfword is restored.
            CoreState.Undo.RunUndo();
            Assert.Equal(originalRow5, (ushort)rom.u16(addr5));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // ViewModel - palette read semantics (Copilot v1 review item 3).
    // -----------------------------------------------------------------

    /// <summary>
    /// Round-trips a known 16-color RGB-555 palette through LoadPalette
    /// for a normal (non-fog) palette index. paletteBase + idx * 0x20
    /// indexing per WF semantics.
    /// </summary>
    [Fact]
    public void ViewModel_LoadPalette_ReadsRgb555_NormalPalette()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // Read palette index 1 (non-fog).
            bool ok = vm.LoadPalette(paletteBase, paletteIndex: 1, isFog: false);
            Assert.True(ok, "LoadPalette must return true for in-bounds palette");

            // The synthetic palette was seeded with R = 1+i, G = 2+i, B = 3+i
            // at index 1's offset = paletteBase + 1 * 0x20.
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal((ushort)(1 + i), vm.GetColorR(i + 1));
                Assert.Equal((ushort)(2 + i), vm.GetColorG(i + 1));
                Assert.Equal((ushort)(3 + i), vm.GetColorB(i + 1));
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Same fixture but read palette index 2 in FOG mode. The effective
    /// offset must be paletteBase + (2 + 5) * 0x20 (fog adds 5).
    /// </summary>
    [Fact]
    public void ViewModel_LoadPalette_ReadsRgb555_FogPalette()
    {
        var (rom, paletteBase) = MakeSyntheticPaletteRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // Read palette index 2 with fog (effective index = 7).
            bool ok = vm.LoadPalette(paletteBase, paletteIndex: 2, isFog: true);
            Assert.True(ok, "LoadPalette must return true for in-bounds fog palette");

            // The synthetic ROM was seeded so that palette index 7 has
            // R = 10+i, G = 20+i, B = 30+i (mod 32).
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal((ushort)((10 + i) & 0x1F), vm.GetColorR(i + 1));
                Assert.Equal((ushort)((20 + i) & 0x1F), vm.GetColorG(i + 1));
                Assert.Equal((ushort)((30 + i) & 0x1F), vm.GetColorB(i + 1));
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Out-of-bounds / zero paletteBase must return false WITHOUT
    /// writing properties. Mirrors WF behaviour where LoadPalette is
    /// a pure read.
    /// </summary>
    [Fact]
    public void ViewModel_LoadPalette_OutOfBoundsReturnsFalse()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();

            // paletteBase == 0 must return false.
            Assert.False(vm.LoadPalette(0, paletteIndex: 0, isFog: false));

            // paletteBase + idx * 0x20 beyond ROM length must return false.
            uint farBase = (uint)rom.Data.Length - 8;
            Assert.False(vm.LoadPalette(farBase, paletteIndex: 9, isFog: false));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - TSA word encode/decode (Copilot v1 review item 2).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_EncodeTsaWord_RoundTrips()
    {
        // Pick a representative non-zero packing.
        int x = 5, y = 7, palette = 11, flip = 2;
        ushort packed = MapStyleEditorViewModel.EncodeTsaWord(x, y, palette, flip);
        var (rx, ry, rp, rf) = MapStyleEditorViewModel.DecodeTsaWord(packed);
        Assert.Equal(x, rx);
        Assert.Equal(y, ry);
        Assert.Equal(palette, rp);
        Assert.Equal(flip, rf);
    }

    [Fact]
    public void ViewModel_EncodeTsaWord_MatchesGbaBitLayout()
    {
        // GBA OAM/BG-map word layout:
        //   bits 0..9  = tile index (x + y * 32)
        //   bits 10..11 = flip (h flip + v flip)
        //   bits 12..15 = palette
        ushort packed = MapStyleEditorViewModel.EncodeTsaWord(3, 4, 5, 3);
        int tile = 3 + 4 * 32; // = 131
        Assert.Equal(tile, packed & 0x3FF);
        Assert.Equal(3, (packed >> 10) & 0x3);
        Assert.Equal(5, (packed >> 12) & 0xF);
    }

    /// <summary>
    /// Setting a split field (TSA_X / TSA_Y / TSA_PALETTE / TSA_FLIP)
    /// must update the raw Slot_W to match EncodeTsaWord(...) — proves
    /// the editable split surface stays synchronized (Copilot v2 review
    /// item 2 setter synchronization).
    /// </summary>
    [Fact]
    public void ViewModel_SettingSplitField_UpdatesRawWord()
    {
        var vm = new MapStyleEditorViewModel();
        int[] slots = { 0, 2, 4, 6 };

        foreach (int s in slots)
        {
            // Set the split fields one at a time and verify the raw
            // Slot_W matches.
            vm.SetSlotSplit(s, x: 5, y: 7, palette: 11, flip: 2);
            ushort expected = MapStyleEditorViewModel.EncodeTsaWord(5, 7, 11, 2);
            Assert.Equal(expected, vm.GetSlotW(s));

            // Mutate one field and verify the raw word follows.
            vm.SetSlotSplitField(s, "X", 9);
            Assert.Equal(MapStyleEditorViewModel.EncodeTsaWord(9, 7, 11, 2), vm.GetSlotW(s));

            vm.SetSlotSplitField(s, "Y", 3);
            Assert.Equal(MapStyleEditorViewModel.EncodeTsaWord(9, 3, 11, 2), vm.GetSlotW(s));

            vm.SetSlotSplitField(s, "PALETTE", 13);
            Assert.Equal(MapStyleEditorViewModel.EncodeTsaWord(9, 3, 13, 2), vm.GetSlotW(s));

            vm.SetSlotSplitField(s, "FLIP", 1);
            Assert.Equal(MapStyleEditorViewModel.EncodeTsaWord(9, 3, 13, 1), vm.GetSlotW(s));
        }
    }

    /// <summary>
    /// Setting the raw Slot_W must repopulate the split fields to
    /// DecodeTsaWord(rawWord) — proves the reverse direction (loading
    /// a word from ROM re-populates the split editable fields).
    /// Copilot v2 review item 2.
    /// </summary>
    [Fact]
    public void ViewModel_SettingRawWord_UpdatesSplitFields()
    {
        var vm = new MapStyleEditorViewModel();
        int[] slots = { 0, 2, 4, 6 };

        foreach (int s in slots)
        {
            ushort word = MapStyleEditorViewModel.EncodeTsaWord(12, 6, 9, 3);
            vm.SetSlotW(s, word);

            var (x, y, p, f) = MapStyleEditorViewModel.DecodeTsaWord(word);
            Assert.Equal(x, vm.GetSlotSplitField(s, "X"));
            Assert.Equal(y, vm.GetSlotSplitField(s, "Y"));
            Assert.Equal(p, vm.GetSlotSplitField(s, "PALETTE"));
            Assert.Equal(f, vm.GetSlotSplitField(s, "FLIP"));
        }
    }

    // -----------------------------------------------------------------
    // Legacy automation IDs (cross-PR compatibility).
    // -----------------------------------------------------------------

    /// <summary>
    /// PLIST resolution regression (#670 Copilot CLI v1 PR review): the
    /// chip-preview palette MUST resolve via the per-style palette_plist
    /// (read from the map_setting struct at +6), NOT by reusing the
    /// obj-table index against map_pal_pointer. Asserts the VM source
    /// reads palette_plist from a map_setting entry whose obj_plist low
    /// byte matches our obj-table index, and then computes the palette
    /// table slot as palTableBase + palette_plist * 4.
    /// </summary>
    [Fact]
    public void ViewModel_LoadEntry_ResolvesPalettePlistFromMapSetting()
    {
        string code = File.ReadAllText(ViewModelPath());
        // Must enumerate map_setting entries to find the matching obj_plist.
        Assert.Contains("MapSettingCore.MakeMapIDList(rom)", code);
        // Must read palette_plist from map_setting +6 and config_plist from +7.
        Assert.Contains("rom.u8(map.addr + 6)", code);
        Assert.Contains("rom.u8(map.addr + 7)", code);
        // Must NOT compute palette/config slot as obj-index*4 anymore.
        // (Old code used `paletteTableBase + index * 4`.)
        Assert.DoesNotContain("paletteTableBase + index * 4", code);
        Assert.DoesNotContain("configTableBase + index * 4", code);
    }

    /// <summary>
    /// Map Chip Preview thumbnail (#670) must be a real GbaImageControl,
    /// not the pre-#670 placeholder TextBlock. The control name
    /// `MapChipPreviewImage` is referenced from the code-behind
    /// `RefreshChipPreview()` helper, and the new AutomationId
    /// `MapStyleEditor_MapChipPreview_Image` is what tests / harnesses
    /// should target going forward.
    /// </summary>
    [Fact]
    public void View_MapChipPreview_IsGbaImageControl_NotPlaceholder()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"MapChipPreviewImage\"", axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_MapChipPreview_Image\"",
            axaml);
        // The legacy MapStyleEditor_MapChipPreview_Label AutomationId is
        // retained on a hidden TextBlock so older parity scans don't drop.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_MapChipPreview_Label\"",
            axaml);
    }

    /// <summary>
    /// The pre-#391 view exposed five AutomationIds that existing harnesses
    /// rely on. The rebuild must preserve them all (test pins this).
    /// </summary>
    [Fact]
    public void View_RetainsLegacyAutomationIds()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_Entry_List\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_Addr_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ObjPtr_Input\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_ConfigPtr_Label\"",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"MapStyleEditor_Write_Button\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // #672 Slice A + #692 partial slice — 7 newly-functional buttons.
    // #672 Slice A: PaletteExport / PaletteImport / PaletteClipboard /
    // ObjExport / Undo. #692 partial slice: Redo + MapChip Export. The
    // 2 remaining buttons (OBJ Import + MapChip Import) are tracked by
    // the follow-up issue filed before the #692 partial-slice PR.
    // -----------------------------------------------------------------

    public static IEnumerable<object[]> Slice672ButtonIds()
    {
        yield return new object[] { "MapStyleEditor_PaletteExport_Button", "PaletteExport_Click" };
        yield return new object[] { "MapStyleEditor_PaletteImport_Button", "PaletteImport_Click" };
        yield return new object[] { "MapStyleEditor_PaletteClipboard_Button", "PaletteClipboard_Click" };
        yield return new object[] { "MapStyleEditor_ObjExport_Button", "ObjExport_Click" };
        yield return new object[] { "MapStyleEditor_Undo_Button", "Undo_Click" };
        // #692 partial slice additions.
        yield return new object[] { "MapStyleEditor_Redo_Button", "Redo_Click" };
        yield return new object[] { "MapStyleEditor_MapChipExport_Button", "MapChipExport_Click" };
    }

    /// <summary>
    /// Each of the 5 #672 Slice A buttons must NOT carry IsEnabled="False"
    /// in AXAML (parity with the #660 / #671 enable pattern).
    /// </summary>
    [Theory]
    [MemberData(nameof(Slice672ButtonIds))]
    public void View_Slice672_Button_IsEnabled(string id, string handler)
    {
        _ = handler; // signal that the handler name is checked elsewhere
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""" + Regex.Escape(id) + @"""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, $"{id} must exist");
        Assert.False(m.Value.Contains("IsEnabled=\"False\""),
            $"{id} must not carry IsEnabled=\"False\" anymore (#672 Slice A made it functional)");
        // Stale #374 tooltips must NOT remain on the 5 #672 buttons.
        Assert.False(m.Value.Contains("#374"),
            $"{id} must not carry the legacy #374 tooltip (#672 Slice A enabled it)");
    }

    /// <summary>
    /// Each of the 5 #672 Slice A buttons must wire its Click attribute to
    /// the matching handler in AXAML. This catches accidental disconnects
    /// between the XAML and the code-behind handler.
    /// </summary>
    [Theory]
    [MemberData(nameof(Slice672ButtonIds))]
    public void View_Slice672_Click_HandlerWired(string id, string handler)
    {
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""" + Regex.Escape(id) + @"""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, $"{id} must exist");
        Assert.Contains($"Click=\"{handler}\"", m.Value);

        // And the handler method must exist in the code-behind.
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void " + Regex.Escape(handler) + @"\(object\?",
            RegexOptions.None), code);
    }

    /// <summary>
    /// Undo handler must guard on <c>CoreState.Undo?.Postion &gt; 0</c>
    /// before calling RunUndo (v2 Copilot review item 2 — silently no-op
    /// when Position is 0 misleads the user with a false "Undo applied"
    /// toast). The code-behind must read Postion before RunUndo.
    /// </summary>
    [Fact]
    public void View_Undo_Click_GuardsOnPostion()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The Undo_Click body must reference Postion and bail to ShowInfo
        // ("Nothing to undo") before RunUndo() is called.
        var bodyMatch = Regex.Match(code,
            @"void Undo_Click[\s\S]*?(?=\n\s{8}(static\s|public\s|void\s|/// |\}))",
            RegexOptions.Singleline);
        Assert.True(bodyMatch.Success, "Undo_Click method not found");
        string body = bodyMatch.Value;
        Assert.Contains("Postion", body);
        Assert.Contains("Nothing to undo", body);
        // The guard must precede RunUndo() in source order.
        int postionIdx = body.IndexOf("Postion", StringComparison.Ordinal);
        int runUndoIdx = body.IndexOf("RunUndo()", StringComparison.Ordinal);
        Assert.True(postionIdx >= 0 && runUndoIdx >= 0,
            "Undo_Click must reference Postion and RunUndo()");
        Assert.True(postionIdx < runUndoIdx,
            "Postion guard must precede RunUndo() call");
    }

    // -----------------------------------------------------------------
    // #692 partial slice — Redo + MapChip Export specific shape checks.
    // -----------------------------------------------------------------

    /// <summary>
    /// Redo handler must guard on <see cref="Undo.CanRedo"/> before calling
    /// <see cref="Undo.RunRedo"/> (mirrors <see cref="Undo_Click"/>'s
    /// Postion-guard pattern) and emit a "Nothing to redo" toast on the
    /// no-op path. The guard must precede RunRedo() in source order so a
    /// silently no-op rollback never falsely claims "Redo applied".
    /// </summary>
    [Fact]
    public void View_Redo_Click_GuardsOnCanRedo()
    {
        string code = File.ReadAllText(CodeBehindPath());
        var bodyMatch = Regex.Match(code,
            @"void Redo_Click[\s\S]*?(?=\n\s{8}(static\s|public\s|void\s|/// |\}))",
            RegexOptions.Singleline);
        Assert.True(bodyMatch.Success, "Redo_Click method not found");
        string body = bodyMatch.Value;
        Assert.Contains("CanRedo", body);
        Assert.Contains("Nothing to redo", body);
        int canRedoIdx = body.IndexOf("CanRedo", StringComparison.Ordinal);
        int runRedoIdx = body.IndexOf("RunRedo()", StringComparison.Ordinal);
        Assert.True(canRedoIdx >= 0 && runRedoIdx >= 0,
            "Redo_Click must reference CanRedo and RunRedo()");
        Assert.True(canRedoIdx < runRedoIdx,
            "CanRedo guard must precede RunRedo() call");
    }

    /// <summary>
    /// MapChip Export handler must access the VM's CONFIG buffer via the
    /// public <c>GetCachedConfigClone</c> accessor (which clones the
    /// internal cache to prevent caller mutation — Copilot CLI v2 review)
    /// and use the shared <c>FileDialogHelper.SaveFile</c> wrapper for
    /// picker parity with the rest of the Avalonia layer.
    /// </summary>
    [Fact]
    public void View_MapChipExport_Click_UsesVmAccessorAndFileDialogHelper()
    {
        string code = File.ReadAllText(CodeBehindPath());
        var bodyMatch = Regex.Match(code,
            @"async void MapChipExport_Click[\s\S]*?(?=\n\s{8}(static\s|public\s|void\s|async\s|/// |\}))",
            RegexOptions.Singleline);
        Assert.True(bodyMatch.Success, "MapChipExport_Click method not found");
        string body = bodyMatch.Value;
        Assert.Contains("_vm.GetCachedConfigClone()", body);
        Assert.Contains("FileDialogHelper.SaveFile", body);
        Assert.Contains("*.MAPCHIP_CONFIG", body);
        Assert.Contains("File.WriteAllBytes", body);
    }

    /// <summary>
    /// The VM must expose <c>GetCachedConfigClone</c> as a public method
    /// that returns a defensive clone (i.e. `.Clone()` appears in the
    /// expression body). This catches direct field exposure regressions.
    /// </summary>
    [Fact]
    public void ViewModel_GetCachedConfigClone_ReturnsDefensiveClone()
    {
        string vm = File.ReadAllText(ViewModelPath());
        Assert.Matches(new Regex(
            @"public\s+byte\[\]\??\s+GetCachedConfigClone\(\)[\s\S]*?Clone\(\)",
            RegexOptions.Singleline), vm);
    }

    // -----------------------------------------------------------------
    // #704 — MapChip Import button parity tests.
    // -----------------------------------------------------------------

    /// <summary>
    /// #704: the MapChip Import button must NOT carry IsEnabled="False"
    /// or any stale #374/#692 tooltip anymore — it became functional via
    /// the #704 raw .MAPCHIP_CONFIG read path.
    /// </summary>
    [Fact]
    public void View_MapChipImport_Button_IsEnabled()
    {
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""MapStyleEditor_MapChipImport_Button""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, "MapStyleEditor_MapChipImport_Button must exist");
        Assert.False(m.Value.Contains("IsEnabled=\"False\""),
            "MapStyleEditor_MapChipImport_Button must not carry IsEnabled=\"False\" anymore (#704)");
        Assert.False(m.Value.Contains("#374"),
            "MapStyleEditor_MapChipImport_Button must not advertise the legacy #374 tooltip");
        Assert.False(m.Value.Contains("#692"),
            "MapStyleEditor_MapChipImport_Button must not advertise the stale #692 tooltip (functional in #704)");
    }

    /// <summary>
    /// #704: the MapChip Import button must wire Click to the
    /// MapChipImport_Click handler in AXAML AND the handler method must
    /// exist in the code-behind, wrapped in an undo scope.
    /// </summary>
    [Fact]
    public void View_MapChipImport_Click_HandlerWired()
    {
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationProperties\.AutomationId=""MapStyleEditor_MapChipImport_Button""[^>]*",
            RegexOptions.None);
        Match m = pattern.Match(axaml);
        Assert.True(m.Success, "MapStyleEditor_MapChipImport_Button must exist");
        Assert.Contains("Click=\"MapChipImport_Click\"", m.Value);

        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void MapChipImport_Click\(object\?",
            RegexOptions.None), code);
        // Handler must wrap the VM call in an undo scope (Begin / Commit /
        // Rollback) so a failed write does not leave a half-applied import.
        var bodyMatch = Regex.Match(code,
            @"void MapChipImport_Click[\s\S]*?(?=\n\s{8}(static\s|public\s|void\s|/// |\}))",
            RegexOptions.Singleline);
        Assert.True(bodyMatch.Success, "MapChipImport_Click method not found");
        string body = bodyMatch.Value;
        Assert.Contains("_undoService.Begin", body);
        Assert.Contains("_vm.TryWriteConfigBuffer", body);
        Assert.Contains("_undoService.Commit()", body);
        Assert.Contains("_undoService.Rollback()", body);
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with a known palette region.
    /// Returns (rom, paletteBase) where paletteBase is a ROM offset.
    ///
    /// Layout planted at paletteBase:
    ///   - index 1 (non-fog): R = 1+i, G = 2+i, B = 3+i for i in 0..15
    ///   - index 7 (fog of 2): R = (10+i)&0x1F, G = (20+i)&0x1F, B = (30+i)&0x1F
    /// </summary>
    static (ROM rom, uint paletteBase) MakeSyntheticPaletteRom()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        uint paletteBase = 0x200000;

        // Plant palette index 1 (0x20 bytes starting at paletteBase + 0x20).
        uint slot1Addr = paletteBase + 1 * 0x20;
        for (int i = 0; i < 16; i++)
        {
            ushort r = (ushort)(1 + i);
            ushort g = (ushort)(2 + i);
            ushort b = (ushort)(3 + i);
            ushort packed = (ushort)((r & 0x1F) | ((g & 0x1F) << 5) | ((b & 0x1F) << 10));
            rom.Data[slot1Addr + i * 2 + 0] = (byte)(packed & 0xFF);
            rom.Data[slot1Addr + i * 2 + 1] = (byte)((packed >> 8) & 0xFF);
        }

        // Plant palette index 7 (0x20 bytes starting at paletteBase + 7 * 0x20).
        uint slot7Addr = paletteBase + 7 * 0x20;
        for (int i = 0; i < 16; i++)
        {
            ushort r = (ushort)((10 + i) & 0x1F);
            ushort g = (ushort)((20 + i) & 0x1F);
            ushort b = (ushort)((30 + i) & 0x1F);
            ushort packed = (ushort)((r & 0x1F) | ((g & 0x1F) << 5) | ((b & 0x1F) << 10));
            rom.Data[slot7Addr + i * 2 + 0] = (byte)(packed & 0xFF);
            rom.Data[slot7Addr + i * 2 + 1] = (byte)((packed >> 8) & 0xFF);
        }

        return (rom, paletteBase);
    }

    static string AxamlPath() => Path.Combine(AvaloniaDir, "Views", "MapStyleEditorView.axaml");
    static string CodeBehindPath() => Path.Combine(AvaloniaDir, "Views", "MapStyleEditorView.axaml.cs");
    static string ViewModelPath() => Path.Combine(AvaloniaDir, "ViewModels", "MapStyleEditorViewModel.cs");

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

