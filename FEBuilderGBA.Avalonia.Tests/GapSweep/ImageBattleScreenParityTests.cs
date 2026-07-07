// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for ImageBattleScreenView (#393).
//
// Closes the 130 Avalonia ↔ WinForms gaps the gap-sweep methodology surfaced
// on `ImageBattleScreenForm` (HIGH density 133/3 == -97.7 %, 42 WF-only labels).
// After this PR `ImageBattleScreenView` rebuilds to the 5-tab battle-screen
// layout editor the WinForms form exposes, with the TSA + palette + image
// pointer I/O extracted to FEBuilderGBA.Core/ImageBattleScreenCore.cs.
//
// Copilot CLI plan-review v1 issues addressed:
//   1. Image label swap: image2/4 = Name, image3/5 = Item. Verified via the
//      WF Designer.cs L1971-2202.
//   2. Undo ownership: Core methods take NO Undo.UndoData parameter — they
//      rely on the ambient ROM.BeginUndoScope only. Tests verify the view
//      wraps Write() in _undoService.Begin/Commit/Rollback.
//   3. PaletteCore reuse: ReadPaletteBlock / WritePaletteBlock delegate to
//      PaletteCore — single source of truth for BGR15 packing.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImageBattleScreenForm parity raise (#393) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner
/// can race a sibling test's ROM swap.
/// </summary>
[Collection("SharedState")]
public class ImageBattleScreenParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 133 control instantiations (per 2026-05-21
    /// density sweep). To leave the HIGH verdict we need
    /// AV >= ceil(133 * 0.75) = 100.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 133;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 100
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75 % of WF={WfControlCount}) to leave the HIGH verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions per tab.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTopToolbar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_AllWrite_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Zoom_Combo\"", axaml);
        // Per Copilot bot PR #594 round 5 thread #1: TSAInfo is split into
        // two separate translatable TextBlocks (Selected + Canvas) so each
        // fragment goes through ViewTranslationHelper independently.
        Assert.Contains("AutomationId=\"ImageBattleScreen_TSAInfoSelected_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_TSAInfoCanvas_Label\"", axaml);
    }

    /// <summary>
    /// Per Copilot bot PR #594 round 5 thread #2: WriteButton_Click must
    /// refuse to write if the VM has not been loaded yet (default zeros
    /// would wipe the ROM TSA + image pointer slots).
    /// </summary>
    [Fact]
    public void View_WriteHandler_GuardsAgainstUnloadedVM()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The handler must check _vm.IsLoaded before invoking Write().
        var match = Regex.Match(code,
            @"void\s+WriteButton_Click[\s\S]*?(?=\n\s{4,}void\s|\n\s{0,4}\})",
            RegexOptions.Singleline);
        Assert.True(match.Success, "Could not locate WriteButton_Click handler");
        Assert.Matches(new Regex(@"if\s*\(\s*!_vm\.IsLoaded", RegexOptions.Singleline), match.Value);
    }

    /// <summary>
    /// Per Copilot bot PR #594 round 5 thread #3: PaletteWrite_Click must
    /// refuse to write if the VM has not been loaded yet (default zero
    /// R/G/B arrays would write a black palette over the existing one).
    /// </summary>
    [Fact]
    public void View_PaletteWriteHandler_GuardsAgainstUnloadedVM()
    {
        string code = File.ReadAllText(CodeBehindPath());
        var match = Regex.Match(code,
            @"void\s+PaletteWrite_Click[\s\S]*?(?=\n\s{4,}void\s|\n\s{0,4}\})",
            RegexOptions.Singleline);
        Assert.True(match.Success, "Could not locate PaletteWrite_Click handler");
        Assert.Matches(new Regex(@"if\s*\(\s*!_vm\.IsLoaded", RegexOptions.Singleline), match.Value);
    }

    /// <summary>
    /// #802: the Battle preview is now rendered LIVE via a GbaImageControl
    /// (ImageBattleScreenCore.RenderBattleScreenPreview), replacing the old
    /// deferred KnownGap placeholder label. #805: the Chipset chip list is now
    /// ALSO rendered live (verified by View_HasLiveChipsetPreviewImage).
    /// </summary>
    [Fact]
    public void View_HasLiveBattlePreviewImage()
    {
        string axaml = ReadAxaml();
        // The live preview control carries the BattlePreview AutomationId and
        // is a GbaImageControl (Image suffix per AutomationId convention).
        Assert.Contains("AutomationId=\"ImageBattleScreen_BattlePreview_Image\"", axaml);
        Assert.Contains("controls:GbaImageControl", axaml);
        // The old deferred placeholder label must be gone.
        Assert.DoesNotContain("AutomationId=\"ImageBattleScreen_BattlePreview_Label\"", axaml);
        // The code-behind must wire the render into the control.
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"BattlePreview\.SetImage\(\s*_vm\.RenderBattlePreview\(\)\s*\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// #769 item 1: the composited battle-screen preview can be exported to PNG
    /// (read-only, mirrors WF ImageBattleScreenForm.ExportButton_Click). The
    /// toolbar carries a distinct Export PNG button wired to a Click handler,
    /// starts disabled (gated until a render succeeds), and the code-behind
    /// drives both the gate flag and the button IsEnabled from BattlePreview.
    /// </summary>
    [Fact]
    public void View_HasBattleExportPngButton_WiredAndGated()
    {
        string axaml = ReadAxaml();
        // Distinct id — must NOT reuse the inert per-image export buttons.
        Assert.Contains("AutomationId=\"ImageBattleScreen_BattleExportPng_Button\"", axaml);
        Assert.Contains("Click=\"BattleExportPng_Click\"", axaml);
        // Starts disabled until a render succeeds.
        Assert.Matches(new Regex(
            "ImageBattleScreen_BattleExportPng_Button\"[\\s\\S]*?IsEnabled=\"False\"",
            RegexOptions.Singleline), axaml);

        string code = File.ReadAllText(CodeBehindPath());
        // Handler exports the live preview surface (no ROM write).
        Assert.Matches(new Regex(
            @"BattlePreview\.ExportPng\(\s*TopLevel\.GetTopLevel\(this\)\s+as\s+Window", RegexOptions.Singleline), code);
        // Gate is driven from HasImage and applied to the button (no DataContext).
        Assert.Matches(new Regex(
            @"_vm\.CanExportBattle\s*=\s*BattlePreview\.HasImage", RegexOptions.Singleline), code);
        Assert.Matches(new Regex(
            @"BattleExportPngButton\.IsEnabled\s*=\s*_vm\.CanExportBattle", RegexOptions.Singleline), code);
        // Failure path resets the gate so a throwing reselect can't leave it enabled.
        Assert.Matches(new Regex(
            @"catch[\s\S]*?_vm\.CanExportBattle\s*=\s*false", RegexOptions.Singleline), code);
    }

    /// <summary>
    /// The export gate defaults to false on a fresh VM (no render yet), and the
    /// preview render — its data source — returns null when no ROM is loaded, so
    /// the gate stays closed (HasImage would be false). Locks the gating contract.
    /// </summary>
    [Fact]
    public void VM_CanExportBattle_DefaultsFalse_AndNoRomRenderIsNull()
    {
        ROM prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageBattleScreenViewModel();
            Assert.False(vm.CanExportBattle);
            Assert.Null(vm.RenderBattlePreview());
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// #805: the Chipset chip list is now rendered LIVE via a GbaImageControl
    /// (ImageBattleScreenCore.RenderChipsetPreview, mirroring WF MakeCHIPLIST),
    /// replacing the old deferred KnownGap placeholder label. The new control
    /// carries the ChipsetPreview_Image AutomationId; the old _Label is gone.
    /// </summary>
    [Fact]
    public void View_HasLiveChipsetPreviewImage()
    {
        string axaml = ReadAxaml();
        // The live chip-list control carries the ChipsetPreview AutomationId and
        // is a GbaImageControl (Image suffix per AutomationId convention).
        Assert.Contains("AutomationId=\"ImageBattleScreen_ChipsetPreview_Image\"", axaml);
        Assert.Contains("Name=\"ChipsetPreview\"", axaml);
        // The old deferred placeholder label must be gone.
        Assert.DoesNotContain("AutomationId=\"ImageBattleScreen_ChipsetPreview_Label\"", axaml);
        // The code-behind must wire the render into the control.
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"ChipsetPreview\.SetImage\(\s*_vm\.RenderChipsetPreview\(\)\s*\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// #816: each per-image preview (Image1..Image5) is now rendered LIVE via
    /// its own GbaImageControl at its WF per-image dimensions
    /// (ImageBattleScreenCore.RenderSingleImagePreview), replacing the 5 old
    /// deferred placeholder labels. Each new control carries the
    /// Image{N}_Preview_Image AutomationId; the old _Preview_Label ids are gone;
    /// the VM exposes RenderImagePreview(n); and the code-behind refreshes all 5.
    /// </summary>
    [Fact]
    public void View_HasLivePerImagePreviewImages()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 5; i++)
        {
            // Each per-image preview is a live GbaImageControl.
            Assert.Contains($"AutomationId=\"ImageBattleScreen_Image{i}_Preview_Image\"", axaml);
            Assert.Contains($"Name=\"Image{i}Preview\"", axaml);
            // The old deferred placeholder labels must be gone.
            Assert.DoesNotContain($"AutomationId=\"ImageBattleScreen_Image{i}_Preview_Label\"", axaml);
        }

        // VM delegates to the Core per-image renderer.
        string vmCode = File.ReadAllText(ViewModelPath());
        Assert.Matches(new Regex(
            @"public\s+IImage\s+RenderImagePreview\(int\s+\w+\)[\s\S]*?ImageBattleScreenCore\.RenderSingleImagePreview\(CoreState\.ROM,",
            RegexOptions.Singleline), vmCode);

        // Code-behind refreshes all 5 per-image previews (entry load + writes).
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void\s+RefreshImagePreviews\(\)[\s\S]*?\.SetImage\(\s*_vm\.RenderImagePreview\(",
            RegexOptions.Singleline), code);
        // RefreshImagePreviews must be invoked on the entry-load path (OnSelected).
        Assert.Matches(new Regex(
            @"void\s+OnSelected[\s\S]*?RefreshImagePreviews\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_HasPaletteTab()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteIndex_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteWrite_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteClipboard_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteUndo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_PaletteRedo_Button\"", axaml);
    }

    [Fact]
    public void View_HasPalette16RowGrid()
    {
        string axaml = ReadAxaml();
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"AutomationId=\"ImageBattleScreen_R{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleScreen_G{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleScreen_B{i}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleScreen_Swatch{i}_Label\"", axaml);
            Assert.Contains($"AutomationId=\"ImageBattleScreen_Index{i}_Label\"", axaml);
        }
    }

    [Fact]
    public void View_HasMainImageTab()
    {
        string axaml = ReadAxaml();
        // Tab title + tile labels 1..5 + image1 ZIMAGE + Import/Export + image preview.
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"AutomationId=\"ImageBattleScreen_Tile{i}_Label\"", axaml);
        }
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image1_ZIMAGE_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image1_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image1_Export_Button\"", axaml);
        // #816: the image1 preview is now a live GbaImageControl (rendered at
        // its natural W x H), replacing the old deferred KnownGap label.
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image1_Preview_Image\"", axaml);
    }

    [Fact]
    public void View_HasLeftSideTab_Image2IsName_Image3IsItem()
    {
        // Per Copilot CLI plan-review round 1 #1: image2 = Name (左/名前),
        // image3 = Item (左/アイテム). Verified via WF Designer.cs L1971-2072.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image2_ZIMAGE_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image2_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image2_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image2_Name_Label\"", axaml);

        Assert.Contains("AutomationId=\"ImageBattleScreen_Image3_ZIMAGE_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image3_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image3_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image3_Item_Label\"", axaml);
    }

    [Fact]
    public void View_HasRightSideTab_Image4IsName_Image5IsItem()
    {
        // Per Copilot CLI plan-review round 1 #1: image4 = Name (右/名前),
        // image5 = Item (右/アイテム). Verified via WF Designer.cs L2096-2202.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image4_ZIMAGE_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image4_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image4_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image4_Name_Label\"", axaml);

        Assert.Contains("AutomationId=\"ImageBattleScreen_Image5_ZIMAGE_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image5_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image5_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image5_Item_Label\"", axaml);
    }

    [Fact]
    public void View_HasImportExportTab()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_BulkImport_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_BulkExport_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_BulkUndo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_BulkRedo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImageBattleScreen_BulkDescription_Label\"", axaml);
    }

    [Fact]
    public void View_AllNumericsHaveIncrement8()
    {
        // All 48 R/G/B NumericUpDowns must have Increment="8" (matches WF
        // 5-bit step pattern).
        string axaml = ReadAxaml();
        var increment8Count = Regex.Matches(axaml,
            @"Name=""[RGB]\d+""[^/]*Increment=""8""", RegexOptions.Singleline).Count;
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
    /// #802: now that the live Battle preview is rendered, the ZoomCombo is
    /// re-enabled and wired to drive the GbaImageControl's zoom. The earlier
    /// IsEnabled="False" KnownGap deferral is gone.
    /// </summary>
    [Fact]
    public void View_ZoomCombo_IsEnabledAndDrivesPreviewZoom()
    {
        string axaml = ReadAxaml();
        // The combo must NOT carry the disabled-KnownGap marker any more.
        Assert.DoesNotMatch(new Regex(
            @"AutomationId=""ImageBattleScreen_Zoom_Combo""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
        // It must wire a SelectionChanged handler.
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleScreen_Zoom_Combo""[\s\S]{0,400}SelectionChanged=""Zoom_SelectionChanged""",
            RegexOptions.Singleline), axaml);
        // And the handler must drive the live preview's zoom.
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void\s+Zoom_SelectionChanged[\s\S]*?BattlePreview\.Zoom\s*=",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_BulkImportExport_IsHonestlyDeferredKnownGap()
    {
        // Per Plan v2: bulk Import/Export are WF-coupled. Buttons are
        // rendered but disabled with explanatory tooltips.
        string axaml = ReadAxaml();
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleScreen_BulkImport_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleScreen_BulkExport_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
    }

    /// <summary>
    /// #872: per-image Import/Export buttons (Image1..5) are now fully wired —
    /// they carry Click handlers (not IsEnabled="False" stubs). This replaces the
    /// old "IsHonestlyDeferredKnownGap" assertion.
    /// </summary>
    [Fact]
    public void View_PerImageImportExport_IsWiredAndEnabled()
    {
        // Per #872: buttons are wired with Click handlers and must NOT carry
        // the old KnownGap IsEnabled="False" stub marker.
        string axaml = ReadAxaml();
        for (int i = 1; i <= 5; i++)
        {
            // Import button must have a Click handler.
            Assert.Matches(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Import_Button""[\s\S]{{0,200}}Click=""Image{i}Import_Click""",
                RegexOptions.Singleline), axaml);
            // Export button must have a Click handler.
            Assert.Matches(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Export_Button""[\s\S]{{0,200}}Click=""Image{i}Export_Click""",
                RegexOptions.Singleline), axaml);
            // The KnownGap IsEnabled="False" stub must be gone.
            Assert.DoesNotMatch(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Import_Button""[\s\S]{{0,400}}IsEnabled=""False""",
                RegexOptions.Singleline), axaml);
            Assert.DoesNotMatch(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Export_Button""[\s\S]{{0,400}}IsEnabled=""False""",
                RegexOptions.Singleline), axaml);
        }
    }

    // -----------------------------------------------------------------
    // #994: PaletteRedo button wired, BulkRedo stays deferred.
    // -----------------------------------------------------------------

    /// <summary>
    /// #994: the PaletteRedo button is now wired to CoreState.Undo.RunRedo().
    /// Must carry Click="PaletteRedo_Click" and must NOT contain IsEnabled="False".
    /// </summary>
    [Fact]
    public void View_PaletteRedoButton_IsWiredAndEnabled()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageBattleScreen_PaletteRedo_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "PaletteRedo button tag not found");
        Assert.Contains("Click=\"PaletteRedo_Click\"", m.Value);
        Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
    }

    /// <summary>
    /// #994: PaletteRedo_Click handler must call CoreState.Undo.RunRedo()
    /// and guard on CanRedo. CanRedo must appear before RunRedo() within
    /// the PaletteRedo_Click method body.
    /// </summary>
    [Fact]
    public void View_PaletteRedoHandler_CallsRunRedo()
    {
        string source = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void\s+PaletteRedo_Click[\s\S]*?CoreState\.Undo\.CanRedo",
            RegexOptions.Compiled), source);
        Assert.Matches(new Regex(
            @"void\s+PaletteRedo_Click[\s\S]*?CoreState\.Undo\.RunRedo\(\)",
            RegexOptions.Compiled), source);
        // Extract only the PaletteRedo_Click method body to check ordering.
        int methodStart = source.IndexOf("void PaletteRedo_Click(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "PaletteRedo_Click method not found");
        int idxCanRedo = source.IndexOf("CoreState.Undo.CanRedo", methodStart, StringComparison.Ordinal);
        int idxRunRedo = source.IndexOf("CoreState.Undo.RunRedo()", methodStart, StringComparison.Ordinal);
        Assert.True(idxCanRedo >= 0 && idxRunRedo >= 0);
        Assert.True(idxCanRedo < idxRunRedo, "CanRedo must appear before RunRedo()");
    }

    /// <summary>
    /// #994 regression guard: BulkRedo stays a documented deferral (#988) —
    /// it must still have IsEnabled="False".
    /// </summary>
    [Fact]
    public void View_BulkRedoButton_StaysDocumentedDeferral()
    {
        string axaml = ReadAxaml();
        Assert.Matches(new Regex(
            "AutomationId=\"ImageBattleScreen_BulkRedo_Button\"[\\s\\S]{0,400}IsEnabled=\"False\"",
            RegexOptions.Compiled), axaml);
    }

    /// <summary>
    /// #994: write a palette color via WritePalette, undo it, verify CanRedo
    /// is true, redo it, verify the palette bytes return to the edited state.
    /// </summary>
    [Fact]
    public void BattleScreenPalette_EditUndoRedo_RoundTrip()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();

            // Capture original palette color[0] from ROM.
            byte origLo = rom.Data[0x105000];
            byte origHi = rom.Data[0x105001];

            // Edit slot 0 to a known color (pure green = R0, G248, B0).
            vm.SetR(0, 0);
            vm.SetG(0, 248);
            vm.SetB(0, 0);

            var undoService = new UndoService();
            undoService.Begin("Edit Palette");
            bool wrote = vm.WritePalette();
            undoService.Commit();
            Assert.True(wrote, "WritePalette must succeed");

            byte editedLo = rom.Data[0x105000];
            byte editedHi = rom.Data[0x105001];
            Assert.False(editedLo == origLo && editedHi == origHi,
                "ROM bytes should have changed after palette write");

            // Undo.
            CoreState.Undo.RunUndo();
            Assert.Equal(origLo, rom.Data[0x105000]);
            Assert.Equal(origHi, rom.Data[0x105001]);

            // CanRedo must be true after undo.
            Assert.True(CoreState.Undo.CanRedo, "CanRedo must be true after undo");

            // Redo.
            CoreState.Undo.RunRedo();
            Assert.Equal(editedLo, rom.Data[0x105000]);
            Assert.Equal(editedHi, rom.Data[0x105001]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Phase 5 - Code-behind / write-handler assertions.
    // -----------------------------------------------------------------

    /// <summary>
    /// Per acceptance criterion #3: ALL write paths in the view MUST wrap
    /// the VM Write call in _undoService.Begin / Commit / Rollback. The
    /// WriteButton_Click handler is the bulk write.
    /// </summary>
    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"WriteButton_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?_vm\.Write\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// The PaletteWrite_Click handler MUST also wrap its write call in
    /// the UndoService scope so palette changes are atomic.
    /// </summary>
    [Fact]
    public void View_PaletteWriteHandler_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"PaletteWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?_vm\.WritePalette\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_WriteHandler_RollsBackOnFailure()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The handler must check for a failure and call Rollback.
        Assert.Matches(new Regex(
            @"_undoService\.Rollback\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_PaletteIndexCombo_HasFourEntries()
    {
        string code = File.ReadAllText(CodeBehindPath());
        var matches = Regex.Matches(code, @"PaletteIndexCombo\.Items\.Add\(");
        Assert.Equal(4, matches.Count);
    }

    [Fact]
    public void View_ZoomCombo_HasFourEntries()
    {
        // 1x..4x zoom matches WF's 1/2/3/4 zoom indices.
        string code = File.ReadAllText(CodeBehindPath());
        var matches = Regex.Matches(code, @"ZoomCombo\.Items\.Add\(");
        Assert.Equal(4, matches.Count);
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic ROM).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_ReadsMapAndPalette()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();
            Assert.True(vm.IsLoaded);
            Assert.NotNull(vm.Map);
            Assert.Equal(640, vm.Map.Length);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_RoundTripsThroughCoreUnderUndo()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();

            // Mutate one TSA cell so the write is non-trivial.
            vm.Map[0 * 32 + 1] = 0xABCD;

            var undoService = new UndoService();
            undoService.Begin("test write");
            bool ok = vm.Write();
            undoService.Commit();

            Assert.True(ok);

            // Re-load and verify the mutation survived.
            var roundtrip = new ImageBattleScreenViewModel();
            roundtrip.LoadEntry();
            Assert.Equal((ushort)0xABCD, roundtrip.Map[0 * 32 + 1]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ViewModel_Write_TrackedWriteFails_RollbackRunsUndoAndRestoresRom()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // Snapshot the TSA1 region.
            uint tsa1Addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            byte[] before = new byte[16];
            for (int i = 0; i < 16; i++) before[i] = rom.Data[tsa1Addr + i];

            int initialCount = CoreState.Undo.UndoBuffer.Count;
            int initialPostion = CoreState.Undo.Postion;

            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();
            vm.Map[0 * 32 + 1] = 0xABCD;

            // Inject a writer that performs a tracked write THEN returns false.
            vm.SetWriterOverrideForTests((r, m) =>
            {
                r.write_u16(tsa1Addr, 0xFFFF); // partial tracked write
                return false;
            });

            var undoService = new UndoService();
            undoService.Begin("test failing write");
            bool result = vm.Write();
            Assert.False(result);
            undoService.Rollback();

            // ROM bytes at tsa1Addr must be restored.
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(before[i], rom.Data[tsa1Addr + i]);
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
    /// Static guard (per Copilot CLI PR #594 round 3 finding): VM.Write()
    /// must return false (not true) when any of the 5 WriteImagePointer
    /// calls returns false. Roslyn regex over the VM source ensures each
    /// call is wrapped in 'if (!...) return false;'.
    ///
    /// We assert via static code analysis rather than runtime injection
    /// because RomInfo.battle_screen_imageN_pointer values are protected
    /// constants on the ROMFE8U class -- there is no test-friendly way to
    /// force WriteImagePointer to return false at runtime without
    /// reflection-against-protected-setters or shrinking rom.Data
    /// (which collapses the entire test ROM). The Core-level tests
    /// (WriteImagePointer_InvalidPointerSlot_ReturnsFalse) exercise the
    /// false-return path directly; this guard ensures the VM consumes it.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PropagatesImagePointerFailures()
    {
        string vmCode = File.ReadAllText(ViewModelPath());
        // Each of the 5 WriteImagePointer calls must be guarded by a
        // `if (!...) return false;` so a failing slot bails out.
        for (int i = 1; i <= 5; i++)
        {
            Assert.Matches(new Regex(
                @"if\s*\(\s*!ImageBattleScreenCore\.WriteImagePointer\(rom,\s*rom\.RomInfo\.battle_screen_image" + i + @"_pointer,\s*_image" + i + @"Pointer\)\s*\)\s*return\s+false",
                RegexOptions.Singleline), vmCode);
        }
    }

    static string ViewModelPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImageBattleScreenViewModel.cs");
    }

    /// <summary>
    /// Per Copilot CLI PR review round 1 finding #2: switching palette
    /// type must reload ONLY the palette R/G/B rows -- pending image
    /// pointer edits in the VM must survive. WF
    /// PaletteFormRef.MakePaletteROMToUI only updates the palette UI on
    /// palette-index changes, not the image pointer fields.
    /// </summary>
    [Fact]
    public void ViewModel_LoadPalette_PreservesPendingImagePointerEdits()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();

            // Stash original pointer values for comparison.
            uint originalImage1 = vm.Image1Pointer;
            uint originalImage5 = vm.Image5Pointer;

            // Simulate the user typing new image-pointer values into the UI
            // (which mutates the VM via the property setters).
            const uint pendingImage1 = 0x12345678u;
            const uint pendingImage5 = 0x87654321u;
            vm.Image1Pointer = pendingImage1;
            vm.Image5Pointer = pendingImage5;
            Assert.NotEqual(originalImage1, vm.Image1Pointer);
            Assert.NotEqual(originalImage5, vm.Image5Pointer);

            // Switch palette type and call LoadPalette() -- the equivalent
            // of what PaletteIndex_SelectionChanged does after this fix.
            vm.PaletteIndex = 1;
            vm.LoadPalette();

            // Image pointers MUST still hold the pending edits.
            Assert.Equal(pendingImage1, vm.Image1Pointer);
            Assert.Equal(pendingImage5, vm.Image5Pointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Static check: the view's PaletteIndex_SelectionChanged handler must
    /// call _vm.LoadPalette(), not _vm.LoadEntry(), so it cannot clobber
    /// the image-pointer fields.
    /// </summary>
    [Fact]
    public void View_PaletteIndexHandler_CallsLoadPaletteNotLoadEntry()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The handler must use LoadPalette(), not LoadEntry().
        Assert.Matches(new Regex(
            @"PaletteIndex_SelectionChanged[\s\S]*?_vm\.LoadPalette\(\)",
            RegexOptions.Singleline), code);
        // And the handler section MUST NOT call _vm.LoadEntry().
        var match = Regex.Match(code,
            @"void\s+PaletteIndex_SelectionChanged[\s\S]*?(?=\n\s{4,}void\s|\n\s{0,4}\})",
            RegexOptions.Singleline);
        Assert.True(match.Success, "Could not locate PaletteIndex_SelectionChanged handler");
        Assert.DoesNotContain("_vm.LoadEntry()", match.Value);
    }

    /// <summary>
    /// Per Copilot bot PR #594 inline review #3: AddrLabel (under "TSA1
    /// Address" bar) must reflect the TSA1 offset, not the palette
    /// address. Static regression guard so the wiring stays correct.
    /// </summary>
    [Fact]
    public void View_AddrLabel_DisplaysTSA1AddressNotPaletteAddress()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The PopulateUI helper must format the TSA1 address into AddrLabel.
        Assert.Matches(new Regex(
            @"AddrLabel\.Text\s*=\s*string\.Format\([^)]*_vm\.TSA1Address",
            RegexOptions.Singleline), code);
        // And it must NOT format PaletteAddress into AddrLabel.
        Assert.DoesNotMatch(new Regex(
            @"AddrLabel\.Text\s*=\s*string\.Format\([^)]*_vm\.PaletteAddress",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesTSA1AddressFromRom()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();
            Assert.Equal((uint)0x100000, vm.TSA1Address);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_NoTrackedWriteFails_RollbackIsNoOp()
    {
        var rom = MakeSyntheticRom();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            int initialCount = CoreState.Undo.UndoBuffer.Count;
            int initialPostion = CoreState.Undo.Postion;

            var vm = new ImageBattleScreenViewModel();
            vm.LoadEntry();
            // Inject a writer that immediately fails (no writes).
            vm.SetWriterOverrideForTests((r, m) => false);

            var undoService = new UndoService();
            undoService.Begin("test no-write failing write");
            bool result = vm.Write();
            undoService.Rollback();

            Assert.False(result);
            // No spurious Push must happen.
            Assert.Equal(initialPostion, CoreState.Undo.Postion);
            Assert.Equal(initialCount, CoreState.Undo.UndoBuffer.Count);
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
            "ImageBattleScreenView.axaml");
    }

    static string CodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBattleScreenView.axaml.cs");
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
    /// Build a synthetic FE8U ROM with planted TSA/palette/image pointers
    /// so the VM Load/Write paths can execute end-to-end.
    /// </summary>
    static ROM MakeSyntheticRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x2000000];
        // Use Array.Fill for the 32MB free-space init (Copilot bot
        // PR #594 round 3 finding #2 perf/readability).
        Array.Fill(data, (byte)0xFF);
        rom.LoadLow("synth.gba", data, "BE8E01");

        // Plant the 5 TSA pointer slots at arbitrary offsets.
        WriteU32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(0x100000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_TSA2_pointer, U.toPointer(0x101000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(0x102000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_TSA4_pointer, U.toPointer(0x103000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_TSA5_pointer, U.toPointer(0x104000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(0x105000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_image1_pointer, U.toPointer(0x106000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_image2_pointer, U.toPointer(0x107000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(0x108000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_image4_pointer, U.toPointer(0x109000));
        WriteU32(rom.Data, rom.RomInfo.battle_screen_image5_pointer, U.toPointer(0x10A000));

        // Zero-fill the TSA + palette regions.
        for (uint i = 0; i < 0x5000; i++) rom.Data[0x100000 + i] = 0;
        for (uint i = 0; i < 0x80; i++) rom.Data[0x105000 + i] = 0;
        return rom;
    }

    static void WriteU32(byte[] data, uint offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
