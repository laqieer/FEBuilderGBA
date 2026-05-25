// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5/6 gap-sweep regression tests for MapTileAnimation2View. (#426)
//
// Closes the 46 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on MapTileAnimation2Form (HIGH density 14/40, 20 WF-only labels, 0 common
// labels). Each assertion maps to a concrete acceptance-criterion bullet in
// the issue body and to a Copilot CLI plan-review finding.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the MapTileAnimation2 parity raise (#426) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests
/// mutate CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class MapTileAnimation2ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 40 control instantiations. To leave the
    /// HIGH verdict we need AV >= ceil(40 * 0.75) = 30.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 40;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 30
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_ReloadList_Button\"", axaml);
        Assert.Contains("Click=\"ReloadList_Click\"", axaml);
    }

    [Fact]
    public void View_HasFilterCombo()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_Filter_Combo\"", axaml);
        Assert.Contains("SelectionChanged=\"FilterCombo_SelectionChanged\"", axaml);
    }

    [Fact]
    public void View_HasSelectionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasPaletteSubList()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_NList_List\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_NW0_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_NR_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_NG_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_NB_Input\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_GbaColor_Label\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_NPanel_Label\"", axaml);
    }

    [Fact]
    public void View_HasBulkImportExportButtons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_BulkImport_Button\"", axaml);
        Assert.Contains("AutomationId=\"MapTileAnimation2_BulkExport_Button\"", axaml);
        Assert.Contains("Click=\"BulkImport_Click\"", axaml);
        Assert.Contains("Click=\"BulkExport_Click\"", axaml);
    }

    [Fact]
    public void View_HasNWriteButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_NWrite_Button\"", axaml);
        Assert.Contains("Click=\"NWrite_Click\"", axaml);
    }

    [Fact]
    public void View_HasNListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_NListExpand_Button\"", axaml);
        Assert.Contains("Click=\"NListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"MapTileAnimation2_ListExpand_Button\"", axaml);
        Assert.Contains("Click=\"ListExpand_Click\"", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 2 (#524) inversion - the four deferred affordances
    // (Bulk Import / Bulk Export / List Expand main + palette sub-list)
    // are now wired against the Core helpers in MapTileAnimation2Core
    // (BulkImport / BulkExport / ExpandEntryList / ExpandPaletteRowList).
    // The buttons MUST be rendered enabled (no explicit IsEnabled="False")
    // and the placeholder tooltip referencing the follow-up issue MUST be
    // gone (#524 should no longer appear in the button element). The
    // active tooltips describe the actual behavior.
    // -----------------------------------------------------------------

    /// <summary>
    /// Each of the four affordances is now enabled (no IsEnabled="False"
    /// attribute on the button element) and does NOT contain the #524
    /// follow-up reference (the tooltip describes the live behavior
    /// instead).
    /// </summary>
    [Theory]
    [InlineData("MapTileAnimation2_BulkImport_Button")]
    [InlineData("MapTileAnimation2_BulkExport_Button")]
    [InlineData("MapTileAnimation2_ListExpand_Button")]
    [InlineData("MapTileAnimation2_NListExpand_Button")]
    public void View_BulkAndExpandButton_IsEnabledAndNoFollowupTooltip(string automationId)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");

        // Walk backwards to the previous '<' to find element start.
        int elementStart = axaml.LastIndexOf('<', idx);
        Assert.True(elementStart >= 0, "Could not find element start");
        // Walk forward to the closing '>' so we capture the full element.
        int elementEnd = axaml.IndexOfAny(new[] { '>' }, idx);
        Assert.True(elementEnd > elementStart, "Could not find element end");
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);

        // The button must NOT carry the deferred-state attribute or the
        // follow-up issue reference. (Buttons default to enabled in AXAML
        // when no explicit IsEnabled attribute is set.)
        Assert.DoesNotContain("IsEnabled=\"False\"", element);
        Assert.DoesNotContain("#524", element);
    }

    // -----------------------------------------------------------------
    // Phase 5 - Write/NWrite handlers must wrap ROM mutation in undo scope.
    // -----------------------------------------------------------------

    /// <summary>
    /// Write handler must wrap ROM mutation in <c>_undoService.Begin/Commit</c>.
    /// Roslyn-static read of the code-behind source - no Avalonia head needed.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // The Write_Click + NWrite_Click handlers BOTH must call
        // _undoService.Begin/Commit/Rollback. We assert all three keywords
        // appear in the file.
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_NWriteHandler_ReferencesUndoService()
    {
        // The N_Write handler must be present and wrap in a separate
        // Begin/Commit scope from the main Write handler.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("NWrite_Click", source);
        // Verify the N handler also opens an undo scope: search for
        // "Edit Palette Row" which is the unique scope-name string used.
        Assert.Contains("Edit Palette Row", source);
    }

    // -----------------------------------------------------------------
    // ViewModel state - Phase 1 new fields populated, P0 (pointer-aware)
    // round-trip (Copilot CLI #1).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_FieldDef_UsesPointerForP0()
    {
        // The internal _fields list must contain a P-prefixed entry at
        // offset 0 so EditorFormRef round-trips via rom.write_p32 and not
        // raw rom.write_u32 (gap-sweep #426, Copilot CLI plan-review #1).
        var fields = EditorFormRef.DetectFields(new[] { "P0", "B4", "B5", "B6", "B7" });
        Assert.Equal(EditorFormRef.FieldType.Pointer, fields[0].Type);
        Assert.Equal(0u, fields[0].Offset);
    }

    [Fact]
    public void ViewModel_Write_PersistsPaletteDataPointer_AsGbaPointer()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);

            // Mutate the palette data pointer to a ROM offset (no high bit).
            // Write() must encode it back with the 0x08000000 high bit so
            // the raw u32 read after Write matches.
            uint newOffset = 0x00200000u;
            vm.PaletteDataPointer = newOffset;
            vm.Write();

            uint raw = rom.u32(entryAddr + 0);
            uint decoded = rom.p32(entryAddr + 0);
            Assert.Equal(newOffset | 0x08000000u, raw);
            Assert.Equal(newOffset, decoded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAllFields()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(entryAddr, vm.CurrentAddr);
            // Synthetic ROM plants P0=0x08800100 (offset 0x00800100), wait=0x13,
            // count=4, startindex=0x3C, padding=0.
            Assert.Equal(0x00800100u, vm.PaletteDataPointer);
            Assert.Equal(0x13u, vm.AnimInterval);
            Assert.Equal(4u, vm.DataCount);
            Assert.Equal(0x3Cu, vm.StartPaletteIndex);
            Assert.Equal(0u, vm.Unknown7);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesPaletteSubList()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);

            // Synthetic ROM plants 4 palette rows at 0x00800100.
            Assert.Equal(4, vm.PaletteRows.Count);
            Assert.Equal(0, vm.SelectedPaletteRowIndex);
            // Row 0 is white (0x7FFF) -> (248, 248, 248).
            Assert.Equal((byte)0xF8, vm.PaletteRows[0].R);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_PaletteRow_RgbToGba_RoundTrips()
    {
        var vm = new MapTileAnimation2ViewModel();
        vm.PaletteR = 0xF8;
        vm.PaletteG = 0x10;
        vm.PaletteB = 0x80;
        vm.RecomputeGbaColor();
        Assert.Equal((uint)MapTileAnimation2Core.RgbToGba(0xF8, 0x10, 0x80), vm.PaletteGba);
    }

    [Fact]
    public void ViewModel_WritePaletteRow_PersistsToRom()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);
            // Pick row 1; set RGB to a known unique color (0xF8, 0x10, 0x80
            // = 0x405F GBA).
            vm.SelectedPaletteRowIndex = 1;
            vm.PaletteR = 0xF8;
            vm.PaletteG = 0x10;
            vm.PaletteB = 0x80;
            bool ok = vm.WritePaletteRow();
            Assert.True(ok);

            uint dataOffset = U.toOffset(vm.PaletteDataPointer);
            ushort raw = (ushort)rom.u16(dataOffset + 2 * 1);
            Assert.Equal((ushort)0x405F, raw);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadList_FallsBackToEmpty_WhenNoPlistEntries()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            var items = vm.LoadList();
            Assert.Empty(items);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Regression test for the Copilot CLI inline review on PR #534:
    /// Write_Click must rebuild the palette sub-list when the user changes
    /// the Palette Data Pointer (and/or Data Count) and clicks Write to ROM.
    /// We simulate the view's "set new pointer + Write + LoadEntry" sequence
    /// and assert the palette rows reflect the NEW pointer's bytes, not the
    /// pre-write data.
    /// </summary>
    [Fact]
    public void ViewModel_Write_FollowedByLoadEntry_RefreshesPaletteSubList()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        // Plant a DIFFERENT palette block at 0x00800500 with one color that
        // differs from row 0 of 0x00800100 (which is white 0x7FFF).
        WriteU16(rom.Data, 0x800500 + 0, 0x001F); // red instead of white
        WriteU16(rom.Data, 0x800500 + 2, 0x03E0); // green

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);
            // After LoadEntry, row 0 should be white from 0x00800100.
            Assert.Equal((byte)0xF8, vm.PaletteRows[0].R);
            Assert.Equal((byte)0xF8, vm.PaletteRows[0].G);

            // Now mutate the pointer + count and Write (simulates the view's
            // Write_Click handler before the LoadEntry refresh).
            vm.PaletteDataPointer = 0x00800500u;
            vm.DataCount = 2u;
            vm.Write();

            // The view's Write_Click now reloads the entry so the sub-list
            // reflects the new pointer's data. Without the LoadEntry call,
            // PaletteRows would still hold the old white-at-row-0 values.
            vm.LoadEntry(entryAddr);

            Assert.Equal(2, vm.PaletteRows.Count);
            // Row 0 should now be red (0x001F decoded to 248, 0, 0).
            Assert.Equal((byte)0xF8, vm.PaletteRows[0].R);
            Assert.Equal((byte)0x00, vm.PaletteRows[0].G);
            Assert.Equal((byte)0x00, vm.PaletteRows[0].B);
            // NReadStartAddress should reflect the new pointer's offset.
            Assert.Equal(0x00800500u, vm.NReadStartAddress);
            // NReadCount should mirror DataCount.
            Assert.Equal(2u, vm.NReadCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Roslyn-static check: Write_Click must call LoadEntry + UpdateUI after
    /// Commit so the palette sub-list, N-address bar, color preview, and
    /// SelectedAddress label do not stay stale (Copilot CLI inline review
    /// on PR #534).
    /// </summary>
    [Fact]
    public void View_WriteHandler_ReloadsEntryAfterCommit()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        int writeClick = source.IndexOf("void Write_Click(", StringComparison.Ordinal);
        Assert.True(writeClick >= 0, "Write_Click handler not found");
        int writeBodyEnd = source.IndexOf("void NWrite_Click(", writeClick, StringComparison.Ordinal);
        Assert.True(writeBodyEnd > writeClick, "NWrite_Click should come after Write_Click");
        string writeBody = source.Substring(writeClick, writeBodyEnd - writeClick);
        Assert.Contains("_vm.LoadEntry(", writeBody);
        Assert.Contains("UpdateUI()", writeBody);
    }

    /// <summary>
    /// Regression test for the second Copilot CLI inline review on PR #534:
    /// LoadEntry must CLEAR the N-address bar (NReadStartAddress/NReadCount)
    /// and the R/G/B/GbaColor fields when the entry's palette block is empty
    /// (DataCount==0 or unsafe pointer). Without the clear, switching from
    /// an entry with palette rows to one without would leave the previous
    /// entry's N-address and color values visible in the UI.
    /// </summary>
    [Fact]
    public void ViewModel_LoadEntry_ClearsNStateWhenPaletteEmpty()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        // Plant a second entry at entryAddr+8 with DataCount=0 so LoadEntry
        // produces an empty palette sub-list.
        uint secondEntry = entryAddr + 8;
        WriteU32(rom.Data, (int)secondEntry + 0, 0x08800100u); // P0 valid
        rom.Data[secondEntry + 4] = 0x10;
        rom.Data[secondEntry + 5] = 0; // DataCount = 0 (empty palette)
        rom.Data[secondEntry + 6] = 0x3C;
        rom.Data[secondEntry + 7] = 0;
        // Terminator after the second entry so ScanEntries stops cleanly.
        WriteU32(rom.Data, (int)secondEntry + 8, 0u);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();

            // First, load the entry WITH palette rows.
            vm.LoadEntry(entryAddr);
            Assert.True(vm.PaletteRows.Count > 0);
            uint priorNAddr = vm.NReadStartAddress;
            uint priorNCount = vm.NReadCount;
            Assert.NotEqual(0u, priorNAddr);
            Assert.NotEqual(0u, priorNCount);

            // Now load the entry with DataCount=0 - all N-state must clear.
            vm.LoadEntry(secondEntry);
            Assert.Empty(vm.PaletteRows);
            Assert.Equal(-1, vm.SelectedPaletteRowIndex);
            Assert.Equal(0u, vm.NReadStartAddress);
            Assert.Equal(0u, vm.NReadCount);
            Assert.Equal(0u, vm.PaletteR);
            Assert.Equal(0u, vm.PaletteG);
            Assert.Equal(0u, vm.PaletteB);
            Assert.Equal(0u, vm.PaletteGba);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// WritePaletteRow must normalize the in-memory PaletteR/G/B to the
    /// post-truncation (multiples of 8) values so the UI inputs match the
    /// ROM bytes (Copilot CLI inline review on PR #534).
    /// </summary>
    [Fact]
    public void ViewModel_WritePaletteRow_NormalizesRgbAfterTruncation()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);
            vm.SelectedPaletteRowIndex = 1;
            // Pick a value that is NOT a multiple of 8 - e.g. R=125, G=190,
            // B=49. After truncation to 5 bits + left-shift by 3 these
            // become 120, 184, 48 (each rounded down to the nearest
            // multiple of 8).
            vm.PaletteR = 125;
            vm.PaletteG = 190;
            vm.PaletteB = 49;
            Assert.True(vm.WritePaletteRow());
            // After WritePaletteRow, R/G/B should be normalized.
            Assert.Equal(120u, vm.PaletteR);
            Assert.Equal(184u, vm.PaletteG);
            Assert.Equal(48u, vm.PaletteB);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Phase 2 (#524) - ViewModel BulkExport / BulkImport / Expand
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_BulkExport_WritesFile()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        string tmp = Path.GetTempFileName();
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            // BuildList walks the entry table from entryAddr.
            vm.BuildList(entryAddr);
            string err = vm.BulkExport(tmp);
            Assert.Equal("", err);
            string[] lines = File.ReadAllLines(tmp);
            Assert.True(lines.Length >= 2, "Export must produce header + at least one row");
            Assert.StartsWith("//wait", lines[0]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ViewModel_BulkImport_RoundTrips()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        string tmp = Path.GetTempFileName();
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.BuildList(entryAddr);

            // Export and re-import via the VM (round-trip parity).
            Assert.Equal("", vm.BulkExport(tmp));

            // PointerSlot = PLIST table slot for plist=1 (planted at
            // 0x900000 + 1*4 by MakeMinimalFE8URomWithEntry).
            uint plistTableBase = rom.p32(rom.RomInfo.map_tileanime2_pointer);
            uint pointerSlot = plistTableBase + 1 * 4;

            // Open ambient undo so the import path can record into it.
            var undo = new Undo.UndoData();
            undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
            undo.filesize = (uint)rom.Data.Length;
            using (ROM.BeginUndoScope(undo))
            {
                string err = vm.BulkImport(tmp, pointerSlot);
                Assert.Equal("", err);
            }

            // The pointer now resolves to a fresh table; that table's row 0
            // must have the same WAIT/COUNT/STARTINDEX as the original
            // (wait=0x13, count=4, startindex=0x3C).
            uint newBase = rom.p32(pointerSlot);
            Assert.NotEqual(0u, newBase);
            Assert.Equal(0x13u, rom.u8(newBase + 4));
            Assert.Equal(0x04u, rom.u8(newBase + 5));
            Assert.Equal(0x3Cu, rom.u8(newBase + 6));
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ViewModel_ExpandEntryList_GrowsTable()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.BuildList(entryAddr);
            uint oldCount = vm.ReadCount;
            Assert.True(oldCount > 0);

            // PLIST table slot for plist=1.
            uint plistTableBase = rom.p32(rom.RomInfo.map_tileanime2_pointer);
            uint pointerSlot = plistTableBase + 1 * 4;

            uint newBase;
            var undo = new Undo.UndoData();
            undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
            undo.filesize = (uint)rom.Data.Length;
            using (ROM.BeginUndoScope(undo))
            {
                newBase = vm.ExpandEntryListByOne(pointerSlot);
            }
            Assert.NotEqual(U.NOT_FOUND, newBase);
            // ScanEntries returns oldCount + 1 (row-0 template clone of the
            // first entry preserves isPointer(P0)).
            var entries = MapTileAnimation2Core.ScanEntries(rom, newBase, maxRows: 16);
            Assert.Equal((int)(oldCount + 1), entries.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_ExpandPaletteRowList_GrowsBlock()
    {
        var rom = MakeMinimalFE8URomWithEntry(out uint entryAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTileAnimation2ViewModel();
            vm.LoadEntry(entryAddr);
            uint oldCount = vm.DataCount;
            Assert.True(oldCount > 0);

            uint newBase;
            var undo = new Undo.UndoData();
            undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
            undo.filesize = (uint)rom.Data.Length;
            using (ROM.BeginUndoScope(undo))
            {
                newBase = vm.ExpandPaletteRowListByOne();
            }
            Assert.NotEqual(U.NOT_FOUND, newBase);
            // The first oldCount colors are copied; the new row(s) are row-0
            // template clones (per V1 plan-review #2 - WF parity, NOT zero).
            ushort row0 = (ushort)rom.u16(newBase + 0);
            ushort newRow = (ushort)rom.u16(newBase + (uint)(oldCount * 2));
            Assert.Equal(row0, newRow);
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
            "MapTileAnimation2View.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapTileAnimation2View.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with:
    /// - map_setting_pointer -> 0x08800000 (map block at 0x00800000),
    ///   one map entry with anime2_plist=1, terminator at index 1.
    /// - map_tileanime2_pointer -> 0x08900000 (PLIST table at 0x00900000).
    /// - PLIST table entry for plist=1 -> 0x08800200 (entry block at
    ///   0x00800200), one 8-byte entry pointing at palette data at
    ///   0x00800100 with wait=0x13, count=4, startindex=0x3C.
    /// - Palette data at 0x00800100: 4 u16 GBA colors (white, red, green,
    ///   blue) so palette sub-list has 4 rows.
    /// Returns the entry address (0x00800200) so the test can call
    /// LoadEntry() directly without going through the filter chain.
    /// </summary>
    static ROM MakeMinimalFE8URomWithEntry(out uint entryAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        // Map block at 0x00800000: D0=pointer, anime2_plist=1 at +10.
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08800000u);
        WriteU32(rom.Data, 0x800000, 0x08800200u); // D0 = pointer (valid)
        rom.Data[0x800000 + 10] = 1; // anime2_plist = 1
        // Map[1]: terminator (D0 = 0)
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)(0x800000 + dataSize), 0u);

        // PLIST table at 0x00900000. Slot 1 -> 0x08800200.
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer, 0x08900000u);
        WriteU32(rom.Data, 0x900000 + 1 * 4, 0x08800200u);

        // Entry block at 0x00800200: P0=0x08800100, wait=0x13, count=4,
        // startindex=0x3C, padding=0.
        entryAddr = 0x800200;
        WriteU32(rom.Data, (int)entryAddr + 0, 0x08800100u);
        rom.Data[entryAddr + 4] = 0x13;
        rom.Data[entryAddr + 5] = 4;
        rom.Data[entryAddr + 6] = 0x3C;
        rom.Data[entryAddr + 7] = 0;
        // Entry[1] = zero P0 so ScanEntries stops cleanly.
        WriteU32(rom.Data, (int)entryAddr + 8, 0u);

        // Palette data at 0x00800100: 4 u16 GBA colors.
        WriteU16(rom.Data, 0x800100 + 0, 0x7FFF); // white
        WriteU16(rom.Data, 0x800100 + 2, 0x001F); // red
        WriteU16(rom.Data, 0x800100 + 4, 0x03E0); // green
        WriteU16(rom.Data, 0x800100 + 6, 0x7C00); // blue

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
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
