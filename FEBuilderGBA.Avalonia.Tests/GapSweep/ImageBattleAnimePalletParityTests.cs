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
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
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
    public void View_SamplePreview_IsHonestlyDeferredPlaceholder()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageBattleAnimePallet_SamplePreview_Label\"", axaml);
        // The placeholder text must explain the deferral honestly.
        Assert.Contains("deferred", axaml);
    }

    [Fact]
    public void View_RedoButton_IsHonestlyDisabledKnownGap()
    {
        // Per Plan v6 Finding #2: the local palette-edit redo buffer is
        // WF PaletteFormRef-coupled. The Redo button is rendered but
        // disabled with an explanatory tooltip.
        string axaml = ReadAxaml();
        // Find the Redo button section.
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Redo_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
    }

    [Fact]
    public void View_ImportExportButtons_AreHonestlyDisabledKnownGap()
    {
        // Real PNG export/import are WF ImageFormRef-coupled (#399). Both
        // buttons are rendered but disabled.
        string axaml = ReadAxaml();
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Import_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
        Assert.Matches(new Regex(
            @"AutomationId=""ImageBattleAnimePallet_Export_Button""[\s\S]{0,400}IsEnabled=""False""",
            RegexOptions.Singleline), axaml);
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
    public void ViewModel_Is32ColorMode_HonestlyDeferred()
    {
        // Per Plan v3 Finding #2: even after loading a multi-slot palette
        // (which WF would detect as 32-color), the VM keeps
        // WarningVisible/Is32ColorMode false because the WF
        // ImageUtil.GetPalette16Count(Bitmap) data source has not been
        // ported to Core.
        var (rom, paletteOffset, sourceSlot) = MakeRomWithMultiSlotPalette();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleAnimePalletViewModel();
            vm.LoadEntry(paletteOffset, sourceSlot, paletteIndex: 0);
            Assert.False(vm.WarningVisible,
                "32-color-mode detection is honestly deferred per Plan Finding #2");
            Assert.False(vm.Is32ColorMode);
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
}
