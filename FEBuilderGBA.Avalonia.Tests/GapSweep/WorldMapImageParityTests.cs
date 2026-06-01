// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for WorldMapImageView. (#395)
//
// Closes the 151 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `WorldMapImageForm` (HIGH density 3/107 -> -97.2%, 47 WF-only labels,
// 0 common labels). The fix raises WorldMapImageView from a single-stub
// editor to a 6-tab editor mirroring the WF tab structure exactly:
//   tabPage1 / Main      - 4 main-map pointer NUDs + import/export/dark/decrease/source.
//   tabPage2 / Event     - 3 NUDs (image/palette/zheadertsa) + import/export/decrease.
//   tabPage3 / Mini      - 2 NUDs (image/palette) + import/export.
//   tabPage4 / PointIcon - 3 NUDs (Point1/Point2/Road) + 6 import/export buttons.
//   tabPage5 / Border    - pointer-backed AddressList + 4 NUDs + Address bar.
//   tabPage6 / IconData  - pointer-backed AddressList + 12 NUDs + 9 row labels.
//
// Copilot CLI v1 plan-review surfaced four blockers that v2 corrected and
// these tests pin in place:
//   C1 - AllWriteButton MUST persist all 13 canonical pointer slots
//        (4 main + 3 event + 2 mini + 3 point/icon + 1 icon-palette) in
//        one undo scope; not just the 4 main-map pointers.
//   C2 - Border/Icon list terminator predicates use raw u32 (NOT p32)
//        validated by U.isPointer(...). p32 is only used after the
//        predicate confirms validity.
//   C3 - No per-tab Undo controls (WinForms has none either). Exactly
//        one top-level Undo button.
//   C4 - ko.txt remains unmapped per repo convention; L10nCoverageTest
//        scans only ja+zh.

using System;
using System.IO;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the WorldMapImageForm parity raise (#395) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner
/// can race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class WorldMapImageParityTests
{
    // ===================================================================
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // ===================================================================

    /// <summary>
    /// WF designer.cs reports 107 control instantiations (per the
    /// 2026-05-21 density-sweep manifest). To leave the HIGH verdict
    /// we need AV >= ceil(107 * 0.75) = 81.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 107;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 81
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // ===================================================================
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // ===================================================================

    [Fact]
    public void View_HasSixTabs()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"WorldMapImage_Main_Tab\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Event_Tab\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Mini_Tab\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_PointIcon_Tab\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_Tab\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_Tab\"", axaml);
    }

    [Fact]
    public void View_HasAllThirteenPointerNuds()
    {
        // 4 main + 3 event + 2 mini + 3 point/icon + 1 (icon palette is
        // shared by point/icon; total = 13).
        string axaml = ReadAxaml();
        // Main tab (4 pointers).
        Assert.Contains("AutomationId=\"WorldMapImage_Main_Image_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Main_Palette_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Main_DarkPalette_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Main_PaletteMap_Input\"", axaml);
        // Event tab (3 pointers).
        Assert.Contains("AutomationId=\"WorldMapImage_Event_Image_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Event_Palette_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Event_TSA_Input\"", axaml);
        // Mini tab (2 pointers).
        Assert.Contains("AutomationId=\"WorldMapImage_Mini_Image_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Mini_Palette_Input\"", axaml);
        // PointIcon tab (3 image pointers + 1 shared palette).
        Assert.Contains("AutomationId=\"WorldMapImage_PointIcon_Point1Image_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_PointIcon_Point2Image_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_PointIcon_RoadImage_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_PointIcon_IconPalette_Input\"", axaml);
    }

    [Fact]
    public void View_HasExactlyOneAllWriteButton_AndOneUndoButton()
    {
        string axaml = ReadAxaml();
        // Exactly ONE Write-All button (top global panel) — mirrors WF
        // AllWriteButton "ポインタを書き込む". Per Copilot plan-review C1,
        // this writes all 13 pointer slots in one undo scope.
        AssertOccurrences(axaml, "AutomationId=\"WorldMapImage_WriteAll_Button\"", 1);
        // Exactly ONE Undo button (top-level). Per Copilot plan-review C3,
        // there are NO per-tab Undo controls.
        AssertOccurrences(axaml, "AutomationId=\"WorldMapImage_Undo_Button\"", 1);
    }

    [Fact]
    public void View_HasBorderTabControls()
    {
        string axaml = ReadAxaml();
        // AddressList for the border table. Suffix is `_List` (per the
        // AutomationId naming convention enforced by
        // scripts/validate-automation-ids.ps1).
        Assert.Contains("AutomationId=\"WorldMapImage_Border_Entry_List\"", axaml);
        // The 4 fields P0 (image ptr), P4 (oam ptr), W8 (width), W10 (height).
        Assert.Contains("AutomationId=\"WorldMapImage_Border_P0_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_P4_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_W8_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_W10_Input\"", axaml);
        // Address bar (Address NUD + SelectAddress Label + BlockSize + Write).
        Assert.Contains("AutomationId=\"WorldMapImage_Border_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_SelectAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_BlockSize_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_Write_Button\"", axaml);
        // Read panel.
        // #668: NUD-based "ReadStartAddress" / "ReadCount" surfaces became
        // read-only TextBlock slots inside the unified EditorTopBar, so the
        // *_Input AutomationIds were renamed to *_Label (the values are
        // now display-only). The Reload_Button and AddressListExpand_Button
        // AutomationIds are preserved unchanged.
        Assert.Contains("AutomationId=\"WorldMapImage_Border_ReadStartAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_Border_AddressListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_HasIconDataTabControls()
    {
        string axaml = ReadAxaml();
        // AddressList for the icon-data table. Suffix is `_List`.
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_Entry_List\"", axaml);
        // The 12 fields: B0..B3 (bytes 0..3), P4 (pointer at +4), B8..B13, W14.
        for (int i = 0; i <= 3; i++)
        {
            Assert.Contains($"AutomationId=\"WorldMapImage_IconData_B{i}_Input\"", axaml);
        }
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_P4_Input\"", axaml);
        for (int i = 8; i <= 13; i++)
        {
            Assert.Contains($"AutomationId=\"WorldMapImage_IconData_B{i}_Input\"", axaml);
        }
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_W14_Input\"", axaml);
        // Address bar + Write button + Read panel.
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_SelectAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_BlockSize_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_Write_Button\"", axaml);
        // #668: NUD inputs → EditorTopBar read-only Label slots (see
        // Border-tab note above).
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_ReadStartAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_Reload_Button\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_AddressListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_IconDataTab_HasNineUniqueRowLabels()
    {
        // Per WF designer, the 12 NUDs are labeled with 9 unique labels:
        // - B0: Image Sheet Number (画像シート番号)
        // - B1: 00 Padding (alias for B0..B3 secondary byte indicator)
        // - P4: OAMTable entry
        // - B8: Center X
        // - B9: Center Y
        // - B10: Width
        // - B11: Height
        // - B12: tcs params??
        // - B13: ??
        // - W14: 00 Padding (alias)
        string axaml = ReadAxaml();
        // Every row gets a Label with the WF text in English. Translation
        // happens at the R._() / ViewTranslationHelper layer.
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B0_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_P4_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B8_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B9_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B10_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B11_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B12_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_B13_Label\"", axaml);
        Assert.Contains("AutomationId=\"WorldMapImage_IconData_W14_Label\"", axaml);

        // Specific row-label text (in English; localized later).
        Assert.Contains("Image Sheet Number", axaml);
        Assert.Contains("OAMTable entry", axaml);
        Assert.Contains("Center X", axaml);
        Assert.Contains("Center Y", axaml);
        Assert.Contains("Width", axaml);
        Assert.Contains("Height", axaml);
        Assert.Contains("tcs params??", axaml);
    }

    /// <summary>
    /// Deferred affordances (image IMPORT, dark-map import/export,
    /// decrease-color tool, open-source, select-source, and the two NV5b/NV5c
    /// previews' export: main-field-map + border) must be disabled and
    /// reference KnownGap in the tooltip. No follow-up issues per task scope
    /// discipline.
    ///
    /// NOTE (#843 NV5a): the five reuse-based preview EXPORT buttons
    /// (Event / Mini / Point1 / Point2 / Road) are NO LONGER deferred — they
    /// are working read-only Export PNG buttons gated by CanExport* and are
    /// asserted by <see cref="View_ReuseExportButton_IsBindingGatedReadOnlyExport"/>.
    /// </summary>
    [Theory]
    [InlineData("WorldMapImage_Main_Import_Button")]
    [InlineData("WorldMapImage_Main_Export_Button")]
    [InlineData("WorldMapImage_Main_DarkImport_Button")]
    [InlineData("WorldMapImage_Main_DarkExport_Button")]
    [InlineData("WorldMapImage_Main_DecreaseColor_Button")]
    [InlineData("WorldMapImage_Main_OpenSource_Button")]
    [InlineData("WorldMapImage_Main_SelectSource_Button")]
    [InlineData("WorldMapImage_Event_Import_Button")]
    [InlineData("WorldMapImage_Event_DecreaseColor_Button")]
    [InlineData("WorldMapImage_Mini_Import_Button")]
    [InlineData("WorldMapImage_PointIcon_Point1Import_Button")]
    [InlineData("WorldMapImage_PointIcon_Point2Import_Button")]
    [InlineData("WorldMapImage_PointIcon_RoadImport_Button")]
    [InlineData("WorldMapImage_Border_Import_Button")]
    [InlineData("WorldMapImage_Border_Export_Button")]
    public void View_DeferredButton_IsDisabledAndReferencesKnownGap(string automationId)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");

        int elementStart = axaml.LastIndexOf('<', idx);
        Assert.True(elementStart >= 0);
        int elementEnd = FindElementEnd(axaml, elementStart);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);

        Assert.Contains("IsEnabled=\"False\"", element);
        Assert.Contains("KnownGap", element);
    }

    /// <summary>
    /// #843 NV5a: the five reuse-based preview EXPORT buttons are working
    /// read-only Export PNG buttons — gated by a CanExport* binding (set true
    /// only after a successful render) and wired to an Export click handler.
    /// They must NOT be IsEnabled="False" and must NOT reference KnownGap.
    /// </summary>
    [Theory]
    [InlineData("WorldMapImage_Event_Export_Button", "CanExportEvent", "EventExport_Click")]
    [InlineData("WorldMapImage_Mini_Export_Button", "CanExportMini", "MiniExport_Click")]
    [InlineData("WorldMapImage_PointIcon_Point1Export_Button", "CanExportPoint1", "Point1Export_Click")]
    [InlineData("WorldMapImage_PointIcon_Point2Export_Button", "CanExportPoint2", "Point2Export_Click")]
    [InlineData("WorldMapImage_PointIcon_RoadExport_Button", "CanExportRoad", "RoadExport_Click")]
    public void View_ReuseExportButton_IsBindingGatedReadOnlyExport(
        string automationId, string canExportBinding, string clickHandler)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");

        int elementStart = axaml.LastIndexOf('<', idx);
        Assert.True(elementStart >= 0);
        int elementEnd = FindElementEnd(axaml, elementStart);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);

        // Working read-only export: gated by the CanExport* binding, wired to
        // the export click handler, and NOT a deferred KnownGap stub.
        Assert.Contains($"IsEnabled=\"{{Binding {canExportBinding}}}\"", element);
        Assert.Contains($"Click=\"{clickHandler}\"", element);
        Assert.DoesNotContain("IsEnabled=\"False\"", element);
        Assert.DoesNotContain("KnownGap", element);

        // The click handler exists in the code-behind and routes through the
        // shared GbaImageControl ExportPng helper.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains(clickHandler, source);
    }

    /// <summary>
    /// #843 NV5a: the five reuse-based previews must render via
    /// ImageWorldMapCore (resolve + decode) into a GbaImageControl, and the two
    /// follow-up previews (main-field-map NV5b, border NV5c) must NOT — they
    /// keep their KnownGap Image placeholders. Asserts the five preview controls
    /// are GbaImageControl and the NV5a "live bitmap preview pending Core
    /// extraction" markers are gone for them while the NV5b/NV5c markers remain.
    /// </summary>
    [Fact]
    public void View_ReusePreviews_AreGbaImageControlsAndCoreWired()
    {
        string axaml = ReadAxaml();

        // The five reuse previews are GbaImageControl (not <Image>).
        foreach (string id in new[]
        {
            "WorldMapImage_Event_Preview_Image",
            "WorldMapImage_Mini_Preview_Image",
            "WorldMapImage_PointIcon_Point1Preview_Image",
            "WorldMapImage_PointIcon_Point2Preview_Image",
            "WorldMapImage_PointIcon_RoadPreview_Image",
        })
        {
            int idx = axaml.IndexOf($"AutomationId=\"{id}\"", StringComparison.Ordinal);
            Assert.True(idx >= 0, $"AutomationId {id} not found");
            int elementStart = axaml.LastIndexOf('<', idx);
            string head = axaml.Substring(elementStart, idx - elementStart);
            Assert.Contains("controls:GbaImageControl", head);
        }

        // The main-field-map (NV5b) and border (NV5c) previews stay deferred:
        // still <Image> with the KnownGap live-preview marker.
        foreach (string id in new[]
        {
            "WorldMapImage_Main_Preview_Image",
            "WorldMapImage_Border_DrawSample_Image",
        })
        {
            int idx = axaml.IndexOf($"AutomationId=\"{id}\"", StringComparison.Ordinal);
            Assert.True(idx >= 0, $"AutomationId {id} not found");
            int elementStart = axaml.LastIndexOf('<', idx);
            int elementEnd = FindElementEnd(axaml, elementStart);
            string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);
            Assert.Contains("live bitmap preview pending Core extraction", element);
        }

        // The view renders the reuse previews through the Core helper.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("RefreshPreviews", source);
        string vm = File.ReadAllText(ViewModelPath());
        Assert.Contains("ImageWorldMapCore.TryRenderEvent", vm);
        Assert.Contains("ImageWorldMapCore.TryRenderMini", vm);
        Assert.Contains("ImageWorldMapCore.TryRenderPoint1", vm);
        Assert.Contains("ImageWorldMapCore.TryRenderPoint2", vm);
        Assert.Contains("ImageWorldMapCore.TryRenderRoad", vm);
    }

    // ===================================================================
    // Write handlers must wrap ROM mutation in undo scope.
    // ===================================================================

    [Fact]
    public void View_WriteAllHandler_WrapsInUndoScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // Three distinct undo scopes: WriteAll (the global write button),
        // Border (per-record), Icon (per-record).
        Assert.Contains("_undoService.Begin(\"Write World Map Pointers\")", source);
        Assert.Contains("_undoService.Begin(\"Write World Map Border\")", source);
        Assert.Contains("_undoService.Begin(\"Write World Map Icon\")", source);
        // Commit/Rollback present at least once each.
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_UndoHandler_CallsRunUndo()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("CoreState.Undo?.RunUndo()", source);
    }

    /// <summary>
    /// Per Copilot bot inline review #1-#3 on PR #592: read-only load /
    /// selection paths must wrap their VM SetField cascade in an
    /// IsLoading scope and call MarkClean() in finally so a row
    /// selection doesn't flip the VM IsDirty bit.
    /// </summary>
    [Fact]
    public void View_LoadAndSelectionPaths_UseIsLoadingScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // LoadAll, LoadBorderList, LoadIconList, OnBorderSelected,
        // OnIconSelected must all set IsLoading = true and call
        // MarkClean in finally.
        Assert.Contains("_vm.IsLoading = true", source);
        Assert.Contains("_vm.IsLoading = prevLoading", source);
        Assert.Contains("_vm.MarkClean()", source);
        // The pattern must appear in BOTH selection handlers and the
        // initial-load entry-point.
        int loadingAssignments = CountOccurrences(source, "_vm.IsLoading = true");
        Assert.True(loadingAssignments >= 5,
            $"Expected >= 5 `_vm.IsLoading = true` assignments " +
            $"(LoadAll + LoadBorderList + LoadIconList + OnBorderSelected " +
            $"+ OnIconSelected); got {loadingAssignments}.");
    }

    /// <summary>
    /// Per Copilot bot inline review #4 on PR #592: after successful
    /// writes the view must call <c>_vm.MarkClean()</c> so the VM
    /// IsDirty bit resets (ReadNudsIntoVm + per-tab assignments flip
    /// IsDirty via SetField during the write).
    /// </summary>
    [Fact]
    public void View_WriteHandlers_CallMarkCleanAfterCommit()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // Three distinct undo-scope strings, each followed by Commit
        // then MarkClean.
        string[] scopes =
        {
            "Write World Map Pointers",
            "Write World Map Border",
            "Write World Map Icon",
        };
        foreach (string scope in scopes)
        {
            int idx = source.IndexOf(scope, StringComparison.Ordinal);
            Assert.True(idx > 0, $"Undo scope '{scope}' missing");
            int commitIdx = source.IndexOf("_undoService.Commit()", idx, StringComparison.Ordinal);
            Assert.True(commitIdx > idx, $"_undoService.Commit() not found after scope '{scope}'");
            int markCleanIdx = source.IndexOf("_vm.MarkClean()", commitIdx, StringComparison.Ordinal);
            Assert.True(markCleanIdx > commitIdx,
                $"_vm.MarkClean() must follow _undoService.Commit() for scope '{scope}' " +
                $"(commit at {commitIdx}, MarkClean at {markCleanIdx}).");
            // Sanity: MarkClean should be close to Commit (within 1KB)
            // so we don't accidentally match an unrelated later MarkClean.
            Assert.True(markCleanIdx - commitIdx < 1024,
                $"MarkClean is suspiciously far ({markCleanIdx - commitIdx} chars) " +
                $"from Commit for scope '{scope}' — wrong site?");
        }
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ===================================================================
    // ViewModel synthetic-ROM tests
    // ===================================================================

    [Fact]
    public void ViewModel_LoadAll_PopulatesAllThirteenPointers()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PlantPointer(rom, rom.RomInfo.worldmap_big_image_pointer, 0x111100u);
            PlantPointer(rom, rom.RomInfo.worldmap_big_palette_pointer, 0x222200u);
            PlantPointer(rom, rom.RomInfo.worldmap_big_dpalette_pointer, 0x333300u);
            PlantPointer(rom, rom.RomInfo.worldmap_big_palettemap_pointer, 0x444400u);
            PlantPointer(rom, rom.RomInfo.worldmap_event_image_pointer, 0x555500u);
            PlantPointer(rom, rom.RomInfo.worldmap_event_palette_pointer, 0x666600u);
            PlantPointer(rom, rom.RomInfo.worldmap_event_tsa_pointer, 0x777700u);
            PlantPointer(rom, rom.RomInfo.worldmap_mini_image_pointer, 0x888800u);
            PlantPointer(rom, rom.RomInfo.worldmap_mini_palette_pointer, 0x999900u);
            PlantPointer(rom, rom.RomInfo.worldmap_icon1_pointer, 0xAAAA00u);
            PlantPointer(rom, rom.RomInfo.worldmap_icon2_pointer, 0xBBBB00u);
            PlantPointer(rom, rom.RomInfo.worldmap_road_tile_pointer, 0xCCCC00u);
            PlantPointer(rom, rom.RomInfo.worldmap_icon_palette_pointer, 0xDDDD00u);

            var vm = new WorldMapImageViewModel();
            vm.LoadAll();

            // VM exposes pointers as GBA-encoded uints (matches what the WF
            // NUDs display).
            Assert.Equal(U.toPointer(0x111100u), vm.MainImagePtr);
            Assert.Equal(U.toPointer(0x222200u), vm.MainPalettePtr);
            Assert.Equal(U.toPointer(0x333300u), vm.MainDarkPalettePtr);
            Assert.Equal(U.toPointer(0x444400u), vm.MainPaletteMapPtr);
            Assert.Equal(U.toPointer(0x555500u), vm.EventImagePtr);
            Assert.Equal(U.toPointer(0x666600u), vm.EventPalettePtr);
            Assert.Equal(U.toPointer(0x777700u), vm.EventTsaPtr);
            Assert.Equal(U.toPointer(0x888800u), vm.MiniImagePtr);
            Assert.Equal(U.toPointer(0x999900u), vm.MiniPalettePtr);
            Assert.Equal(U.toPointer(0xAAAA00u), vm.Point1ImagePtr);
            Assert.Equal(U.toPointer(0xBBBB00u), vm.Point2ImagePtr);
            Assert.Equal(U.toPointer(0xCCCC00u), vm.RoadImagePtr);
            Assert.Equal(U.toPointer(0xDDDD00u), vm.IconPalettePtr);
            Assert.True(vm.IsLoaded);
        }
        finally { CoreState.ROM = prev; }
    }

    /// <summary>
    /// Copilot CLI plan-review v1 -&gt; v2 finding C1:
    /// WriteAllPointers MUST persist all 13 slots, not just 4.
    /// </summary>
    [Fact]
    public void ViewModel_WriteAllPointers_PersistsAll13Slots()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapImageViewModel();

            uint p1 = U.toPointer(0x110000u);
            uint p2 = U.toPointer(0x220000u);
            uint p3 = U.toPointer(0x330000u);
            uint p4 = U.toPointer(0x440000u);
            uint p5 = U.toPointer(0x550000u);
            uint p6 = U.toPointer(0x660000u);
            uint p7 = U.toPointer(0x770000u);
            uint p8 = U.toPointer(0x880000u);
            uint p9 = U.toPointer(0x990000u);
            uint p10 = U.toPointer(0xAA0000u);
            uint p11 = U.toPointer(0xBB0000u);
            uint p12 = U.toPointer(0xCC0000u);
            uint p13 = U.toPointer(0xDD0000u);

            vm.MainImagePtr = p1;
            vm.MainPalettePtr = p2;
            vm.MainDarkPalettePtr = p3;
            vm.MainPaletteMapPtr = p4;
            vm.EventImagePtr = p5;
            vm.EventPalettePtr = p6;
            vm.EventTsaPtr = p7;
            vm.MiniImagePtr = p8;
            vm.MiniPalettePtr = p9;
            vm.Point1ImagePtr = p10;
            vm.Point2ImagePtr = p11;
            vm.RoadImagePtr = p12;
            vm.IconPalettePtr = p13;

            bool ok = vm.WriteAllPointers();
            Assert.True(ok);

            // Each of the 13 pointer slots in ROM must reflect the VM value.
            Assert.Equal(p1, rom.u32(rom.RomInfo.worldmap_big_image_pointer));
            Assert.Equal(p2, rom.u32(rom.RomInfo.worldmap_big_palette_pointer));
            Assert.Equal(p3, rom.u32(rom.RomInfo.worldmap_big_dpalette_pointer));
            Assert.Equal(p4, rom.u32(rom.RomInfo.worldmap_big_palettemap_pointer));
            Assert.Equal(p5, rom.u32(rom.RomInfo.worldmap_event_image_pointer));
            Assert.Equal(p6, rom.u32(rom.RomInfo.worldmap_event_palette_pointer));
            Assert.Equal(p7, rom.u32(rom.RomInfo.worldmap_event_tsa_pointer));
            Assert.Equal(p8, rom.u32(rom.RomInfo.worldmap_mini_image_pointer));
            Assert.Equal(p9, rom.u32(rom.RomInfo.worldmap_mini_palette_pointer));
            Assert.Equal(p10, rom.u32(rom.RomInfo.worldmap_icon1_pointer));
            Assert.Equal(p11, rom.u32(rom.RomInfo.worldmap_icon2_pointer));
            Assert.Equal(p12, rom.u32(rom.RomInfo.worldmap_road_tile_pointer));
            Assert.Equal(p13, rom.u32(rom.RomInfo.worldmap_icon_palette_pointer));
        }
        finally { CoreState.ROM = prev; }
    }

    /// <summary>
    /// Copilot CLI plan-review v1 -&gt; v2 finding C2:
    /// Border list terminator MUST use raw u32 + U.isPointer, not p32.
    /// Planting an entry whose raw u32 is NOT a valid pointer must
    /// terminate the scan there.
    /// </summary>
    [Fact]
    public void ViewModel_LoadBorderList_TerminatesOnInvalidEntry()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint tableBase = 0x100000u;
            PlantPointer(rom, rom.RomInfo.worldmap_county_border_pointer, tableBase);

            // Two valid 12-byte rows.
            for (int i = 0; i < 2; i++)
            {
                uint rowOff = tableBase + (uint)(i * 12);
                PlantU32(rom, rowOff + 0, U.toPointer(0x200000u + (uint)i * 0x100u));   // valid pointer
                PlantU32(rom, rowOff + 4, U.toPointer(0x300000u + (uint)i * 0x100u));   // valid pointer
                PlantU32(rom, rowOff + 8, 0x00000000u);                                 // width/height
            }

            // Third row has u32(+0) NOT a valid pointer (0xFFFFFFFF).
            // This must terminate the scan -- the iterator should return
            // exactly 2 entries.
            uint terminator = tableBase + 2u * 12u;
            PlantU32(rom, terminator + 0, 0xFFFFFFFFu);

            var vm = new WorldMapImageViewModel();
            var list = vm.LoadBorderList();
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadIconList_TerminatesOnInvalidEntry()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint tableBase = 0x100000u;
            PlantPointer(rom, rom.RomInfo.worldmap_icon_data_pointer, tableBase);

            // Two valid 16-byte rows.
            for (int i = 0; i < 2; i++)
            {
                uint rowOff = tableBase + (uint)(i * 16);
                PlantU32(rom, rowOff + 0, 0x12345678u);                                 // raw bytes (not a pointer field)
                PlantU32(rom, rowOff + 4, U.toPointer(0x200000u + (uint)i * 0x100u));   // valid pointer (the predicate target)
                PlantU32(rom, rowOff + 8, 0x00000000u);
                PlantU32(rom, rowOff + 12, 0x00000000u);
            }

            // Third row has u32(+4) NOT a valid pointer (0xFFFFFFFF).
            uint terminator = tableBase + 2u * 16u;
            PlantU32(rom, terminator + 4, 0xFFFFFFFFu);

            var vm = new WorldMapImageViewModel();
            var list = vm.LoadIconList();
            Assert.Equal(2, list.Count);
        }
        finally { CoreState.ROM = prev; }
    }

    /// <summary>
    /// Per Copilot bot inline review on PR #592 round 2: LoadBorderList /
    /// LoadIconList must surface BaseAddress + DataCount via VM
    /// properties so the read panel reflects WF InputFormRef semantics.
    /// </summary>
    [Fact]
    public void ViewModel_LoadBorderList_PopulatesReadConfigIndicators()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint tableBase = 0x100000u;
            PlantPointer(rom, rom.RomInfo.worldmap_county_border_pointer, tableBase);

            for (int i = 0; i < 3; i++)
            {
                uint rowOff = tableBase + (uint)(i * 12);
                PlantU32(rom, rowOff + 0, U.toPointer(0x200000u + (uint)i * 0x100u));
                PlantU32(rom, rowOff + 4, U.toPointer(0x300000u + (uint)i * 0x100u));
                PlantU32(rom, rowOff + 8, 0u);
            }
            uint terminator = tableBase + 3u * 12u;
            PlantU32(rom, terminator + 0, 0xFFFFFFFFu);

            var vm = new WorldMapImageViewModel();
            var list = vm.LoadBorderList();
            Assert.Equal(3, list.Count);
            Assert.Equal(U.toPointer(tableBase), vm.BorderReadStartAddress);
            Assert.Equal(3, vm.BorderReadCount);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadIconList_PopulatesReadConfigIndicators()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint tableBase = 0x100000u;
            PlantPointer(rom, rom.RomInfo.worldmap_icon_data_pointer, tableBase);

            for (int i = 0; i < 4; i++)
            {
                uint rowOff = tableBase + (uint)(i * 16);
                PlantU32(rom, rowOff + 4, U.toPointer(0x200000u + (uint)i * 0x100u));
            }
            uint terminator = tableBase + 4u * 16u;
            PlantU32(rom, terminator + 4, 0xFFFFFFFFu);

            var vm = new WorldMapImageViewModel();
            var list = vm.LoadIconList();
            Assert.Equal(4, list.Count);
            Assert.Equal(U.toPointer(tableBase), vm.IconReadStartAddress);
            Assert.Equal(4, vm.IconReadCount);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadBorderList_NullRom_ClearsReadConfigIndicators()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new WorldMapImageViewModel();
            vm.BorderReadStartAddress = 0xDEADBEEFu;
            vm.BorderReadCount = 99;
            var list = vm.LoadBorderList();
            Assert.Empty(list);
            Assert.Equal(0u, vm.BorderReadStartAddress);
            Assert.Equal(0, vm.BorderReadCount);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadBorderEntry_ReadsAllFields()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addr = 0x100000u;
            PlantU32(rom, addr + 0, U.toPointer(0x200000u));
            PlantU32(rom, addr + 4, U.toPointer(0x300000u));
            PlantU32(rom, addr + 8, 0x000a0014u); // u32 little-endian: low half = 0x0014, high half = 0x000a

            var vm = new WorldMapImageViewModel();
            vm.LoadBorderEntry(addr);
            Assert.Equal(U.toPointer(0x200000u), vm.BorderP0);
            Assert.Equal(U.toPointer(0x300000u), vm.BorderP4);
            // W8 is the LOW half of u32 at +8 (X origin); W10 is the HIGH half (Y origin).
            // 0x000a0014 -> low=0x0014 (20)=W8, high=0x000a (10)=W10.
            Assert.Equal(0x0014u, vm.BorderW8);
            Assert.Equal(0x000au, vm.BorderW10);
            Assert.Equal(addr, vm.BorderCurrentAddr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadIconEntry_ReadsAllFields()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addr = 0x100000u;
            // 16-byte record:
            //   +0  byte B0 = 1   (image sheet number)
            //   +1  byte B1 = 0   (00 padding)
            //   +2  byte B2 = 7
            //   +3  byte B3 = 9
            //   +4  u32  P4 = pointer 0x08300000
            //   +8  byte B8 = 0x10  (Center X)
            //   +9  byte B9 = 0x20  (Center Y)
            //   +10 byte B10 = 0x30 (Width)
            //   +11 byte B11 = 0x40 (Height)
            //   +12 byte B12 = 0x50 (tcs)
            //   +13 byte B13 = 0x60 (??)
            //   +14 u16  W14 = 0xBEEF (00 padding alias)
            rom.Data[addr + 0] = 1;
            rom.Data[addr + 1] = 0;
            rom.Data[addr + 2] = 7;
            rom.Data[addr + 3] = 9;
            PlantU32(rom, addr + 4, U.toPointer(0x300000u));
            rom.Data[addr + 8]  = 0x10;
            rom.Data[addr + 9]  = 0x20;
            rom.Data[addr + 10] = 0x30;
            rom.Data[addr + 11] = 0x40;
            rom.Data[addr + 12] = 0x50;
            rom.Data[addr + 13] = 0x60;
            rom.Data[addr + 14] = 0xEF;
            rom.Data[addr + 15] = 0xBE;

            var vm = new WorldMapImageViewModel();
            vm.LoadIconEntry(addr);
            Assert.Equal(1, (int)vm.IconB0);
            Assert.Equal(0, (int)vm.IconB1);
            Assert.Equal(7, (int)vm.IconB2);
            Assert.Equal(9, (int)vm.IconB3);
            Assert.Equal(U.toPointer(0x300000u), vm.IconP4);
            Assert.Equal(0x10, (int)vm.IconB8);
            Assert.Equal(0x20, (int)vm.IconB9);
            Assert.Equal(0x30, (int)vm.IconB10);
            Assert.Equal(0x40, (int)vm.IconB11);
            Assert.Equal(0x50, (int)vm.IconB12);
            Assert.Equal(0x60, (int)vm.IconB13);
            Assert.Equal(0xBEEFu, vm.IconW14);
            Assert.Equal(addr, vm.IconCurrentAddr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_WriteBorder_RoundTripsFields()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addr = 0x100000u;
            var vm = new WorldMapImageViewModel();
            vm.LoadBorderEntry(addr);

            vm.BorderP0 = U.toPointer(0x444444u);
            vm.BorderP4 = U.toPointer(0x555555u);
            vm.BorderW8 = 50u;
            vm.BorderW10 = 30u;
            bool ok = vm.WriteBorder();
            Assert.True(ok);

            vm.LoadBorderEntry(addr);
            Assert.Equal(U.toPointer(0x444444u), vm.BorderP0);
            Assert.Equal(U.toPointer(0x555555u), vm.BorderP4);
            Assert.Equal(50u, vm.BorderW8);
            Assert.Equal(30u, vm.BorderW10);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_WriteIcon_RoundTripsFields()
    {
        var rom = MakeMinimalFE8URom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addr = 0x100000u;
            var vm = new WorldMapImageViewModel();
            vm.LoadIconEntry(addr);

            vm.IconB0 = 0xAA;
            vm.IconB1 = 0xBB;
            vm.IconB2 = 0xCC;
            vm.IconB3 = 0xDD;
            vm.IconP4 = U.toPointer(0x500000u);
            vm.IconB8 = 0x88;
            vm.IconB9 = 0x99;
            vm.IconB10 = 0xAA;
            vm.IconB11 = 0xBB;
            vm.IconB12 = 0xCC;
            vm.IconB13 = 0xDD;
            vm.IconW14 = 0xCAFE;

            bool ok = vm.WriteIcon();
            Assert.True(ok);

            vm.LoadIconEntry(addr);
            Assert.Equal(0xAA, (int)vm.IconB0);
            Assert.Equal(0xBB, (int)vm.IconB1);
            Assert.Equal(0xCC, (int)vm.IconB2);
            Assert.Equal(0xDD, (int)vm.IconB3);
            Assert.Equal(U.toPointer(0x500000u), vm.IconP4);
            Assert.Equal(0x88, (int)vm.IconB8);
            Assert.Equal(0x99, (int)vm.IconB9);
            Assert.Equal(0xAA, (int)vm.IconB10);
            Assert.Equal(0xBB, (int)vm.IconB11);
            Assert.Equal(0xCC, (int)vm.IconB12);
            Assert.Equal(0xDD, (int)vm.IconB13);
            Assert.Equal(0xCAFEu, vm.IconW14);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_WriteAllPointers_ReturnsFalse_OnNullRom()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new WorldMapImageViewModel();
            Assert.False(vm.WriteAllPointers());
            Assert.False(vm.WriteBorder());
            Assert.False(vm.WriteIcon());
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_LoadAll_ReturnsEmptyList_OnNullRom()
    {
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new WorldMapImageViewModel();
            vm.LoadAll();
            Assert.Empty(vm.LoadBorderList());
            Assert.Empty(vm.LoadIconList());
            Assert.False(vm.IsLoaded);
        }
        finally { CoreState.ROM = prev; }
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    static int FindElementEnd(string axaml, int elementStart)
    {
        bool inAttrValue = false;
        for (int i = elementStart; i < axaml.Length; i++)
        {
            char c = axaml[i];
            if (c == '"') inAttrValue = !inAttrValue;
            else if (c == '>' && !inAttrValue) return i;
        }
        return -1;
    }

    static void AssertOccurrences(string haystack, string needle, int expected)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        Assert.True(count == expected,
            $"Expected {expected} occurrences of '{needle}', got {count}");
    }

    /// <summary>Build a 16 MB ROM with FE8U signature.</summary>
    static ROM MakeMinimalFE8URom()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        return rom;
    }

    /// <summary>Plant a u32 (raw bytes — caller pre-encodes the value).</summary>
    static void PlantU32(ROM rom, uint addr, uint value)
    {
        rom.Data[addr + 0] = (byte)(value & 0xFF);
        rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
        rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
        rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>Plant an encoded GBA pointer (0x08...) at the given offset.</summary>
    static void PlantPointer(ROM rom, uint slotAddr, uint targetOffset)
    {
        PlantU32(rom, slotAddr, U.toPointer(targetOffset));
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string AxamlPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "WorldMapImageView.axaml");

    static string ViewCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "WorldMapImageView.axaml.cs");

    static string ViewModelPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "ViewModels", "WorldMapImageViewModel.cs");

    static string FindRepoRoot()
    {
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"Could not locate FEBuilderGBA.sln from {start}");
    }
}
