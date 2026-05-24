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
        "MapStyleEditor_MapChipExport_Button",
        "MapStyleEditor_MapChipImport_Button",
        "MapStyleEditor_ObjExport_Button",
        "MapStyleEditor_ObjImport_Button",
        "MapStyleEditor_PaletteExport_Button",
        "MapStyleEditor_PaletteImport_Button",
        "MapStyleEditor_PaletteClipboard_Button",
        "MapStyleEditor_PaletteWrite_Button",
        "MapStyleEditor_CopyTile_Button",
        "MapStyleEditor_CopyType_Button",
        "MapStyleEditor_Paste_Button",
        "MapStyleEditor_ConfigWrite_Button",
        "MapStyleEditor_Undo_Button",
        "MapStyleEditor_Redo_Button",
    };

    [Fact]
    public void View_KnownGapButtons_AreDisabledAndTagged()
    {
        string axaml = ReadAxaml();
        foreach (string id in KnownGapButtonIds)
        {
            // Each button must appear with IsEnabled="False" AND a
            // tooltip referencing #374 in the same element block.
            // Allow attributes in any order via DOTALL flag.
            var idAttr = $"AutomationProperties.AutomationId=\"{id}\"";
            Assert.Contains(idAttr, axaml);

            // Locate the <Button ... /> element containing this id and
            // verify the element body has IsEnabled="False" and a #374
            // tooltip.
            var elementPattern = new Regex(
                @"<Button(?:\s[^/>]*?)?" + Regex.Escape(idAttr) + @"[\s\S]*?/?>",
                RegexOptions.None);
            Match m = elementPattern.Match(axaml);
            Assert.True(m.Success, $"Cannot locate <Button> element for {id}");

            string elementText = m.Value;
            Assert.True(
                elementText.Contains("IsEnabled=\"False\""),
                $"KnownGap button {id} must have IsEnabled=\"False\"; element was: {elementText}");
            Assert.True(
                elementText.Contains("#374"),
                $"KnownGap button {id} must have a #374 tooltip; element was: {elementText}");
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
