// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/5 gap-sweep regression tests for ImagePalletView. (#400)
//
// Closes the 123 Avalonia <-> WinForms gaps the gap-sweep surfaced on
// ImagePalletForm (HIGH density 3/98, -96.9%, 28 WF-only labels,
// 0 common labels). Each assertion maps to an acceptance-criterion
// bullet from issue #400 or to a Copilot CLI plan-review finding.

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
/// Tests proving the ImagePalletView parity raise (#400) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests
/// mutate CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class ImagePalletParityTests
{
    // ===================================================================
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // ===================================================================

    /// <summary>
    /// WF designer.cs reports 98 control instantiations. To leave the
    /// HIGH verdict we need AV &gt;= ceil(98 * 0.75) = 74.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 98;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 74
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // ===================================================================
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // ===================================================================

    [Fact]
    public void View_HasTopSelectionBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImagePallet_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Address_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Zoom_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Zoom_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasPaletteIndexBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImagePallet_PaletteIndex_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_PaletteIndex_Label\"", axaml);
    }

    [Fact]
    public void View_HasSixteenColorCells()
    {
        // AutomationIds use the {Editor}_{Field}{N}_{Type} pattern so the
        // AutomationIdTests.All_AutomationIds_Follow_Naming_Convention check
        // recognises each suffix (Image for swatches, Input for NUDs).
        string axaml = ReadAxaml();
        for (int n = 1; n <= 16; n++)
        {
            Assert.Contains($"AutomationId=\"ImagePallet_P{n}_Image\"", axaml);
            Assert.Contains($"AutomationId=\"ImagePallet_R{n}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImagePallet_G{n}_Input\"", axaml);
            Assert.Contains($"AutomationId=\"ImagePallet_B{n}_Input\"", axaml);
        }
    }

    [Fact]
    public void View_HasNumericLabels_1Through16()
    {
        string axaml = ReadAxaml();
        for (int n = 1; n <= 16; n++)
            Assert.Contains($"AutomationId=\"ImagePallet_Index{n}_Label\"", axaml);
    }

    [Fact]
    public void View_HasRGBRowLabels()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImagePallet_R_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_G_Label\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_B_Label\"", axaml);
    }

    [Fact]
    public void View_HasImagePreview()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImagePallet_Preview_Image\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Preview_Label\"", axaml);
    }

    [Fact]
    public void View_HasButtonRow()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImagePallet_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Clipboard_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Undo_Button\"", axaml);
        Assert.Contains("AutomationId=\"ImagePallet_Redo_Button\"", axaml);
    }

    // ===================================================================
    // #994: Redo button is now wired to CoreState.Undo.RunRedo()
    // ===================================================================

    /// <summary>
    /// #994: the Redo button is now wired (not disabled) — the stale "#500 Core
    /// has no RunRedo() API" blocker was removed. The button must carry
    /// Click="Redo_Click" and must NOT contain IsEnabled="False".
    /// </summary>
    [Fact]
    public void View_RedoButton_IsWiredAndEnabled()
    {
        string axaml = ReadAxaml();
        var rx = new System.Text.RegularExpressions.Regex(
            "AutomationId=\"ImagePallet_Redo_Button\"[\\s\\S]*?/>",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        System.Text.RegularExpressions.Match m = rx.Match(axaml);
        Assert.True(m.Success, "Redo button tag not found");
        Assert.Contains("Click=\"Redo_Click\"", m.Value);
        Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
    }

    /// <summary>
    /// #994: the Redo_Click handler must call CoreState.Undo.RunRedo() and
    /// guard on CanRedo. CanRedo must appear textually before RunRedo()
    /// within the Redo_Click method body.
    /// The stale "Core has no RunRedo" string must be gone.
    /// </summary>
    [Fact]
    public void View_RedoHandler_CallsRunRedo()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // Verify both calls exist in the Redo_Click method.
        Assert.Matches(new System.Text.RegularExpressions.Regex(
            @"void\s+Redo_Click[\s\S]*?CoreState\.Undo\.CanRedo",
            System.Text.RegularExpressions.RegexOptions.Compiled), source);
        Assert.Matches(new System.Text.RegularExpressions.Regex(
            @"void\s+Redo_Click[\s\S]*?CoreState\.Undo\.RunRedo\(\)",
            System.Text.RegularExpressions.RegexOptions.Compiled), source);
        // Extract only the Redo_Click method body to check ordering.
        // Find the method start, then the matching close brace.
        int methodStart = source.IndexOf("void Redo_Click(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Redo_Click method not found");
        // Find the first '!CoreState.Undo.CanRedo' after the method start.
        int idxCanRedo = source.IndexOf("CoreState.Undo.CanRedo", methodStart, StringComparison.Ordinal);
        int idxRunRedo = source.IndexOf("CoreState.Undo.RunRedo()", methodStart, StringComparison.Ordinal);
        Assert.True(idxCanRedo >= 0 && idxRunRedo >= 0, "CanRedo and RunRedo must both be present");
        Assert.True(idxCanRedo < idxRunRedo, "CanRedo must appear before RunRedo()");
        // Stale strings must be gone.
        Assert.DoesNotContain("Core has no RunRedo", source);
    }

    /// <summary>
    /// Import / Export / Clipboard buttons were wired to the Core palette
    /// helpers in #777 and must no longer carry the `IsEnabled="False"`
    /// deferral attribute. Their click handlers stay wired.
    /// </summary>
    [Theory]
    [InlineData("ImagePallet_Import_Button", "Import_Click")]
    [InlineData("ImagePallet_Export_Button", "Export_Click")]
    [InlineData("ImagePallet_Clipboard_Button", "Clipboard_Click")]
    public void View_PaletteIoButtons_AreEnabled(string automationId, string clickHandler)
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AutomationId {automationId} not found in AXAML");
        int elementStart = axaml.LastIndexOf('<', idx);
        int elementEnd = FindElementEnd(axaml, elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);

        Assert.DoesNotContain("IsEnabled=\"False\"", element);
        Assert.DoesNotContain("#500", element);
        Assert.Contains($"Click=\"{clickHandler}\"", element);
    }

    /// <summary>
    /// Undo button MUST be enabled (Copilot CLI plan review #5). Check
    /// by verifying the explicit `IsEnabled="False"` attribute is
    /// absent from the Undo button's element.
    /// </summary>
    [Fact]
    public void View_UndoButton_IsEnabled()
    {
        string axaml = ReadAxaml();
        int idx = axaml.IndexOf("AutomationId=\"ImagePallet_Undo_Button\"", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        int elementStart = axaml.LastIndexOf('<', idx);
        int elementEnd = FindElementEnd(axaml, elementStart);
        string element = axaml.Substring(elementStart, elementEnd - elementStart + 1);
        Assert.DoesNotContain("IsEnabled=\"False\"", element);
        Assert.Contains("Click=\"Undo_Click\"", element);
    }

    // ===================================================================
    // Write handler must wrap ROM mutation in undo scope.
    // ===================================================================

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    /// <summary>
    /// Undo button click handler must wire CoreState.Undo.RunUndo()
    /// (Copilot CLI plan review #5).
    /// </summary>
    [Fact]
    public void View_UndoHandler_CallsRunUndo()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("CoreState.Undo?.RunUndo()", source);
    }

    /// <summary>
    /// Write_Click must honor _vm.Write()'s U.NOT_FOUND return value
    /// (Copilot CLI round-2 review on PR #586). When PaletteCore.Write
    /// no-ops (invalid address / overflow), the handler must rollback,
    /// surface an error, and NOT show the success toast.
    /// </summary>
    [Fact]
    public void View_WriteHandler_HonorsNoOpReturn()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // Capture the return value of _vm.Write() into a uint variable.
        Assert.Contains("uint writtenOffset = _vm.Write()", source);
        // Branch on U.NOT_FOUND and rollback rather than commit.
        Assert.Contains("writtenOffset == U.NOT_FOUND", source);
        // Error toast on no-op (uses the new "Invalid palette address" key).
        Assert.Contains("Invalid palette address", source);
    }

    /// <summary>
    /// View must wire ValueChanged handlers on all 48 R/G/B NUDs so
    /// edits refresh the swatch immediately (Copilot bot round-3
    /// inline review #1 on PR #586).
    /// </summary>
    [Fact]
    public void View_NudChangeHandlers_RefreshSwatchesLive()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // The wire-up registers Nud_ValueChanged on every NUD.
        Assert.Contains("WireNudChangeHandlers", source);
        Assert.Contains("n.ValueChanged += Nud_ValueChanged", source);
        // The handler refreshes swatches from the NUD values (NOT VM).
        Assert.Contains("RefreshSwatchesFromNuds()", source);
        Assert.Contains("NudByte(", source);
    }

    /// <summary>
    /// View must cache the palette-name overrides passed via JumpTo
    /// and pass them back into LoadEntry on every reload path (Write,
    /// Undo) so the Palette Index combo labels survive (Copilot bot
    /// round-3 inline reviews #2 + #3 on PR #586).
    /// </summary>
    [Fact]
    public void View_ReloadPaths_PreservePaletteNames()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        // Field caching the names.
        Assert.Contains("_lastPaletteNames", source);
        // JumpTo writes to the cache.
        Assert.Contains("_lastPaletteNames = paletteNames", source);
        // Both reload sites (Write + Undo) pass the cached names back.
        // We assert there is NO LoadEntry(..., null) call after the
        // JumpTo flow - all reloads use _lastPaletteNames.
        Assert.DoesNotContain("LoadEntry(reloadAddr, reloadMax, reloadIdx, null)", source);
        Assert.DoesNotContain("LoadEntry(addr, max, idx, null)", source);
        Assert.Contains("LoadEntry(reloadAddr, reloadMax, reloadIdx, _lastPaletteNames)", source);
        Assert.Contains("LoadEntry(addr, max, idx, _lastPaletteNames)", source);
    }

    /// <summary>
    /// ViewModel.Write() must return U.NOT_FOUND on a sentinel
    /// PaletteAddress (Copilot CLI round-2 review on PR #586).
    /// </summary>
    [Fact]
    public void ViewModel_Write_ReturnsNotFound_OnInvalidAddress()
    {
        var rom = MakeMinimalRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImagePalletViewModel();
            // Plant a valid address first via LoadEntry, then mutate
            // PaletteAddress to U.NOT_FOUND so the IsLoaded guard does
            // not short-circuit.
            vm.LoadEntry(0x100000u, 1, 0, null);
            vm.PaletteAddress = U.NOT_FOUND;
            uint result = vm.Write();
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ===================================================================
    // ViewModel state - round-trip semantics + JumpTo wiring.
    // ===================================================================

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAll48Properties()
    {
        var rom = MakeMinimalRomWithPaletteAt(0x100000u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImagePalletViewModel();
            vm.LoadEntry(0x100000u, 1, 0, null);

            // Slot 0 was planted as white (R=248, G=248, B=248).
            Assert.Equal(248, vm.R1);
            Assert.Equal(248, vm.G1);
            Assert.Equal(248, vm.B1);
            // Slot 2 (index 1) was planted as red (R=248).
            Assert.Equal(248, vm.R2);
            Assert.Equal(0, vm.G2);
            Assert.Equal(0, vm.B2);
            // Slot 16 (last) was planted as blue.
            Assert.Equal(0, vm.R16);
            Assert.Equal(0, vm.G16);
            Assert.Equal(248, vm.B16);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_RoundTripsPaletteBytes()
    {
        var rom = MakeMinimalRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImagePalletViewModel();
            vm.LoadEntry(0x100000u, 1, 0, null);

            vm.R1 = 0xF8; vm.G1 = 0;    vm.B1 = 0;     // red
            vm.R2 = 0;    vm.G2 = 0xF8; vm.B2 = 0;     // green
            vm.R3 = 0;    vm.G3 = 0;    vm.B3 = 0xF8;  // blue
            vm.Write();

            vm.LoadEntry(0x100000u, 1, 0, null);
            Assert.Equal(248, vm.R1); Assert.Equal(0, vm.G1); Assert.Equal(0, vm.B1);
            Assert.Equal(0, vm.R2); Assert.Equal(248, vm.G2); Assert.Equal(0, vm.B2);
            Assert.Equal(0, vm.R3); Assert.Equal(0, vm.G3); Assert.Equal(248, vm.B3);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot CLI plan review #1: the JumpTo / LoadEntry path must
    /// accept both raw offsets and GBA pointers identically.
    /// </summary>
    [Fact]
    public void ViewModel_JumpTo_AcceptsGbaPointer_StoresAndReads()
    {
        var rom = MakeMinimalRomWithPaletteAt(0x100000u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            var vmPtr = new ImagePalletViewModel();
            vmPtr.LoadEntry(0x08100000u, 1, 0, null);

            var vmOff = new ImagePalletViewModel();
            vmOff.LoadEntry(0x00100000u, 1, 0, null);

            Assert.Equal(vmOff.R1, vmPtr.R1);
            Assert.Equal(vmOff.G1, vmPtr.G1);
            Assert.Equal(vmOff.B1, vmPtr.B1);
            Assert.Equal(248, vmPtr.R1); // white slot
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Write at index 1 must leave index 0 bytes untouched. Index
    /// arithmetic verified at the VM level (PaletteCore does the same
    /// for unit tests; this exercises the VM wrapper).
    /// </summary>
    [Fact]
    public void ViewModel_Write_NonZeroIndex_DoesNotOverwriteIndex0()
    {
        var rom = MakeMinimalRomWithPaletteAt(0x100000u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // index 0 already has the white/red/.../blue pattern.
            ushort idx0Color0Before = (ushort)(rom.Data[0x100000] | (rom.Data[0x100001] << 8));

            var vm = new ImagePalletViewModel();
            vm.LoadEntry(0x100000u, 2, 1, null);
            vm.R1 = 0xF8; vm.G1 = 0xF8; vm.B1 = 0;
            vm.Write();

            ushort idx0Color0After = (ushort)(rom.Data[0x100000] | (rom.Data[0x100001] << 8));
            Assert.Equal(idx0Color0Before, idx0Color0After);
            // Index 1 slot 0 reflects the yellow write (R+G).
            ushort idx1Color0 = (ushort)(rom.Data[0x100020] | (rom.Data[0x100021] << 8));
            Assert.Equal(0x03FF, idx1Color0); // R=0x1F | G<<5=0x3E0
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot CLI plan review #2: caller wiring test. The
    /// ImagePortraitView.JumpToPalette_Click MUST forward the
    /// portrait's PalettePtr into the ImagePalletView so the rebuilt
    /// editor opens with real data.
    ///
    /// Source-level test (no Avalonia headless instantiation): assert
    /// the JumpToPalette_Click source contains the expected
    /// JumpTo(_vm.PalettePtr, ...) invocation.
    /// </summary>
    [Fact]
    public void ImagePortraitJumpToPalette_PassesPalettePtr_ToImagePalletView()
    {
        string source = File.ReadAllText(ImagePortraitCodeBehindPath());
        // Locate the JumpToPalette_Click method body.
        int idx = source.IndexOf("void JumpToPalette_Click", StringComparison.Ordinal);
        Assert.True(idx > 0, "JumpToPalette_Click not found");
        // The method must call JumpTo on the opened ImagePalletView
        // window using PalettePtr.
        int methodEnd = source.IndexOf("\n        }", idx, StringComparison.Ordinal);
        Assert.True(methodEnd > idx);
        string methodBody = source.Substring(idx, methodEnd - idx);
        Assert.Contains("Open<ImagePalletView>()", methodBody);
        Assert.Contains(".JumpTo(", methodBody);
        Assert.Contains("PalettePtr", methodBody);
    }

    // ===================================================================
    // #994: Functional edit->undo->redo round-trip
    // ===================================================================

    /// <summary>
    /// #994: write a palette, undo it, verify CanRedo is true, redo it,
    /// verify the bytes are restored to the edited state.
    /// </summary>
    [Fact]
    public void Palette_EditUndoRedo_RoundTrip()
    {
        var rom = MakeMinimalRomWithPaletteAt(0x100000u);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new ImagePalletViewModel();
            vm.LoadEntry(0x100000u, 1, 0, null);

            // Capture original bytes at slot 0.
            byte origLo = rom.Data[0x100000];
            byte origHi = rom.Data[0x100001];

            // Edit slot 0 to a known color (pure green = 0, 248, 0).
            var undoService = new UndoService();
            undoService.Begin("Edit Palette");
            vm.R1 = 0;
            vm.G1 = 248;
            vm.B1 = 0;
            vm.Write();
            undoService.Commit();

            // ROM must have changed.
            byte editedLo = rom.Data[0x100000];
            byte editedHi = rom.Data[0x100001];
            Assert.False(editedLo == origLo && editedHi == origHi,
                "ROM bytes should have changed after the write");

            // Undo — bytes must revert.
            CoreState.Undo.RunUndo();
            vm.LoadEntry(0x100000u, 1, 0, null);
            Assert.Equal(origLo, rom.Data[0x100000]);
            Assert.Equal(origHi, rom.Data[0x100001]);

            // CanRedo must be true after undo.
            Assert.True(CoreState.Undo.CanRedo, "CanRedo must be true after undo");

            // Redo — bytes must return to the edited state.
            CoreState.Undo.RunRedo();
            vm.LoadEntry(0x100000u, 1, 0, null);
            Assert.Equal(editedLo, rom.Data[0x100000]);
            Assert.Equal(editedHi, rom.Data[0x100001]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    /// <summary>
    /// Returns the offset of the closing `>` of an AXAML element start
    /// tag at <paramref name="elementStart"/>, skipping over attribute
    /// values that may contain literal `>` characters (rare but
    /// possible in tooltips).
    /// </summary>
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

    /// <summary>Build a 16 MB ROM with FE8U signature and zero palette bytes.</summary>
    static ROM MakeMinimalRom()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        return rom;
    }

    /// <summary>
    /// Build a synthetic ROM with two 16-color palettes planted at
    /// <paramref name="addr"/>. Index 0 = white / red / 14 zeros /
    /// blue at slot 16. Index 1 = all zeros (untouched).
    /// </summary>
    static ROM MakeMinimalRomWithPaletteAt(uint addr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        ushort[] colors = new ushort[16];
        colors[0] = 0x7FFF;   // white
        colors[1] = 0x001F;   // red
        // colors[2..14] stay zero (black)
        colors[15] = 0x7C00;  // blue (slot 16 in human terms)
        for (int i = 0; i < 16; i++)
        {
            rom.Data[(int)(addr + i * 2)] = (byte)(colors[i] & 0xFF);
            rom.Data[(int)(addr + i * 2 + 1)] = (byte)(colors[i] >> 8);
        }
        return rom;
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string AxamlPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImagePalletView.axaml");

    static string ViewCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImagePalletView.axaml.cs");

    static string ImagePortraitCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImagePortraitView.axaml.cs");

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
