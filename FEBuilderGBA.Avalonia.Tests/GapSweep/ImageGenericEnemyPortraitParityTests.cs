// SPDX-License-Identifier: GPL-3.0-or-later
// Wiring-parity tests for ImageGenericEnemyPortraitView Image Import/Export
// (issue #907). The WF ImageGenericEnemyPortraitForm has Export/Import buttons;
// the Avalonia view previously had NONE. These tests assert the new
// preview + Export PNG + Import Image surface is wired correctly.
using System;
using System.IO;
using System.Text.RegularExpressions;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class ImageGenericEnemyPortraitParityTests
{
    // -----------------------------------------------------------------
    // AXAML surface — preview image + Export / Import buttons present.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasPreviewImageControl()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageGenericEnemyPortrait_Preview_Image\"", axaml);
        Assert.Contains("GbaImageControl", axaml);
    }

    [Fact]
    public void View_HasPalettePointerLabel()
    {
        // The VM now reads the palette pointer at +0x20 — the view must surface it.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ImageGenericEnemyPortrait_PalettePtr_Label\"", axaml);
    }

    [Fact]
    public void View_HasExportButton_WiredAndEnabledGated()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageGenericEnemyPortrait_Export_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "Export button tag not found");
        Assert.Contains("Click=\"ExportButton_Click\"", m.Value);
        Assert.Contains("IsEnabled=\"{Binding IsLoaded}\"", m.Value);
    }

    [Fact]
    public void View_HasImportButton_WiredAndEnabledGated()
    {
        string axaml = ReadAxaml();
        var rx = new Regex(
            "AutomationId=\"ImageGenericEnemyPortrait_Import_Button\"[\\s\\S]*?/>",
            RegexOptions.Compiled);
        Match m = rx.Match(axaml);
        Assert.True(m.Success, "Import button tag not found");
        Assert.Contains("Click=\"ImportButton_Click\"", m.Value);
        Assert.Contains("IsEnabled=\"{Binding IsLoaded}\"", m.Value);
    }

    [Fact]
    public void View_AllButtons_AreWiredOrExplicitlyInert()
    {
        string axaml = ReadAxaml();
        var buttonRx = new Regex(@"<Button\b([\s\S]*?)/>|<Button\b([\s\S]*?)>",
            RegexOptions.Compiled);
        var matches = buttonRx.Matches(axaml);
        Assert.True(matches.Count >= 2,
            $"Expected at least 2 Button tags in the AXAML; found {matches.Count}.");
        foreach (Match m in matches)
        {
            string tag = m.Value;
            if (!tag.Contains("ImageGenericEnemyPortrait_")) continue;
            bool hasClick = tag.Contains("Click=\"");
            bool inert = tag.Contains("IsEnabled=\"False\"") || tag.Contains("IsVisible=\"False\"");
            Assert.True(hasClick || inert,
                $"Button without Click handler nor IsEnabled/IsVisible=False: {tag}");
        }
    }

    // -----------------------------------------------------------------
    // Code-behind — Import goes through UndoService + Core; Export uses
    // the GbaImageControl ExportPng seam (regex-declaration style so a
    // signature tweak (async void) does not break the assertion).
    // -----------------------------------------------------------------

    [Fact]
    public void View_ImportHandler_CallsCoreAndUsesUndoService()
    {
        string source = ReadCodeBehind();
        // Calls the dedicated Core helper.
        Assert.Matches(new Regex(
            @"void\s+ImportButton_Click[\s\S]*?GenericEnemyPortraitImportCore\.ImportPortrait",
            RegexOptions.Compiled), source);
        // Wraps the write in the UndoService and rolls back / commits.
        Assert.Matches(new Regex(
            @"void\s+ImportButton_Click[\s\S]*?_undoService\.Begin[\s\S]*?_undoService\.(Commit|Rollback)",
            RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_ImportHandler_PassesBothSlots()
    {
        // CORRECTION 2: exactly TWO slot args — image @ +0, palette @ +0x20.
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(
            @"ImportPortrait\([\s\S]*?entryAddr\s*\+\s*0,[\s\S]*?PALETTE_SLOT_OFFSET",
            RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_ExportHandler_UsesExportPngSeam()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(
            @"void\s+ExportButton_Click[\s\S]*?PreviewImage\.ExportPng",
            RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_NoRomWriteBypassesUndoScope()
    {
        // Every direct ROM-write primitive in the code-behind must live inside
        // a `_undoService.Begin / Commit` window. The view itself should not
        // write ROM directly (it delegates to the Core helper), so this is a
        // belt-and-braces guard.
        string source = ReadCodeBehind();
        var writePattern = new Regex(@"\brom\.(?:write_(?:u8|u16|u32|p32|range|fill)|SetU(?:8|16|32))\b",
            RegexOptions.Compiled);
        foreach (Match m in writePattern.Matches(source))
        {
            int matchIdx = m.Index;
            int methodStart = source.LastIndexOf("void ", matchIdx, StringComparison.Ordinal);
            if (methodStart < 0) methodStart = 0;
            string body = source.Substring(methodStart);
            Assert.Contains("_undoService.Begin", body);
        }
    }

    // -----------------------------------------------------------------
    // ViewModel — palette pointer read @ +0x20 and preview render.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_PaletteSlotOffset_IsFixed0x20()
    {
        Assert.Equal(0x20u, ImageGenericEnemyPortraitViewModel.PALETTE_SLOT_OFFSET);
    }

    [Fact]
    public void ViewModel_LoadEntry_ReadsPalettePointerAt0x20()
    {
        ROM rom = MakeRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x300u;
            // image ptr @ +0, palette ptr @ +0x20.
            U.write_u32(rom.Data, entryAddr + 0x00, U.toPointer(0x110000));
            U.write_u32(rom.Data, entryAddr + 0x20, U.toPointer(0x120000));

            var vm = new ImageGenericEnemyPortraitViewModel();
            vm.LoadEntry(entryAddr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(U.toPointer(0x110000), vm.ImagePointer);
            Assert.Equal(U.toPointer(0x120000), vm.PalettePointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RenderPreview_NoSelection_ReturnsNull()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ImageGenericEnemyPortraitViewModel();
            Assert.Null(vm.RenderPreview());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_RenderPreview_NullPointers_ReturnsNull()
    {
        ROM rom = MakeRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x300u;
            // Leave image/palette pointers as 0xFFFFFFFF (not a valid pointer).
            U.write_u32(rom.Data, entryAddr + 0x00, 0xFFFFFFFFu);
            U.write_u32(rom.Data, entryAddr + 0x20, 0xFFFFFFFFu);

            var vm = new ImageGenericEnemyPortraitViewModel();
            vm.LoadEntry(entryAddr);
            Assert.Null(vm.RenderPreview());
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1100000];
        Array.Fill(data, (byte)0xFF);
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageGenericEnemyPortraitView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageGenericEnemyPortraitView.axaml.cs"));

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
