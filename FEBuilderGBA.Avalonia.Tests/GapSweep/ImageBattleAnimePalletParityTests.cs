// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for ImageBattleAnimePalletView. (#399)
//
// Closes the 125 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `ImageBattleAnimePalletForm` (HIGH density 99/3 == -97 %, 29 WF-only
// labels). After this PR `ImageBattleAnimePalletView` rebuilds to the full
// 16-color palette editor the WinForms form exposes, with the LZ77 compressed
// read/write path extracted to FEBuilderGBA.Core/ImageBattleAnimePaletteCore.
//
// Copilot CLI plan-review v1..v7 issues addressed (see issue #399 comment thread).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImageBattleAnimePalletForm parity raise (#399) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap.
/// </summary>
[Collection("SharedState")]
public class ImageBattleAnimePalletParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 99 control instantiations (per 2026-05-21
    /// density sweep). To leave the HIGH verdict we need
    /// AV >= ceil(99 * 0.75) = 75.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 99;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 75
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75 % of WF={WfControlCount}) to leave the HIGH verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasPaletteTypeBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_PaletteIndex_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_PaletteWrite_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Warning32Color_Label\"", axaml);
    }

    [Fact]
    public void View_HasAddressBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_SourceSlot_Label\"", axaml);
    }

    [Fact]
    public void View_HasPalette16RowGrid()
    {
        // Each of the 16 columns (1..16) has R/G/B numerics + swatch.
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"AutomationId=\"ImageBattleAnimePallet_R{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleAnimePallet_G{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleAnimePallet_B{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleAnimePallet_Swatch{i}_Label\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleAnimePallet_Index{i}_Label\"", axaml);
        }
    }

    [Fact]
    public void View_HasActionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Clipboard_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Undo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Redo_Button\"", axaml);
    }

    [Fact]
    public void View_HasZoomBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_Zoom_Combo\"", axaml);
    }

    [Fact]
    public void View_HasRgbHeaders()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_RHeader_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_GHeader_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_BHeader_Label\"", axaml);
    }

    [Fact]
    public void View_SamplePreview_IsRenderedGbaImageControl()
    {
        // #822: the deferred placeholder label is replaced by a real
        // GbaImageControl that hosts the cross-platform DrawSample render.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_SamplePreview_Image\"", axaml);
        Assert.Contains("<controls:GbaImageControl", axaml);
        // The old deferred placeholder label must be gone.
        Assert.DoesNotContain("ImageBattleAnimePallet_SamplePreview_Label", axaml);
    }

    [Fact]
    public void View_CodeBehind_WiresSamplePreviewOnLoadAndPaletteChange()
    {
        // The code-behind must push the rendered sample into the control on
        // entry-load (OnSelectedEntry) and on palette-type change
        // (ReloadFromAuthoritativeSlot), via RenderSampleBattleAnime().
        string code = File.ReadAllText(CodeBehindPath());
        // The rendered grid must be passed to SetImage AND disposed afterwards
        // (#824 Copilot review: IImage is IDisposable). The code-behind wraps it
        // in a `using` so the freshly-rendered grid is freed after SetImage
        // copies its pixels.
        Assert.Matches(new Regex(
            @"using\s+IImage\s+grid\s*=\s*_vm\.RenderSampleBattleAnime\(\)\s*;[\s\S]{0,200}?SamplePreview\.SetImage\(grid\)",
            RegexOptions.Singleline), code);
        // RefreshSamplePreview is invoked from OnSelectedEntry (load).
        Assert.Matches(new Regex(
            @"OnSelectedEntry[\s\S]*?RefreshSamplePreview\(\)",
            RegexOptions.Singleline), code);
        // ...and from the palette-type reload path.
        Assert.Matches(new Regex(
            @"ReloadFromAuthoritativeSlot[\s\S]*?RefreshSamplePreview\(\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// #994: the Redo button is now wired to CoreState.Undo.RunRedo() — the
    /// stale "#399 WinForms-coupled PaletteFormRef" blocker was removed.
    /// The button must carry Click="Redo_Click" and must NOT contain IsEnabled="False".
    /// </summary>
    [Fact]
    public void View_RedoButton_IsWiredAndEnabled()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageBattleAnimePallet_Redo_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "Redo button tag not found");
        Assert.Contains("Click=\"Redo_Click\"", m.Value);
        Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
    }

    /// <summary>
    /// #994: the Redo_Click handler must call CoreState.Undo.RunRedo() and
    /// guard on CanRedo. CanRedo must appear textually before RunRedo()
    /// within the Redo_Click method body.
    /// The stale "#399" / "WinForms-coupled" strings must be gone.
    /// </summary>
    [Fact]
    public void View_RedoHandler_CallsRunRedo()
    {
        string source = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void\s+Redo_Click[\s\S]*?CoreState\.Undo\.CanRedo",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(
            @"void\s+Redo_Click[\s\S]*?CoreState\.Undo\.RunRedo\(\)",
            RegexOptions.Compiled), source);
        // Extract only the Redo_Click method body to check ordering.
        int methodStart = source.IndexOf("void Redo_Click(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Redo_Click method not found");
        int idxCanRedo = source.IndexOf("CoreState.Undo.CanRedo", methodStart, StringComparison.Ordinal);
        int idxRunRedo = source.IndexOf("CoreState.Undo.RunRedo()", methodStart, StringComparison.Ordinal);
        Assert.True(idxCanRedo >= 0 && idxRunRedo >= 0, "CanRedo and RunRedo must both be present");
        Assert.True(idxCanRedo < idxRunRedo, "CanRedo must appear before RunRedo()");
        // Stale strings must be gone.
        Assert.DoesNotContain("WinForms-coupled", source);
    }

    /// <summary>
    /// #869: the Import button is now wired (not a stub). It opens a file
    /// dialog, quantizes the image to 16 GBA RGB555 colors, and writes via
    /// the existing Write path. The button must be enabled and carry a
    /// Click handler; crucially it must NOT have IsEnabled="False" on the
    /// same line/block (within 100 chars — not the neighboring Export button).
    /// </summary>
    [Fact]
    public void View_ImportButton_IsWiredAndEnabled()
    {
        string axaml = ReadAxaml();
        // Narrow window (100 chars) ensures we don't match the adjacent
        // ExportButton's IsEnabled="False" line which is ~150 chars away.
        Assert.DoesNotMatch(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Import_Button""[\s\S]{0,100}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
        // The button must carry Click="Import_Click".
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Import_Button""[\s\S]{0,200}Click=""Import_Click""",
            RegexOptions.Singleline), axaml);
    }

    /// <summary>
    /// #869: the code-behind must have an Import_Click handler (not a stub).
    /// Verify both the handler and the DoImportFromFile injectable seam exist.
    /// </summary>
    [Fact]
    public void View_ImportButton_HandlerExists()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // Handler must exist and not be empty.
        Assert.Contains("async void Import_Click", code);
        // The injectable seam must call DoImport on the VM.
        Assert.Contains("_vm.DoImport", code);
        // The handler must call Write() under an undo scope.
        Assert.Matches(new Regex(
            @"_undoService\.Begin[\s\S]{0,2000}_vm\.Write\(\)[\s\S]{0,200}_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// #828: the Export Image button now reuses <c>GbaImageControl.ExportPng</c>
    /// (the merged read-only PNG primitive, #815) to export the rendered sample
    /// grid. It is wired to a Click handler, starts disabled (gated until a
    /// sample render succeeds), and the code-behind drives its IsEnabled from
    /// <c>SamplePreview.HasImage</c> — the exact pattern
    /// <c>ImageBattleScreenView</c> (#810) uses. The stale "Export requires…
    /// WinForms-coupled DrawBattleAnime via ImageFormRef.ExportImage" marker
    /// must be gone.
    /// </summary>
    [Fact]
    public void View_ExportButton_WiredAndGated()
    {
        string axaml = ReadAxaml();
        // Click handler is wired.
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Export_Button""[\s\S]{0,400}Click=""Export_Click""",
            RegexOptions.Singleline), axaml);
        // Starts disabled until a render succeeds (gated like ImageBattleScreen).
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Export_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
        // The stale WinForms-coupled deferral marker must be gone.
        Assert.DoesNotContain("ImageFormRef.ExportImage", axaml);

        string code = File.ReadAllText(CodeBehindPath());
        // Handler exports the rendered sample preview (no ROM write).
        Assert.Matches(new Regex(
            @"SamplePreview\.ExportPng\(\s*TopLevel\.GetTopLevel\(this\)\s+as\s+Window", RegexOptions.Singleline), code);
        // Gate is driven from HasImage and applied to the button.
        Assert.Matches(new Regex(
            @"ExportButton\.IsEnabled\s*=\s*SamplePreview\.HasImage",
            RegexOptions.Singleline), code);
    }

    // -----------------------------------------------------------------
    // Phase 5 - Code-behind / write-handler assertions.
    // -----------------------------------------------------------------

    /// <summary>
    /// Per Plan v3 Finding #3 + v6 Finding #2: the PaletteWrite_Click
    /// handler MUST wrap the VM write call in _undoService.Begin /
    /// Commit / Rollback so the LZ77 relocate + pointer rewrites are
    /// undoable atomically.
    /// </summary>
    [Fact]
    public void View_PaletteWriteHandler_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"_undoService\.Begin\([^)]*\)[\s\S]*?_vm\.Write\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_PaletteWriteHandler_RollsBackOnNotFound()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The handler must check newOffset == U.NOT_FOUND and call Rollback.
        Assert.Matches(new Regex(
            @"U\.NOT_FOUND[\s\S]*?_undoService\.Rollback\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_AddrResult_CarriesSourcePointerSlotInTag()
    {
        // Per Plan v6 Finding #3: AddrResult.addr = paletteOffset,
        // AddrResult.tag = sourcePointerSlot. The VM LoadList must populate
        // both, and the view OnSelectedEntry must read .tag.
        string vmCode = File.ReadAllText(ViewModelPath());
        // VM uses the 3-arg AddrResult ctor: (addr, name, tag).
        Assert.Matches(new Regex(
            @"new AddrResult\([\s\S]{0,100}sourceSlot",
            RegexOptions.Singleline), vmCode);
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic ROM with real LZ77 palette).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_PaletteTypeIndex_DefaultsToZero()
    {
        var vm = new ImageBattleAnimePalletViewModel();
        Assert.Equal(0, vm.PaletteTypeIndex);
    }

    [Fact]
    public void ViewModel_ZoomIndex_DefaultsToZero()
    {
        var vm = new ImageBattleAnimePalletViewModel();
        Assert.Equal(0, vm.ZoomIndex);
    }

    [Fact]
    public void ViewModel_Warning32ColorMode_DefaultsHidden()
    {
        var vm = new ImageBattleAnimePalletViewModel();
        Assert.False(vm.WarningVisible);
        Assert.False(vm.Is32ColorMode);
    }

    [Fact]
    public void ViewModel_Is32ColorMode_PaletteOnlyRecord_NoBanner()
    {
        // #1033: the 32-color-mode banner is now driven by an OAM palette-bank
        // scan (BattleAnimeRendererCore.CountAnimationPaletteBanks). A record
        // that has a multi-slot PALETTE but NO section/frame/OAM pointers is
        // unresolvable for the scan => it safely defaults to single-bank
        // (palette_count == 1) => no banner. (Having 4 palette SLOTS does not
        // by itself mean the SPRITES reference more than one bank.)
        var (rom, paletteOffset, sourceSlot) = MakeRomWithMultiSlotPalette();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.False(vm.WarningVisible,
                "an unresolvable (palette-only) record must default to no banner (#1033)");
            Assert.False(vm.Is32ColorMode);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #1033 — 32-color-mode warning banner driven by the OAM palette-bank
    // scan (BattleAnimeRendererCore.CountAnimationPaletteBanks).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_NoBankedSprites_NoBanner()
    {
        // A full record whose only sprite is bank 0 => single bank => no banner.
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithFullAnimeRecord(spritePaletteBank: 0);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.False(vm.WarningVisible);
            Assert.False(vm.Is32ColorMode);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_BankedSprite_ShowsBanner()
    {
        // A full record whose sprite uses 16-color palette bank 1 => 2 banks =>
        // 32-color mode => banner visible (#1033, mirrors WF
        // Is32ColorMode = (palette_count >= 2)).
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithFullAnimeRecord(spritePaletteBank: 1);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.True(vm.WarningVisible);
            Assert.True(vm.Is32ColorMode);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_SourceSlotBelowRecordOffset_DoesNotThrow_NoBanner()
    {
        // Guard: a source pointer slot < 0x1C cannot derive a valid record
        // offset; LoadEntry must not throw and must leave the banner hidden.
        // We construct a synthetic entry with a tiny source slot directly.
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x2000000], "BE8E01");
            // Plant a small valid LZ77 palette near the start so LoadEntry's
            // palette read succeeds; the bank scan is what we guard here.
            const uint paletteOffset = 0x150000;
            PlantLZ77(rom, paletteOffset, new byte[32]);
            CoreState.ROM = rom;

            var vm = new ImageBattleAnimePalletViewModel();
            // sourcePointerSlot = 4 (< PalettePointerOffsetInRecord 0x1C).
            vm.LoadEntry(paletteOffset, sourcePointerSlot: 4, paletteIndex: 0);
            Assert.False(vm.WarningVisible);
            Assert.False(vm.Is32ColorMode);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_RaisesPropertyChanged_WhenBannerFlipsOn()
    {
        // Subscribing to PropertyChanged must see WarningVisible AND
        // Is32ColorMode raised after a LoadEntry that turns the banner on.
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithFullAnimeRecord(spritePaletteBank: 1);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();

            var raised = new HashSet<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            Assert.True(vm.WarningVisible, "banner should be on for a banked sprite");
            Assert.Contains(nameof(ImageBattleAnimePalletViewModel.WarningVisible), raised);
            Assert.Contains(nameof(ImageBattleAnimePalletViewModel.Is32ColorMode), raised);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_ReadsPaletteRgbFromRom()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(
            // Custom colors: index 0 = red, index 1 = green, index 2 = blue.
            new ushort[] {
                0x001F, // R=31, G=0,  B=0
                0x03E0, // R=0,  G=31, B=0
                0x7C00, // R=0,  G=0,  B=31
                0,0,0,0,0,0,0,0,0,0,0,0,0,
            });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            // 5-bit channel values shifted up to 8-bit ((value & 0x1F) << 3).
            Assert.Equal(31 << 3, vm.GetR(0));
            Assert.Equal(0, vm.GetG(0));
            Assert.Equal(0, vm.GetB(0));

            Assert.Equal(0, vm.GetR(1));
            Assert.Equal(31 << 3, vm.GetG(1));
            Assert.Equal(0, vm.GetB(1));

            Assert.Equal(0, vm.GetR(2));
            Assert.Equal(0, vm.GetG(2));
            Assert.Equal(31 << 3, vm.GetB(2));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void View_PaletteIndexCombo_Has4Entries()
    {
        // Verified via code-behind: PaletteIndexCombo.Items.Add(...) is
        // called exactly 4 times for Player/Enemy/Other/4th Army.
        string code = File.ReadAllText(CodeBehindPath());
        var matches = Regex.Matches(code, @"PaletteIndexCombo\.Items\.Add\(");
        Assert.Equal(4, matches.Count);
    }

    [Fact]
    public void View_ZoomCombo_Has5Entries()
    {
        // Verified via code-behind: ZoomCombo.Items.Add(...) is called
        // exactly 5 times for Window Size / Image Size / 2x / 3x / 4x.
        string code = File.ReadAllText(CodeBehindPath());
        var matches = Regex.Matches(code, @"ZoomCombo\.Items\.Add\(");
        Assert.Equal(5, matches.Count);
    }

    [Fact]
    public void View_AllNumericsHaveIncrement8()
    {
        // All 48 R/G/B NumericUpDowns must have Increment="8" (matches WF
        // 5-bit step pattern).
        string axaml = ReadAxaml();
        var increment8Count = Regex.Matches(axaml,
            @"Name=""[RGB]\d+""[^/]*Increment=""8""", RegexOptions.Singleline).Count;
        // We don't need to assert exactly 48 (XAML formatting varies); just
        // assert the count is plausible (every Inc=8 on a NUD named R/G/B).
        Assert.True(increment8Count >= 48,
            $"Expected at least 48 R/G/B NumericUpDowns with Increment=8, got {increment8Count}");
    }

    [Fact]
    public void View_AllNumericsHaveMaximum255()
    {
        string axaml = ReadAxaml();
        var max255Count = Regex.Matches(axaml,
            @"Name=""[RGB]\d+""[^/]*Maximum=""255""", RegexOptions.Singleline).Count;
        Assert.True(max255Count >= 48,
            $"Expected at least 48 R/G/B NumericUpDowns with Maximum=255, got {max255Count}");
    }

    /// <summary>
    /// Per Plan v6 Finding #3 + v5 Finding #3: writing through the VM must
    /// reach the Core helper, which rewrites the source pointer slot AND
    /// the LDR-resolvable pointers atomically under the ambient undo
    /// scope. The synthetic ROM here forces a relocate (the new
    /// recompressed bytes are intentionally larger than the original).
    /// </summary>
    [Fact]
    public void ViewModel_Write_RoundTripsThroughCoreHelperUnderUndo()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(
            new ushort[16]); // all-zero block (compresses very small)
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            // Edit a single channel to force a real diff. We don't care
            // whether this triggers in-place or relocate -- both must
            // round-trip correctly.
            vm.SetR(0, 248); // 248 >> 3 == 31 (max R)

            var undoService = new UndoService();
            undoService.Begin("test write");
            uint newOffset = vm.Write();
            undoService.Commit();

            Assert.NotEqual(U.NOT_FOUND, newOffset);

            // Round-trip: re-read the palette from newOffset, R[0] must be 31.
            ushort[] roundtrip = ImageBattleAnimePaletteCore.ReadPalette(
                rom, newOffset, paletteIndex: 0);
            Assert.NotNull(roundtrip);
            Assert.Equal(31, roundtrip[0] & 0x1F);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Per Plan v6 Finding #2 (rollback semantics): a writer that performs
    /// a tracked write then returns U.NOT_FOUND triggers
    /// UndoService.Rollback() which calls Push + RunUndo. ROM bytes are
    /// restored; the buffer DID grow (push) and Postion returns to initial.
    /// </summary>
    [Fact]
    public void ViewModel_Write_TrackedWriteFails_RollbackRunsUndoAndRestoresRom()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // Snapshot of the relocate target region (high in ROM).
            uint testWriteOffset = (uint)(rom.Data.Length - 16);
            byte[] before = new byte[16];
            for (int i = 0; i < 16; i++) before[i] = rom.Data[testWriteOffset + i];

            int initialCount = CoreState.Undo.UndoBuffer.Count;
            int initialPostion = CoreState.Undo.Postion;

            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            // Force a real diff so the write path runs.
            vm.SetR(0, 248);

            // Inject a writer that performs a tracked write then fails.
            vm.SetWriterOverrideForTests((r, data) =>
            {
                r.write_range(testWriteOffset, new byte[] { 1, 2, 3, 4 });
                return U.NOT_FOUND;
            });

            var undoService = new UndoService();
            undoService.Begin("test failing write");
            uint result = vm.Write();
            Assert.Equal(U.NOT_FOUND, result);
            undoService.Rollback();

            // ROM bytes at testWriteOffset must be restored.
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(before[i], rom.Data[testWriteOffset + i]);
            }
            // Postion returned to initial; buffer grew by 1 (Push happened).
            Assert.Equal(initialPostion, CoreState.Undo.Postion);
            Assert.Equal(initialCount + 1, CoreState.Undo.UndoBuffer.Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Complement to the tracked-write test: a writer that returns
    /// U.NOT_FOUND WITHOUT writing leaves the undo data empty, so
    /// UndoService.Rollback() does NOT push and does NOT call RunUndo.
    /// </summary>
    [Fact]
    public void ViewModel_Write_NoTrackedWriteFails_RollbackIsNoOp()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            int initialCount = CoreState.Undo.UndoBuffer.Count;
            int initialPostion = CoreState.Undo.Postion;

            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            // The new colors must differ from the original, otherwise
            // the in-place capacity check may short-circuit and the
            // injected writer never runs.
            vm.SetR(0, 248);
            vm.SetG(0, 248);
            vm.SetB(0, 248);
            vm.SetR(1, 128);

            // Inject a writer that immediately fails (no writes).
            vm.SetWriterOverrideForTests((r, data) => U.NOT_FOUND);

            var undoService = new UndoService();
            undoService.Begin("test no-write failing write");
            uint result = vm.Write();
            undoService.Rollback();

            // U.NOT_FOUND is the only acceptable outcome when the writer
            // refuses (we don't care whether the in-place branch was
            // taken; either way no spurious Push must happen).
            Assert.True(result == U.NOT_FOUND || result == paletteOffset,
                $"Expected NOT_FOUND or in-place ({paletteOffset:X8}), got {result:X8}");

            // For both branches, no spurious Push must happen. Postion
            // and UndoBuffer.Count must remain at initial values when the
            // writer was reached (i.e. result == NOT_FOUND). When in-place
            // short-circuited, the write_range / write_fill calls under
            // the scope would have added a Push -- but then the buffer
            // grew by exactly 1, matching the tracked-write test case.
            if (result == U.NOT_FOUND)
            {
                Assert.Equal(initialPostion, CoreState.Undo.Postion);
                Assert.Equal(initialCount, CoreState.Undo.UndoBuffer.Count);
            }
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ViewModel_LoadList_EnumeratesBattleAnimeRecords()
    {
        var (rom, _, _) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            List<AddrResult> list = vm.LoadList();
            Assert.NotEmpty(list);
            // AddrResult.addr = palette offset, AddrResult.tag = source pointer slot offset.
            Assert.True(list[0].tag != 0,
                "AddrResult.tag must carry the source pointer slot (Plan v6 Finding #3)");
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #822 — RenderSampleBattleAnime() threading + null-safety.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_RenderSample_NoEntryLoaded_ReturnsNull()
    {
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, _, _) = MakeRomWithSingleSlotPalette(new ushort[16]);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            // No LoadEntry => _sourcePointerSlot is 0 => null, no crash.
            Assert.Null(vm.RenderSampleBattleAnime());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RenderSample_NullRom_ReturnsNull()
    {
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageBattleAnimePalletViewModel();
            Assert.Null(vm.RenderSampleBattleAnime());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RenderSample_PaletteOnlyRecord_ReturnsNull()
    {
        // The single-slot helper plants ONLY a palette pointer (no section /
        // frame / OAM), so the record is unresolvable => null (no crash). This
        // also proves the VM derives the record offset from the source slot and
        // hands it to the Core path without throwing.
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.Null(vm.RenderSampleBattleAnime());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RenderSample_FullRecord_ThreadsRecordOffset_Returns360x290()
    {
        // A complete synthetic anime record. The VM must derive the record
        // offset from the loaded entry's source pointer slot
        // (sourceSlot - 0x1C) and produce the 360x290 grid — proving the
        // anime-ID/record threading is correct (no WF id-1).
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithFullAnimeRecord();
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            IImage grid = vm.RenderSampleBattleAnime();
            Assert.NotNull(grid);
            Assert.Equal(360, grid.Width);
            Assert.Equal(290, grid.Height);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // #828 — Export PNG of the rendered sample. The export path is
    // GbaImageControl.SetImage -> IconBitmapBuilder.FromImage -> WriteableBitmap
    // (the pixel copy) -> ExportPng -> Save (PNG encode). This test exercises
    // both halves: (a) IconBitmapBuilder.FromImage builds a 360x290 bitmap from
    // the rendered IImage, and (b) the rendered sample IImage encodes to a valid
    // 360x290 PNG (decoding the IHDR header). The cross-platform IImage->PNG
    // encode (SkiaImage.EncodePng -> SKEncodedImageFormat.Png) is asserted
    // directly rather than via WriteableBitmap.Save, which depends on Avalonia's
    // platform render backend (the headless test app runs without Skia, so its
    // WriteableBitmap.Save does not emit a real PNG — an environment quirk, not
    // an export-path defect).
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void Export_RenderedSample_EncodesValid360x290Png()
    {
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            var (rom, paletteOffset, sourceSlot) = MakeRomWithFullAnimeRecord();
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            using IImage grid = vm.RenderSampleBattleAnime();
            Assert.NotNull(grid);
            Assert.Equal(360, grid.Width);
            Assert.Equal(290, grid.Height);

            // (a) The SetImage pixel-copy step builds a 360x290 WriteableBitmap.
            using WriteableBitmap bmp = IconBitmapBuilder.FromImage(grid);
            Assert.NotNull(bmp);
            Assert.Equal(360, bmp.PixelSize.Width);
            Assert.Equal(290, bmp.PixelSize.Height);

            // (b) The rendered sample encodes to a real 360x290 PNG via the
            // cross-platform IImage->PNG primitive (SkiaImage.EncodePng).
            byte[] png = grid.EncodePng();
            Assert.True(IsPngSignature(png), "encoded bytes are not a PNG");
            (int w, int h) = ReadPngIhdrDimensions(png);
            Assert.Equal(360, w);
            Assert.Equal(290, h);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [AvaloniaFact]
    public void Export_NullSample_ProducesNoBitmap_NoCrash()
    {
        // Null-safety: a no-resolvable-anime record returns null from
        // RenderSampleBattleAnime; IconBitmapBuilder.FromImage(null) returns
        // null (no bitmap, no file written, button stays disabled). Mirrors
        // GbaImageControl.ExportPng early-returning on a null bitmap.
        EnsureImageService();
        var prevRom = CoreState.ROM;
        try
        {
            // Palette-only record => unresolvable => null sample.
            var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            IImage grid = vm.RenderSampleBattleAnime();
            Assert.Null(grid);

            // The builder is null-safe on a null image (no throw, no bitmap).
            WriteableBitmap bmp = IconBitmapBuilder.FromImage(grid);
            Assert.Null(bmp);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>True if the byte buffer starts with the 8-byte PNG signature.</summary>
    static bool IsPngSignature(byte[] data)
    {
        if (data == null || data.Length < 8) return false;
        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++) if (data[i] != sig[i]) return false;
        return true;
    }

    /// <summary>
    /// Read the width/height from a PNG's IHDR chunk. The IHDR is always the
    /// first chunk: bytes [0..7] signature, [8..11] length, [12..15] "IHDR",
    /// [16..19] width (big-endian), [20..23] height (big-endian).
    /// </summary>
    static (int width, int height) ReadPngIhdrDimensions(byte[] png)
    {
        Assert.True(png.Length >= 24, "PNG too small to contain IHDR");
        // Confirm the IHDR chunk-type marker at offset 12.
        Assert.True(png[12] == (byte)'I' && png[13] == (byte)'H'
                 && png[14] == (byte)'D' && png[15] == (byte)'R',
                 "first chunk is not IHDR");
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    static void EnsureImageService()
    {
        // Use the real SkiaImageService (idempotent) so 4bpp/RGBA decode works
        // — matches the pattern in ClassEditorListPreviewTests.
        if (CoreState.ImageService == null)
            CoreState.ImageService = new SkiaImageService();
    }

    // -----------------------------------------------------------------
    // #869 — DoImport injectable seam tests.
    // -----------------------------------------------------------------

    /// <summary>
    /// ViewModel.DoImport populates all 16 R/G/B cells from a 32-byte
    /// GBA palette (RGB555 u16 LE). This exercises the injectable seam
    /// without a file dialog or ROM write.
    /// </summary>
    [Fact]
    public void ViewModel_DoImport_PopulatesAllRgbFromGbaPalette()
    {
        var vm = new ImageBattleAnimePalletViewModel();

        // Build a known 32-byte palette: color[0] = max R (0x001F),
        // color[1] = max G (0x03E0), color[2] = max B (0x7C00),
        // rest = 0.
        byte[] palette = new byte[32];
        // color 0: R=31, G=0, B=0 → 0x001F → bytes [0]=0x1F,[1]=0x00
        palette[0] = 0x1F; palette[1] = 0x00;
        // color 1: R=0, G=31, B=0 → 0x03E0 → bytes [2]=0xE0,[3]=0x03
        palette[2] = 0xE0; palette[3] = 0x03;
        // color 2: R=0, G=0, B=31 → 0x7C00 → bytes [4]=0x00,[5]=0x7C
        palette[4] = 0x00; palette[5] = 0x7C;

        bool ok = vm.DoImport(palette);
        Assert.True(ok, "DoImport must return true for a valid 32-byte palette");

        // (31 & 0x1F) << 3 = 31 << 3 = 248
        Assert.Equal(248, vm.GetR(0));
        Assert.Equal(0, vm.GetG(0));
        Assert.Equal(0, vm.GetB(0));

        Assert.Equal(0, vm.GetR(1));
        Assert.Equal(248, vm.GetG(1));
        Assert.Equal(0, vm.GetB(1));

        Assert.Equal(0, vm.GetR(2));
        Assert.Equal(0, vm.GetG(2));
        Assert.Equal(248, vm.GetB(2));

        // Colors 3..15 must be zero.
        for (int i = 3; i < 16; i++)
        {
            Assert.Equal(0, vm.GetR(i));
            Assert.Equal(0, vm.GetG(i));
            Assert.Equal(0, vm.GetB(i));
        }
    }

    [Fact]
    public void ViewModel_DoImport_ReturnsFalse_WhenNull()
    {
        var vm = new ImageBattleAnimePalletViewModel();
        Assert.False(vm.DoImport(null));
    }

    [Fact]
    public void ViewModel_DoImport_ReturnsFalse_WhenTooShort()
    {
        var vm = new ImageBattleAnimePalletViewModel();
        Assert.False(vm.DoImport(new byte[31])); // 31 < 32
    }

    [Fact]
    public void ViewModel_DoImport_AcceptsLongerArray()
    {
        // DoImport must accept arrays > 32 bytes (quantizer may pad).
        var vm = new ImageBattleAnimePalletViewModel();
        byte[] palette = new byte[64]; // longer than 32 — should succeed
        palette[0] = 0x1F; // R=31 in color[0]
        bool ok = vm.DoImport(palette);
        Assert.True(ok);
        Assert.Equal(248, vm.GetR(0));
    }

    /// <summary>
    /// Round-trip: DoImport a known palette, then Write to ROM, then
    /// ReadPalette back — must match the original colors.
    /// Covers the full injectable-seam → Core write path under undo.
    /// </summary>
    [Fact]
    public void ViewModel_DoImport_RoundTrip_WritesExpectedBytesToRom()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            // Build import palette: color[0] = max R, color[1] = max G.
            byte[] importPalette = new byte[32];
            importPalette[0] = 0x1F; importPalette[1] = 0x00; // R=31
            importPalette[2] = 0xE0; importPalette[3] = 0x03; // G=31

            bool applied = vm.DoImport(importPalette);
            Assert.True(applied);

            var undoService = new UndoService();
            undoService.Begin("test import round-trip");
            uint newOffset = vm.Write();
            undoService.Commit();

            Assert.NotEqual(U.NOT_FOUND, newOffset);

            // Re-read: color[0] should have R=31, G=0, B=0.
            ushort[] roundtrip = ImageBattleAnimePaletteCore.ReadPalette(rom, newOffset, 0);
            Assert.NotNull(roundtrip);
            Assert.Equal(31, roundtrip[0] & 0x1F);        // R=31
            Assert.Equal(0,  (roundtrip[0] >> 5) & 0x1F); // G=0
            Assert.Equal(0,  (roundtrip[0] >> 10) & 0x1F);// B=0

            Assert.Equal(0,  roundtrip[1] & 0x1F);        // R=0
            Assert.Equal(31, (roundtrip[1] >> 5) & 0x1F); // G=31
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // #994: Functional edit->undo->redo round-trip for Battle Anime palette.
    // -----------------------------------------------------------------

    /// <summary>
    /// #994 (Copilot CLI review on PR #1041): exercises the HIGHEST-risk redo
    /// path — `Redo_Click` reloads via `ReloadFromAuthoritativeSlot()`, which
    /// re-reads `rom.p32(sourcePointerSlot)`. This test FORCES a relocation
    /// (all 16 colors edited to distinct high-entropy values so the
    /// recompressed block grows past the tiny all-zero original) and verifies
    /// the AUTHORITATIVE source-pointer slot rolls BACK on undo and FORWARD on
    /// redo, reading the palette THROUGH that slot (mirroring what
    /// ReloadFromAuthoritativeSlot does) rather than a hardcoded offset. A
    /// regression that restores the relocated bytes but fails to repoint the
    /// source slot would reload from the wrong palette — and now fails here.
    /// </summary>
    [Fact]
    public void BattleAnimePalette_EditUndoRedo_RoundTrip()
    {
        // All-zero original compresses to a tiny block; editing all 16 slots
        // to distinct high-entropy colors forces the recompressed block to
        // exceed the original => the RELOCATE branch.
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);

            // The source slot must initially point at the original palette.
            Assert.Equal(paletteOffset, U.toOffset(rom.p32(sourceSlot)));

            // Capture the original palette read THROUGH the authoritative slot.
            ushort[] origColors = ImageBattleAnimePaletteCore.ReadPalette(
                rom, U.toOffset(rom.p32(sourceSlot)), 0);
            Assert.NotNull(origColors);

            // Edit ALL 16 colors to DISTINCT high-entropy values so the
            // recompressed block is much larger than the all-zero original
            // (one-color edits stay in-place and would NOT relocate).
            var edited = new ushort[16];
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)((0x7FFF - i * 0x111) & 0x7FFF);
                edited[i] = c;
                vm.SetR(i, (byte)(((c) & 0x1F) << 3));
                vm.SetG(i, (byte)(((c >> 5) & 0x1F) << 3));
                vm.SetB(i, (byte)(((c >> 10) & 0x1F) << 3));
            }

            var undoService = new UndoService();
            undoService.Begin("Edit all 16 colors");
            uint newOffset = vm.Write();
            undoService.Commit();

            // Relocation must actually have happened.
            Assert.NotEqual(U.NOT_FOUND, newOffset);
            Assert.NotEqual(paletteOffset, newOffset);

            // The source slot must have been repointed to the new offset.
            Assert.Equal(newOffset, U.toOffset(rom.p32(sourceSlot)));

            // Palette read THROUGH the slot shows the edited colors.
            ushort[] editedColors = ImageBattleAnimePaletteCore.ReadPalette(
                rom, U.toOffset(rom.p32(sourceSlot)), 0);
            Assert.NotNull(editedColors);
            for (int i = 0; i < 16; i++)
                Assert.Equal(edited[i] & 0x7FFF, editedColors[i] & 0x7FFF);

            // -- Undo: the source slot pointer must roll BACK to paletteOffset.
            CoreState.Undo.RunUndo();
            Assert.Equal(paletteOffset, U.toOffset(rom.p32(sourceSlot)));
            // And the palette read THROUGH the slot shows the ORIGINAL colors.
            ushort[] undoneColors = ImageBattleAnimePaletteCore.ReadPalette(
                rom, U.toOffset(rom.p32(sourceSlot)), 0);
            Assert.NotNull(undoneColors);
            for (int i = 0; i < 16; i++)
                Assert.Equal(origColors[i] & 0x7FFF, undoneColors[i] & 0x7FFF);

            // CanRedo must be true after undo.
            Assert.True(CoreState.Undo.CanRedo, "CanRedo must be true after undo");

            // -- Redo: the source slot pointer must roll FORWARD to newOffset.
            CoreState.Undo.RunRedo();
            Assert.Equal(newOffset, U.toOffset(rom.p32(sourceSlot)));
            // And the palette read THROUGH the slot shows the EDITED colors.
            ushort[] redoneColors = ImageBattleAnimePaletteCore.ReadPalette(
                rom, U.toOffset(rom.p32(sourceSlot)), 0);
            Assert.NotNull(redoneColors);
            for (int i = 0; i < 16; i++)
                Assert.Equal(edited[i] & 0x7FFF, redoneColors[i] & 0x7FFF);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBattleAnimePalletView.axaml");
    }

    static string CodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBattleAnimePalletView.axaml.cs");
    }

    static string ViewModelPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImageBattleAnimePalletViewModel.cs");
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

    /// <summary>
    /// Build a synthetic FE8U ROM with one 32-byte battle-animation
    /// record. The palette pointer at entry+28 points to an LZ77-compressed
    /// single-slot palette block planted at 0x150000.
    /// </summary>
    static (ROM rom, uint paletteOffset, uint sourceSlot) MakeRomWithSingleSlotPalette(ushort[] colors)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x2000000], "BE8E01");

        // 64KB of free space (0xFF fill) for ImageImportCore.FindAndWriteData.
        for (uint i = 0x500000; i < 0x600000; i++)
            rom.Data[i] = 0xFF;

        // Plant battle-animation list pointer.
        const uint listBase = 0x140000;
        WriteU32(rom.Data, (int)rom.RomInfo.image_battle_animelist_pointer, U.toPointer(listBase));

        // One battle-animation record at listBase: 32 bytes, palette pointer
        // at +28 -> 0x150000.
        const uint paletteOffset = 0x150000;
        const uint sourceSlot = listBase + 28;
        WriteU32(rom.Data, (int)sourceSlot, U.toPointer(paletteOffset));

        // Plant LZ77-compressed single-slot palette block at paletteOffset.
        byte[] uncompressed = new byte[32]; // one 0x20 slot
        for (int i = 0; i < colors.Length && i < 16; i++)
        {
            U.write_u16(uncompressed, (uint)(i * 2), colors[i]);
        }
        byte[] compressed = LZ77.compress(uncompressed);
        for (int i = 0; i < compressed.Length; i++)
        {
            rom.Data[paletteOffset + i] = compressed[i];
        }

        return (rom, paletteOffset, sourceSlot);
    }

    /// <summary>
    /// Build a synthetic ROM with a 4-slot palette block (128 bytes
    /// decompressed). Used to verify the "32-color-mode is honestly
    /// deferred" assertion.
    /// </summary>
    static (ROM rom, uint paletteOffset, uint sourceSlot) MakeRomWithMultiSlotPalette()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x2000000], "BE8E01");
        for (uint i = 0x500000; i < 0x600000; i++)
            rom.Data[i] = 0xFF;

        const uint listBase = 0x140000;
        WriteU32(rom.Data, (int)rom.RomInfo.image_battle_animelist_pointer, U.toPointer(listBase));

        const uint paletteOffset = 0x150000;
        const uint sourceSlot = listBase + 28;
        WriteU32(rom.Data, (int)sourceSlot, U.toPointer(paletteOffset));

        // 4 slots = 128 bytes uncompressed. Fill with distinct GBA colors.
        byte[] uncompressed = new byte[128];
        for (int slot = 0; slot < 4; slot++)
        {
            for (int color = 0; color < 16; color++)
            {
                ushort gba = (ushort)((slot << 14) | color);
                U.write_u16(uncompressed, (uint)(slot * 32 + color * 2), gba);
            }
        }
        byte[] compressed = LZ77.compress(uncompressed);
        for (int i = 0; i < compressed.Length; i++)
        {
            rom.Data[paletteOffset + i] = compressed[i];
        }

        return (rom, paletteOffset, sourceSlot);
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>
    /// Build a synthetic ROM with ONE complete battle-animation record
    /// (section + frame + OAM + multi-block palette + solid graphics), enough
    /// for RenderSampleBattleAnime to produce a non-blank 360x290 grid. The
    /// geometry mirrors BattleAnimeSamplePreviewTests.MakeAnimeRom (a solid
    /// green 8x8 sprite centered to crop (0,0)). Returns the palette offset
    /// and the source pointer slot (record + 0x1C) the VM loads.
    /// </summary>
    static (ROM rom, uint paletteOffset, uint sourceSlot) MakeRomWithFullAnimeRecord()
        => MakeRomWithFullAnimeRecord(spritePaletteBank: 0);

    /// <summary>
    /// Overload that stamps the sprite's 16-color palette bank into OAM byte[5]
    /// (high nibble), so the #1033 OAM palette-bank scan
    /// (<c>BattleAnimeRendererCore.CountAnimationPaletteBanks</c>) sees a
    /// banked (32-color) animation when <paramref name="spritePaletteBank"/> >= 1.
    /// </summary>
    static (ROM rom, uint paletteOffset, uint sourceSlot) MakeRomWithFullAnimeRecord(int spritePaletteBank)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x2000000], "BE8E01");

        const uint listBase     = 0x140000; // animation list base (record 0 here)
        const uint sectionOff   = 0x141000;
        const uint frameOff     = 0x142000; // LZ77 frame stream
        const uint oamOff       = 0x143000; // LZ77 OAM
        const uint paletteOff   = 0x150000; // LZ77 multi-block palette
        const uint gfxGreen     = 0x160000; // LZ77 solid index-5 tile
        const uint sourceSlot   = listBase + 28;

        WriteU32(rom.Data, (int)rom.RomInfo.image_battle_animelist_pointer, U.toPointer(listBase));

        // ---- record at listBase ----
        WriteU32(rom.Data, (int)(listBase + 12), U.toPointer(sectionOff));
        WriteU32(rom.Data, (int)(listBase + 16), U.toPointer(frameOff));
        WriteU32(rom.Data, (int)(listBase + 20), U.toPointer(oamOff));
        WriteU32(rom.Data, (int)(listBase + 24), U.toPointer(oamOff)); // L-to-R (unused)
        WriteU32(rom.Data, (int)sourceSlot,      U.toPointer(paletteOff));

        // ---- frame stream: section 0 = one frame (gfxGreen, OAM offset 0) ----
        byte[] frameStream = new byte[12];
        frameStream[3] = 0x86;
        U.write_u32(frameStream, 4, U.toPointer(gfxGreen));
        U.write_u32(frameStream, 8, 0);
        PlantLZ77(rom, frameOff, frameStream);

        // ---- section array: section 0 = [0,12); rest empty (start = 12) ----
        for (int s = 0; s < 12; s++)
        {
            uint start = s == 0 ? 0u : 12u;
            WriteU32(rom.Data, (int)(sectionOff + s * 4), start);
        }

        // ---- OAM: one square 1x1 sprite, centered to crop (0,0) ----
        // imgX = vramX + 0x94 = 100 => vramX = -48; imgY = vramY + 0x58 = 30 => vramY = -58.
        byte[] oam = new byte[24];
        oam[0] = 0x00; oam[1] = 0x00; oam[2] = 0x00; oam[3] = 0x00;
        oam[4] = 0x00;
        // byte[5] high nibble = 16-color palette bank selector (#1033 scan).
        oam[5] = (byte)((spritePaletteBank & 0x0F) << 4);
        oam[6] = unchecked((byte)(-48 & 0xFF)); oam[7] = unchecked((byte)((-48 >> 8) & 0xFF));
        oam[8] = unchecked((byte)(-58 & 0xFF)); oam[9] = unchecked((byte)((-58 >> 8) & 0xFF));
        oam[12] = 0x01; // terminator
        PlantLZ77(rom, oamOff, oam);

        // ---- graphics: solid 8x8 tile of color index 5 (64 opaque px) ----
        byte packed = (byte)((5 << 4) | 5);
        byte[] tile = new byte[32];
        for (int i = 0; i < 32; i++) tile[i] = packed;
        PlantLZ77(rom, gfxGreen, tile);

        // ---- palette: 2 blocks; block 0 idx 5 = green (0x03E0) ----
        byte[] pal = new byte[64];
        U.write_u16(pal, (0 * 16 + 5) * 2, 0x03E0);
        U.write_u16(pal, (1 * 16 + 5) * 2, 0x7C00);
        PlantLZ77(rom, paletteOff, pal);

        return (rom, paletteOff, sourceSlot);
    }

    // -----------------------------------------------------------------
    // #871 FIX 1 - palette import does not require tile-size multiples.
    // -----------------------------------------------------------------

    [Fact]
    public void ImageImportService_RequireTileMultipleParam_ControlsDimensionCheck()
    {
        string svcCode = System.IO.File.ReadAllText(
            FindRepoRoot() + "/FEBuilderGBA.Avalonia/Services/ImageImportService.cs");
        Assert.Contains("bool requireTileMultiple = true", svcCode);
        Assert.Contains("requireTileMultiple && (image.Width % 8", svcCode);
        // DecreaseColorCore.Quantize handles arbitrary (non-8-multiple) dims.
        byte[] rgba = new byte[10 * 10 * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 255; rgba[i+3] = 255; }
        var qr = DecreaseColorCore.Quantize(rgba, 10, 10, 16);
        Assert.NotNull(qr);
        byte[] padded = PadHelper(qr.GBAPalette);
        Assert.Equal(32, padded.Length);
    }

    [Fact]
    public void View_DoImportFromFile_PassesRequireTileMultipleFalse()
    {
        string code = System.IO.File.ReadAllText(CodeBehindPath());
        Assert.Contains("requireTileMultiple: false", code);
    }

    // -----------------------------------------------------------------
    // #871 FIX 2 - snapshot before DoImport; restore on write failure.
    // -----------------------------------------------------------------

    [Fact]
    public void View_DoImportFromFile_HasSnapshotAndRestoreBoilerplate()
    {
        string code = System.IO.File.ReadAllText(CodeBehindPath());
        Assert.Contains("byte[] rSnap", code);
        Assert.Contains("gSnap[si] = _vm.GetG(si)", code);
        var matches = System.Text.RegularExpressions.Regex.Matches(code, "RestorePaletteSnapshot");
        Assert.True(matches.Count >= 3,
            $"RestorePaletteSnapshot must appear >= 3 times, got {matches.Count}");
    }

    [Fact]
    public void ViewModel_DoImport_ThenWriteFails_VmStillHoldsImportedState()
    {
        var (rom, paletteOffset, sourceSlot) = MakeRomWithSingleSlotPalette(new ushort[16]);
        var prevRom = CoreState.ROM; var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom; CoreState.Undo = new Undo();
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.Equal(0, vm.GetR(0));
            byte[] importPal = new byte[32];
            importPal[0] = 0x1F; importPal[1] = 0x00;
            bool applied = vm.DoImport(importPal);
            Assert.True(applied);
            Assert.Equal(248, vm.GetR(0));
            vm.SetWriterOverrideForTests((r, data) => U.NOT_FOUND);
            var undoService = new UndoService();
            undoService.Begin("test fail");
            uint result = vm.Write();
            Assert.Equal(U.NOT_FOUND, result);
            undoService.Rollback();
            Assert.Equal(248, vm.GetR(0)); // VM holds imported; view restores it
        }
        finally { CoreState.ROM = prevRom; CoreState.Undo = prevUndo; }
    }

    // -----------------------------------------------------------------
    // #871 FIX 3 - PadGBAPaletteTo16 returns exactly 32 bytes.
    // -----------------------------------------------------------------

    [Fact]
    public void View_PadGBAPaletteTo16_TruncatesOversizedInput_Returns32Bytes()
    {
        var method = typeof(ImageBattleAnimePalletView).GetMethod("PadGBAPaletteTo16",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        byte[] input = new byte[64];
        for (int i = 0; i < 64; i++) input[i] = (byte)((i + 1) % 256);
        byte[] result = (byte[])method.Invoke(null, new object[] { input });
        Assert.Equal(32, result.Length);
        for (int i = 0; i < 32; i++) Assert.Equal(input[i], result[i]);
    }

    [Fact]
    public void View_PadGBAPaletteTo16_PadsShortInput_Returns32Bytes()
    {
        var method = typeof(ImageBattleAnimePalletView).GetMethod("PadGBAPaletteTo16",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        byte[] input = new byte[10];
        input[0] = 0x1F;
        byte[] result = (byte[])method.Invoke(null, new object[] { input });
        Assert.Equal(32, result.Length);
        Assert.Equal(0x1F, result[0]);
        for (int i = 10; i < 32; i++) Assert.Equal(0, result[i]);
    }

    static byte[] PadHelper(byte[] gbaPalette)
    {
        const int needed = 32;
        byte[] padded = new byte[needed];
        if (gbaPalette != null)
            System.Array.Copy(gbaPalette, padded, System.Math.Min(gbaPalette.Length, needed));
        return padded;
    }

    static void PlantLZ77(ROM rom, uint offset, byte[] raw)
    {
        byte[] comp = LZ77.compress(raw);
        for (int i = 0; i < comp.Length; i++) rom.Data[offset + i] = comp[i];
    }
}
