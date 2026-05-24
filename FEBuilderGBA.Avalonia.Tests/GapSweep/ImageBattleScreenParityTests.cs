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
        Assert.Contains("AutomationId=\"ImageBattleScreen_TSAInfo_Label\"", axaml);
    }

    [Fact]
    public void View_HasBattlePreviewKnownGapPlaceholder()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_BattlePreview_Label\"", axaml);
        // The placeholder text must explain the deferral honestly.
        Assert.Contains("deferred", axaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void View_HasChipsetPreviewKnownGapPlaceholder()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleScreen_ChipsetPreview_Label\"", axaml);
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
        Assert.Contains("AutomationId=\"ImageBattleScreen_Image1_Preview_Label\"", axaml);
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
    /// Per Copilot bot PR #594 review round 2 #5: ZoomCombo only affects
    /// the deferred Battle/Chipset preview rendering. Until that rendering
    /// lands, the combo is disabled with an explanatory tooltip.
    /// </summary>
    [Fact]
    public void View_ZoomCombo_IsHonestlyDeferredKnownGap()
    {
        string axaml = ReadAxaml();
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleScreen_Zoom_Combo""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
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

    [Fact]
    public void View_PerImageImportExport_IsHonestlyDeferredKnownGap()
    {
        // Per-image Import/Export buttons (image1..5) are also WF-coupled.
        string axaml = ReadAxaml();
        for (int i = 1; i <= 5; i++)
        {
            Assert.Matches(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Import_Button""[\s\S]{{0,400}}IsEnabled=""False""",
                RegexOptions.Singleline), axaml);
            Assert.Matches(new Regex(
                $@"AutomationId=""ImageBattleScreen_Image{i}_Export_Button""[\s\S]{{0,400}}IsEnabled=""False""",
                RegexOptions.Singleline), axaml);
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
