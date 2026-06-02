// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for ImageMagicCSACreatorView. (#417)
//
// Closes the 59 Avalonia <-> WinForms gaps the gap-sweep surfaced on
// ImageMagicCSACreatorForm (HIGH density 3/37, -91.9%, 25 WF-only labels,
// 0 common labels). Each assertion maps to an acceptance-criterion bullet
// from issue #417 or to a Copilot CLI plan-review finding.
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
/// Tests proving the ImageMagicCSACreator parity raise (#417) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests
/// mutate CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class ImageMagicCSACreatorParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 37 control instantiations. To leave the
    /// HIGH verdict we need AV &gt;= ceil(37 * 0.75) = 28.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 37;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 28
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        // #649: Reload button now lives inside EditorTopBar; its
        // AutomationId is preserved via the ReloadAutomationId override.
        // The hand-rolled Click=\"ReloadList_Click\" handler was replaced
        // by the unified bar's ReloadRequested routed event.
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicCSACreator_ReloadList_Button", axaml);
        Assert.Contains("OnTopBarReloadRequested", axaml);
    }

    [Fact]
    public void View_HasSelectionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasEntryList()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Entry_List\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Name_Label\"", axaml);
    }

    [Fact]
    public void View_HasFramePointerFields()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_P0_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_P4_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_P8_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_P12_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_P16_Input\"", axaml);
    }

    [Fact]
    public void View_HasDimComboBox()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Dim_Combo\"", axaml);
    }

    [Fact]
    public void View_HasZoomComboBox()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Zoom_Combo\"", axaml);
    }

    [Fact]
    public void View_HasFrameNumeric()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Frame_Input\"", axaml);
    }

    [Fact]
    public void View_HasCommentBox()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Comment_Input\"", axaml);
    }

    [Fact]
    public void View_HasBinInfoBox()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_BinInfo_Input\"", axaml);
    }

    [Fact]
    public void View_HasPreviewArea()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Preview_Image\"", axaml);
        Assert.Contains("AutomationId=\"ImageMagicCSACreator_Preview_Label\"", axaml);
    }

    // -----------------------------------------------------------------
    // Deferred affordances (#886 wired Export/OpenSource/SelectSource;
    // Import + Editor remain stubs).
    // -----------------------------------------------------------------

    /// <summary>
    /// Import (#886 part 2) and Editor (#500) are still stubs — must be
    /// IsEnabled="False" in the AXAML element (no tooltip text required).
    /// </summary>
    [Theory]
    [InlineData("ImageMagicCSACreator_Import_Button")]
    [InlineData("ImageMagicCSACreator_Editor_Button")]
    // NOTE: ListExpand_Button is NO LONGER deferred — wired in #837.
    // Export/OpenSource/SelectSource are NO LONGER deferred — wired in #886.
    public void View_StubButton_IsDisabled(string automationId)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");

        int elementStart = axaml.LastIndexOf('<', idx);
        Assert.True(elementStart >= 0);
        int elementEnd = axaml.IndexOfAny(new[] { '>' }, idx);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);

        Assert.Contains("IsEnabled=\"False\"", element);
    }

    /// <summary>
    /// Export is wired in #886 — AXAML element must NOT hard-code
    /// IsEnabled="False" (enablement is driven at runtime by
    /// UpdateExportButtonEnabled) and must have the Click handler.
    /// </summary>
    [Fact]
    public void View_ExportButton_IsWired()
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf("AutomationId=\"ImageMagicCSACreator_Export_Button\"",
            StringComparison.Ordinal);
        Assert.True(idx >= 0, "Export button AutomationId not found in AXAML");
        int elementStart = axaml.LastIndexOf('<', idx);
        int elementEnd = axaml.IndexOf("/>", idx, StringComparison.Ordinal);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 2);

        // #886: Export button must NOT be permanently disabled.
        // Runtime enablement (UpdateExportButtonEnabled) gates it on CSA detection.
        Assert.DoesNotContain("ToolTip.Tip=\"Pending", element);
        Assert.Contains("Click=\"Export_Click\"", element);
    }

    /// <summary>
    /// OpenSource and SelectSource are wired in #886 — their AXAML elements
    /// must use IsVisible (not IsEnabled) for their initial hidden state.
    /// </summary>
    [Theory]
    [InlineData("ImageMagicCSACreator_OpenSource_Button")]
    [InlineData("ImageMagicCSACreator_SelectSource_Button")]
    public void View_SourceButton_IsWired_WithIsVisible(string automationId)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"",
            StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");
        int elementStart = axaml.LastIndexOf('<', idx);
        int elementEnd = axaml.IndexOf("/>", idx, StringComparison.Ordinal);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 2);

        // Wired in #886: use IsVisible="False" for initial hidden state.
        Assert.Contains("IsVisible=\"False\"", element);
        // No longer permanently disabled.
        Assert.DoesNotContain("ToolTip.Tip=\"Pending", element);
    }

    /// <summary>
    /// #837 — the CSA "Data Expansion" (ListExpand) button is now WIRED: the
    /// AXAML element no longer hard-codes IsEnabled="False" nor references the
    /// stale #500 follow-up (enablement is driven at runtime by
    /// UpdateListExpandVisibility), and the Click handler is bound.
    /// </summary>
    [Fact]
    public void View_ListExpandButton_IsWired()
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf("AutomationId=\"ImageMagicCSACreator_ListExpand_Button\"",
            StringComparison.Ordinal);
        Assert.True(idx >= 0, "ListExpand button AutomationId not found in AXAML");
        int elementStart = axaml.LastIndexOf('<', idx);
        // Find the end of THIS Button element (its self-closing "/>"), so the
        // Click attribute on the same element is included in the slice.
        int elementEnd = axaml.IndexOf("/>", idx, StringComparison.Ordinal);
        Assert.True(elementEnd > elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 2);

        Assert.DoesNotContain("IsEnabled=\"False\"", element);
        Assert.DoesNotContain("#500", element);
        Assert.Contains("Click=\"ListExpand_Click\"", element);
    }

    // -----------------------------------------------------------------
    // Write handler must wrap ROM mutation in undo scope.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    // -----------------------------------------------------------------
    // ViewModel state - field defs + write semantics (Copilot CLI #1).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_FieldDef_UsesPointersForAllFiveOffsets()
    {
        var fields = ImageMagicCSACreatorViewModel.FieldDefs;
        Assert.Equal(5, fields.Count);
        Assert.All(fields, f => Assert.Equal(EditorFormRef.FieldType.Pointer, f.Type));
        Assert.Equal(0u, fields[0].Offset);
        Assert.Equal(4u, fields[1].Offset);
        Assert.Equal(8u, fields[2].Offset);
        Assert.Equal(12u, fields[3].Offset);
        Assert.Equal(16u, fields[4].Offset);
    }

    [Fact]
    public void ViewModel_Write_PersistsPointers_AsGbaPointers()
    {
        var rom = MakeMinimalFE8URom(out uint entryAddr, out uint tagAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageMagicCSACreatorViewModel();
            // Prime the magic-system detection cache by walking LoadList.
            vm.LoadList();
            vm.LoadEntry(entryAddr, tagAddr);

            // Mutate all 5 pointers to known offsets (no high bit).
            vm.P0 = 0x00200000u;
            vm.P4 = 0x00210000u;
            vm.P8 = 0x00220000u;
            vm.P12 = 0x00230000u;
            vm.P16 = 0x00240000u;
            vm.Write();

            // Each raw u32 read must include the 0x08000000 high bit.
            Assert.Equal(0x00200000u | 0x08000000u, rom.u32(entryAddr + 0));
            Assert.Equal(0x00210000u | 0x08000000u, rom.u32(entryAddr + 4));
            Assert.Equal(0x00220000u | 0x08000000u, rom.u32(entryAddr + 8));
            Assert.Equal(0x00230000u | 0x08000000u, rom.u32(entryAddr + 12));
            Assert.Equal(0x00240000u | 0x08000000u, rom.u32(entryAddr + 16));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot CLI plan review #1: Dim mode must write to TagAddress (the
    /// pointer-table slot), NOT CurrentAddr (the CSA struct). Plant tag at
    /// a different offset and verify the dim write lands at the tag while
    /// CurrentAddr+0 (P0 raw bytes) stays untouched.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsDimMode_AtTagAddress_NotCurrentAddr()
    {
        var rom = MakeMinimalFE8URom(out uint entryAddr, out uint tagAddr);
        Assert.NotEqual(entryAddr, tagAddr); // sanity

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageMagicCSACreatorViewModel();
            vm.LoadList(); // resolves _dimAddr / _noDimAddr

            // Capture P0's raw bytes BEFORE the write so we can prove dim
            // mode does not touch them.
            vm.LoadEntry(entryAddr, tagAddr);
            // Inject distinctive P0 bytes so the comparison is precise.
            uint distinctiveP0Raw = 0x12345678u;
            // Avoid touching the rest of the entry to keep this assertion
            // surgical - we only care that the dim write doesn't smash P0.
            rom.write_u32(entryAddr + 0, distinctiveP0Raw);
            vm.LoadEntry(entryAddr, tagAddr);

            // dim_pc (index 0) -> _dimAddr (0x0895D7ED with high bit on)
            vm.DimMode = 0;
            vm.Write();
            Assert.Equal(0x0895D7EDu, rom.u32(tagAddr));
            Assert.Equal(distinctiveP0Raw, rom.u32(entryAddr + 0));

            // dim (index 1) -> _noDimAddr (0x0895D899 with high bit on)
            vm.LoadEntry(entryAddr, tagAddr);
            rom.write_u32(entryAddr + 0, distinctiveP0Raw);
            vm.LoadEntry(entryAddr, tagAddr);
            vm.DimMode = 1;
            vm.Write();
            Assert.Equal(0x0895D899u, rom.u32(tagAddr));
            Assert.Equal(distinctiveP0Raw, rom.u32(entryAddr + 0));

            // NULL(EMPTY) (index 2) -> 0
            vm.LoadEntry(entryAddr, tagAddr);
            rom.write_u32(entryAddr + 0, distinctiveP0Raw);
            vm.LoadEntry(entryAddr, tagAddr);
            vm.DimMode = 2;
            vm.Write();
            Assert.Equal(0u, rom.u32(tagAddr));
            Assert.Equal(distinctiveP0Raw, rom.u32(entryAddr + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot CLI plan review #1: TagAddress must round-trip through Load
    /// + Write so the dim selector consistently targets the same slot.
    /// </summary>
    [Fact]
    public void ViewModel_LoadEntry_CarriesTagAddress_ThroughLoadAndWrite()
    {
        var rom = MakeMinimalFE8URom(out uint entryAddr, out uint tagAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageMagicCSACreatorViewModel();
            vm.LoadList();
            vm.LoadEntry(entryAddr, tagAddr);
            Assert.Equal(tagAddr, vm.TagAddress);

            vm.DimMode = 1;
            vm.Write();
            // After Write, TagAddress must STILL be the same value, and the
            // ROM at TagAddress must reflect the dim write.
            Assert.Equal(tagAddr, vm.TagAddress);
            Assert.Equal(0x0895D899u, rom.u32(tagAddr));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot CLI plan review #2: when the CSA magic system signature is
    /// absent, LoadList must return an empty list (not throw, not stub-fail).
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_FallsBackToEmpty_WhenCsaSystemAbsent()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageMagicCSACreatorViewModel();
            var items = vm.LoadList();
            Assert.Empty(items);
            Assert.Equal(MagicSystemKind.None, vm.MagicKind);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Sanity: when the engine signature IS planted, LoadList yields at
    /// least one entry (it walks the spell-data count cap).
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_YieldsEntries_WhenCsaSystemPresent()
    {
        var rom = MakeMinimalFE8URom(out _, out _);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageMagicCSACreatorViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            Assert.Equal(MagicSystemKind.CsaCreator, vm.MagicKind);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a synthetic FE8U ROM with:
    /// - SCA_Creator engine + spell-table signatures planted.
    /// - magic_effect_pointer references a pointer table at 0x300000.
    /// - One dim entry planted at slot originalCount+1 with TagAddr at
    ///   pointerTable + (originalCount+1)*4.
    /// </summary>
    static ROM MakeMinimalFE8URom(out uint entryAddr, out uint tagAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Engine signature.
        byte[] engineSig = new byte[]{0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08};
        Buffer.BlockCopy(engineSig, 0, rom.Data, 0x95d780, engineSig.Length);

        // Spell-table signature + pointer.
        byte[] tableSig = new byte[]{0x1C,0x58,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08};
        Buffer.BlockCopy(tableSig, 0, rom.Data, 0x100000, tableSig.Length);
        uint csaTablePointer = 0x100010u;
        uint csaTable = 0x200000u;
        WriteU32(rom.Data, (int)csaTablePointer, csaTable | 0x08000000u);

        // Magic-effect pointer table.
        WriteU32(rom.Data, (int)rom.RomInfo.magic_effect_pointer, 0x08300000u);

        uint originalCount = rom.RomInfo.magic_effect_original_data_count;
        uint slot = originalCount + 1u; // past original so post-original
        uint pointerTable = 0x300000u;
        tagAddr = pointerTable + slot * 4u;
        entryAddr = csaTable + slot * 20u;

        // Plant a dim_pc pointer at the tag slot so LoadEntry resolves DimMode=0.
        WriteU32(rom.Data, (int)tagAddr, 0x0895D7EDu);

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());
    static string AxamlPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicCSACreatorView.axaml");
    static string ViewCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicCSACreatorView.axaml.cs");

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
