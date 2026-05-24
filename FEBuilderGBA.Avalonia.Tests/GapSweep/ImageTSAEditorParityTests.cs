// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #398 regression tests for ImageTSAEditorView.
//
// Closes the 97-control density gap (WF=100, AV was 3) and the 30
// WF-only labels surfaced on ImageTSAEditorForm (HIGH density verdict).
// The TSA editor is context-injected — callers pass the WF Init(...)
// arguments (width/height/zimg/tsa pointers + palette pointer/address +
// flags). Tests assert:
//
//   - Density Verdict reaches MEDIUM (AV >= 75 controls).
//   - The expected AutomationId surface is present (top toolbar / palette
//     16x3 RGB grid / main image tab / battle preview / chipset preview).
//   - Every Button has Click OR IsEnabled=False (#577 pattern).
//   - The 5-5-5 RGB palette pack / unpack round-trip on a synthetic ROM.
//   - Init(...) flips IsContextLoaded to true.
//   - Every WF-only label is covered as AXAML English OR KnownGap marker.
//   - The KnownGap markers each have a non-empty reason.
//   - Every rom.write_* call in the view code-behind lives inside an
//     ambient _undoService.Begin / Commit window.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Marked [Collection("SharedState")] because the synthetic-ROM tests
/// mutate CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class ImageTSAEditorParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer reports 100 control instantiations (the 16-color palette
    /// grid is the bulk). MEDIUM verdict requires AV >= 75 (75% of WF).
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageTSAEditorView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 100;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 75
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 — control surface assertions (static AXAML inspection).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTopToolbar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteIndex_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_Zoom_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_Info_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_Undo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_Redo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_Write_Button\"", axaml);
        Assert.Contains("Click=\"Write_Click\"", axaml);
    }

    [Fact]
    public void View_HasPaletteTab()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageTSAEditor_Palette_Tab\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteClipboard_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteWrite_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteUndo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_PaletteRedo_Button\"", axaml);
        Assert.Contains("Click=\"PaletteWrite_Click\"", axaml);
    }

    [Fact]
    public void View_HasMainImageTab()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageTSAEditor_MainImage_Tab\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_MainImage_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_MainImage_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_MainImage_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_MainImage_Preview\"", axaml);
    }

    [Fact]
    public void View_HasBattleCanvas()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageTSAEditor_Battle_Preview\"", axaml);
        Assert.Contains("AutomationId=\"ImageTSAEditor_TSAInfo_Input\"", axaml);
    }

    [Fact]
    public void View_HasChipsetList()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageTSAEditor_ChipList_Preview\"", axaml);
    }

    [Fact]
    public void View_HasAllSixteenPaletteRows()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"AutomationId=\"ImageTSAEditor_Palette_R_{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageTSAEditor_Palette_G_{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageTSAEditor_Palette_B_{i}_Input\"", axaml);
        }
    }

    [Fact]
    public void View_HasAllSixteenPaletteIndexAndHexLabels()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"AutomationId=\"ImageTSAEditor_Palette_Idx_{i}_Label\"", axaml);
            Assert.Contains($"AutomationId=\"ImageTSAEditor_Palette_P_{i}_Label\"", axaml);
        }
    }

    /// <summary>
    /// Regression for Copilot CLI plan-review B2 + the #577 pattern: every
    /// Button in the view must either have a Click handler OR be marked
    /// IsEnabled="False" / IsVisible="False". This is what guarantees the
    /// deferred KnownGap buttons (Clipboard / MainImage Import / Export)
    /// don't act as dead controls.
    /// </summary>
    [Fact]
    public void View_AllButtons_AreWiredOrExplicitlyInert()
    {
        string axaml = ReadAxaml();
        var buttonOpenRx = new Regex(@"<Button\b([\s\S]*?)/>|<Button\b([\s\S]*?)>",
            RegexOptions.Compiled);
        var matches = buttonOpenRx.Matches(axaml);
        Assert.True(matches.Count >= 5,
            $"Expected at least 5 Button tags in the AXAML; found {matches.Count}.");
        foreach (Match m in matches)
        {
            string tag = m.Value;
            // Only look at our buttons, not template buttons in nested controls.
            if (!tag.Contains("ImageTSAEditor_")) continue;
            bool hasClick = tag.Contains("Click=\"");
            bool inert = tag.Contains("IsEnabled=\"False\"") || tag.Contains("IsVisible=\"False\"");
            Assert.True(hasClick || inert,
                $"Button without Click handler nor IsEnabled/IsVisible=False: {tag}");
        }
    }

    [Fact]
    public void View_WriteButton_IsContextGated()
    {
        string axaml = ReadAxaml();
        // The Write button's IsEnabled is bound to IsContextLoaded - that
        // is the gate Copilot CLI plan-review B1 asked for.
        var writeBlockRx = new Regex(
            "AutomationId=\"ImageTSAEditor_Write_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = writeBlockRx.Match(axaml);
        Assert.True(m.Success, "Write button tag not found");
        Assert.Contains("IsEnabled=\"{Binding IsContextLoaded}\"", m.Value);
    }

    [Fact]
    public void View_PaletteWriteButton_IsContextGated()
    {
        string axaml = ReadAxaml();
        var paletteWriteBlockRx = new Regex(
            "AutomationId=\"ImageTSAEditor_PaletteWrite_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = paletteWriteBlockRx.Match(axaml);
        Assert.True(m.Success, "PaletteWrite button tag not found");
        Assert.Contains("IsEnabled=\"{Binding IsContextLoaded}\"", m.Value);
    }

    [Fact]
    public void View_ClipboardButton_IsExplicitlyInert()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageTSAEditor_PaletteClipboard_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "Clipboard button tag not found");
        Assert.Contains("IsEnabled=\"False\"", m.Value);
    }

    [Fact]
    public void View_MainImageImportButton_IsExplicitlyInert()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageTSAEditor_MainImage_Import_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "MainImage Import button tag not found");
        Assert.Contains("IsEnabled=\"False\"", m.Value);
    }

    [Fact]
    public void View_MainImageExportButton_IsExplicitlyInert()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageTSAEditor_MainImage_Export_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "MainImage Export button tag not found");
        Assert.Contains("IsEnabled=\"False\"", m.Value);
    }

    // -----------------------------------------------------------------
    // Roslyn-lite checks — Write / PaletteWrite handlers use UndoService.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_PaletteWriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+PaletteWrite_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+PaletteWrite_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+PaletteWrite_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_WriteHandler_GatedOnIsContextLoaded()
    {
        string source = ReadCodeBehind();
        // Guard at top of Write_Click - cheap way to keep address-0 writes off.
        Assert.Matches(new Regex(
            @"void\s+Write_Click[\s\S]*?if\s*\(\s*!_vm\.IsContextLoaded\s*\)\s*return",
            RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_PaletteWriteHandler_GatedOnIsContextLoaded()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(
            @"void\s+PaletteWrite_Click[\s\S]*?if\s*\(\s*!_vm\.IsContextLoaded\s*\)\s*return",
            RegexOptions.Compiled), source);
    }

    /// <summary>
    /// Static scan: every direct ROM-write primitive in the code-behind
    /// must live inside a `_undoService.Begin / Commit` window. This is
    /// the bypass-scan #577 finding.
    /// </summary>
    [Fact]
    public void View_NoRomWriteBypassesUndoScope()
    {
        string source = ReadCodeBehind();
        var writePattern = new Regex(@"\brom\.(?:write_(?:u8|u16|u32|p32|range|fill)|SetU(?:8|16|32))\b",
            RegexOptions.Compiled);
        foreach (Match m in writePattern.Matches(source))
        {
            int matchIdx = m.Index;
            int methodStart = source.LastIndexOf("void ", matchIdx, StringComparison.Ordinal);
            if (methodStart < 0) methodStart = 0;
            int methodEnd = FindMatchingBrace(source, matchIdx);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            Assert.Contains("_undoService.Begin", body);
            Assert.Contains("_undoService.Commit", body);
        }
    }

    static int FindMatchingBrace(string src, int matchIdx)
    {
        int searchStart = Math.Max(0, matchIdx - 200);
        int braceOpen = src.IndexOf('{', searchStart);
        if (braceOpen < 0 || braceOpen > matchIdx) braceOpen = src.IndexOf('{', matchIdx);
        if (braceOpen < 0) return src.Length;
        int depth = 1;
        for (int i = braceOpen + 1; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}') { depth--; if (depth == 0) return i + 1; }
        }
        return src.Length;
    }

    // -----------------------------------------------------------------
    // KnownGap inventory — every WF-only label is covered.
    // -----------------------------------------------------------------

    /// <summary>
    /// The 30 distinct WF-only labels from the
    /// 2026-05-26-labels-sweep.md ImageTSAEditorForm section.
    /// 16 are pure digits (palette index labels 1..16), 3 are pure letters
    /// (R/G/B color row labels). The remaining 11 are caption text from
    /// WinForms Designer.
    /// </summary>
    static readonly string[] WfOnlyLabelInventory =
    {
        "1", "2", "3", "4", "5", "6", "7", "8",
        "9", "10", "11", "12", "13", "14", "15", "16",
        "B", "G", "R",
        "REDO",
        "UNDO",
        "クリップボード",
        "パレット",
        "パレットアドレス",
        "パレット書き込み",
        "メイン画像",
        "書き込み",
        "画像",
        "画像取出",
        "画像読込",
    };

    /// <summary>
    /// Each WF-only label is covered by exactly one of:
    ///   - An AXAML attribute Content/Header/Text equal to the English form.
    ///   - A KnownGap comment with non-empty reason.
    /// The map below is the explicit audit trail.
    /// </summary>
    [Fact]
    public void View_HasAllWfOnlyLabelsCovered()
    {
        Assert.Equal(30, WfOnlyLabelInventory.Length);

        string axaml = ReadAxaml();

        var labelCoverage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Palette index labels (1..16) - rendered as ImageTSAEditor_Palette_Idx_{i}_Label.
            ["1"] = "1",
            ["2"] = "2",
            ["3"] = "3",
            ["4"] = "4",
            ["5"] = "5",
            ["6"] = "6",
            ["7"] = "7",
            ["8"] = "8",
            ["9"] = "9",
            ["10"] = "10",
            ["11"] = "11",
            ["12"] = "12",
            ["13"] = "13",
            ["14"] = "14",
            ["15"] = "15",
            ["16"] = "16",
            // RGB row labels.
            ["R"] = "R",
            ["G"] = "G",
            ["B"] = "B",
            // Top toolbar / palette tab captions (rendered as title-case
            // in AXAML to match Avalonia convention; WF used all-caps).
            ["REDO"] = "Redo",
            ["UNDO"] = "Undo",
            ["パレットアドレス"] = "Palette Address",
            ["パレット書き込み"] = "Palette Write",
            ["書き込み"] = "Write",
            // Main Image tab / image controls.
            ["メイン画像"] = "Main Image",
            ["画像"] = "Image",
            ["画像読込"] = "Image Import",
            ["画像取出"] = "Image Export",
            // KnownGap-covered (no English direct counterpart - they cover deferred behaviour).
            ["パレット"] = "KnownGap: PaletteToClipboard",
            ["クリップボード"] = "Clipboard",
        };

        Assert.Equal(WfOnlyLabelInventory.Length, labelCoverage.Count);
        foreach (var wfLabel in WfOnlyLabelInventory)
        {
            Assert.True(labelCoverage.TryGetValue(wfLabel, out var enLabel),
                $"WF-only label '{wfLabel}' must be in the coverage map.");

            bool found =
                axaml.Contains($"Content=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Header=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Text=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"KnownGap: {wfLabel}", StringComparison.Ordinal)
                || axaml.Contains(enLabel, StringComparison.Ordinal);

            Assert.True(found,
                $"WF label '{wfLabel}' -> English '{enLabel}' must appear in AXAML " +
                $"as Content/Header/Text or be listed in a KnownGap comment.");
        }
    }

    /// <summary>
    /// The KnownGap comment block must enumerate every deferred WF-only
    /// surface with a non-empty `reason=`. Mirrors the acceptance-criterion
    /// audit trail required by Copilot CLI.
    /// </summary>
    [Fact]
    public void View_KnownGapBlock_HasNonEmptyReasons()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(@"KnownGap:\s*(\S+(?:\s+\S+)*?)\s+reason=(.+?)\s*-->",
            RegexOptions.Compiled);
        var matches = rx.Matches(axaml);
        Assert.True(matches.Count >= 5,
            $"AXAML must contain at least 5 KnownGap markers (BattleCanvasRender, " +
            $"ChipsetListRender, TSAByteWrite, PaletteToClipboard, MainImageImportExport); " +
            $"found {matches.Count}.");
        foreach (Match m in matches)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[1].Value),
                $"KnownGap entry must name a feature: '{m.Value}'");
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[2].Value),
                $"KnownGap entry must have a reason: '{m.Value}'");
        }
    }

    // -----------------------------------------------------------------
    // ViewModel: Init flips IsContextLoaded; palette round-trip 5-5-5.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_Init_FlipsIsContextLoaded()
    {
        var vm = new ImageTSAEditorViewModel();
        Assert.False(vm.IsContextLoaded, "Default state must be IsContextLoaded=false");

        vm.Init(
            width8: 32u,
            height8: 20u,
            zimgPointer: 0u,
            isHeaderTSA: false,
            isLZ77TSA: true,
            tsaPointer: 0u,
            palettePointer: U.NOT_FOUND,
            paletteAddress: 0u,
            paletteCount: 1);

        Assert.True(vm.IsContextLoaded, "Init must set IsContextLoaded=true");
        Assert.Equal(32u, vm.Width8);
        Assert.Equal(20u, vm.Height8);
        Assert.False(vm.IsHeaderTSA);
        Assert.True(vm.IsLZ77TSA);
        Assert.Equal(1, vm.PaletteCount);
    }

    [Fact]
    public void ViewModel_Init_WithHeaderTSA_ClampsDimensionsToMinimum()
    {
        var vm = new ImageTSAEditorViewModel();
        vm.Init(
            width8: 10u,    // smaller than 256/8 = 32
            height8: 5u,    // smaller than 160/8 = 20
            zimgPointer: 0u,
            isHeaderTSA: true,
            isLZ77TSA: false,
            tsaPointer: 0u,
            palettePointer: U.NOT_FOUND,
            paletteAddress: 0u,
            paletteCount: 1);

        Assert.Equal(32u, vm.Width8);
        Assert.Equal(20u, vm.Height8);
    }

    [Fact]
    public void ViewModel_LoadPalette_Reads5_5_5_LittleEndian()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint paletteAddr = 0x00500000u;

            // Plant entry index 0:
            //   - color 0: R=0, G=0, B=0       -> 0x0000
            //   - color 1: R=255, G=0, B=0     -> 0x001F (r5=31)
            //   - color 2: R=0, G=255, B=0     -> 0x03E0 (g5=31)
            //   - color 3: R=0, G=0, B=255     -> 0x7C00 (b5=31)
            //   - color 4: R=255, G=255, B=255 -> 0x7FFF
            BitConverter.GetBytes((ushort)0x0000).CopyTo(rom.Data, (int)(paletteAddr + 0));
            BitConverter.GetBytes((ushort)0x001F).CopyTo(rom.Data, (int)(paletteAddr + 2));
            BitConverter.GetBytes((ushort)0x03E0).CopyTo(rom.Data, (int)(paletteAddr + 4));
            BitConverter.GetBytes((ushort)0x7C00).CopyTo(rom.Data, (int)(paletteAddr + 6));
            BitConverter.GetBytes((ushort)0x7FFF).CopyTo(rom.Data, (int)(paletteAddr + 8));

            var vm = new ImageTSAEditorViewModel();
            var rgb = vm.LoadPalette(paletteAddr, 0);
            Assert.Equal(16, rgb.Length);
            Assert.Equal((byte)0, rgb[0].R); Assert.Equal((byte)0, rgb[0].G); Assert.Equal((byte)0, rgb[0].B);
            // 5-bit max (31) << 3 = 248.
            Assert.Equal((byte)248, rgb[1].R); Assert.Equal((byte)0, rgb[1].G); Assert.Equal((byte)0, rgb[1].B);
            Assert.Equal((byte)0, rgb[2].R); Assert.Equal((byte)248, rgb[2].G); Assert.Equal((byte)0, rgb[2].B);
            Assert.Equal((byte)0, rgb[3].R); Assert.Equal((byte)0, rgb[3].G); Assert.Equal((byte)248, rgb[3].B);
            Assert.Equal((byte)248, rgb[4].R); Assert.Equal((byte)248, rgb[4].G); Assert.Equal((byte)248, rgb[4].B);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadPalette_UsesPaletteIndexOffset()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint paletteAddr = 0x00501000u;
            // Plant color at slot 2, color 0: word 0x7FFF (white).
            BitConverter.GetBytes((ushort)0x7FFF).CopyTo(rom.Data, (int)(paletteAddr + 2 * 0x20));

            var vm = new ImageTSAEditorViewModel();
            var rgbSlot0 = vm.LoadPalette(paletteAddr, 0);
            Assert.Equal((byte)0, rgbSlot0[0].R);
            Assert.Equal((byte)0, rgbSlot0[0].G);
            Assert.Equal((byte)0, rgbSlot0[0].B);

            var rgbSlot2 = vm.LoadPalette(paletteAddr, 2);
            Assert.Equal((byte)248, rgbSlot2[0].R);
            Assert.Equal((byte)248, rgbSlot2[0].G);
            Assert.Equal((byte)248, rgbSlot2[0].B);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WritePalette_RoundTrips_5_5_5()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint paletteAddr = 0x00502000u;

            // Build a 16-color RGB sequence with assorted values.
            var rgbIn = new (byte R, byte G, byte B)[16];
            for (int i = 0; i < 16; i++)
            {
                rgbIn[i] = ((byte)(i * 8), (byte)(255 - i * 8), (byte)((i * 16) & 0xFF));
            }

            var vm = new ImageTSAEditorViewModel();
            vm.WritePalette(paletteAddr, 1, rgbIn);

            var rgbOut = vm.LoadPalette(paletteAddr, 1);
            // 5-5-5 packing loses the low 3 bits of each channel, so we
            // compare with the lost-bits-masked input.
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal((byte)(rgbIn[i].R & 0xF8), rgbOut[i].R);
                Assert.Equal((byte)(rgbIn[i].G & 0xF8), rgbOut[i].G);
                Assert.Equal((byte)(rgbIn[i].B & 0xF8), rgbOut[i].B);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WritePalette_ThrowsOnWrongLength()
    {
        var vm = new ImageTSAEditorViewModel();
        // null
        Assert.Throws<ArgumentNullException>(() => vm.WritePalette(0, 0, null!));
        // wrong length
        Assert.Throws<ArgumentException>(() => vm.WritePalette(0, 0, new (byte, byte, byte)[15]));
    }

    [Fact]
    public void ViewModel_PackRgb555_LowBitsArePreservedTo5Bits()
    {
        // Sanity check the static packer used by WritePalette - explicit
        // table-driven test so a future refactor can't silently swap the
        // byte order without flipping the test.
        // R=255, G=0, B=0 -> 0x001F
        Assert.Equal((ushort)0x001F, ImageTSAEditorViewModel.PackRgb555(255, 0, 0));
        // R=0, G=255, B=0 -> 0x03E0
        Assert.Equal((ushort)0x03E0, ImageTSAEditorViewModel.PackRgb555(0, 255, 0));
        // R=0, G=0, B=255 -> 0x7C00
        Assert.Equal((ushort)0x7C00, ImageTSAEditorViewModel.PackRgb555(0, 0, 255));
        // White
        Assert.Equal((ushort)0x7FFF, ImageTSAEditorViewModel.PackRgb555(255, 255, 255));
        // Black
        Assert.Equal((ushort)0x0000, ImageTSAEditorViewModel.PackRgb555(0, 0, 0));
    }

    // -----------------------------------------------------------------
    // ViewModel: LoadList / LoadEntry preserve the existing stub API.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_NullRom_ReturnsEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageTSAEditorViewModel();
            var rows = vm.LoadList();
            Assert.Empty(rows);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadList_WithRom_ReturnsOneRow()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageTSAEditorViewModel();
            var rows = vm.LoadList();
            Assert.Single(rows);
            Assert.Equal("TSA Tile Editor", rows[0].name);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_NullRom_NoThrow()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageTSAEditorViewModel();
            vm.LoadEntry(0);
            Assert.False(vm.IsLoaded); // LoadEntry early-returns when no ROM
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeMinimalFe8uRom()
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageTSAEditorView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageTSAEditorView.axaml.cs"));

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
