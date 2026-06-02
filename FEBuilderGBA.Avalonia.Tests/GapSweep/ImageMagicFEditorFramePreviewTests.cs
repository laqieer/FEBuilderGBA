// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for #852 — Magic FEditor magic-effect frame preview +
// read-only Export PNG in the Avalonia ImageMagicFEditorView.
//
// Verifies:
//   - Preview control is a GbaImageControl (not a plain Image or deferred label).
//   - Export PNG button exists, gated on CanExportMagicFrame.
//   - Import and Open/Select source buttons remain disabled (out of scope).
//   - ViewModel.RenderMagicFramePreview: no-magic-system → null.
//   - ViewModel.CanExportMagicFrame gate.
//   - #500 deferred label text is removed from the AXAML.
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class ImageMagicFEditorFramePreviewTests
{
    // -------------------------------------------------------------------
    // AXAML structure: preview is a GbaImageControl, Export button present
    // -------------------------------------------------------------------

    /// <summary>
    /// The "Display Example" row must now use a GbaImageControl named
    /// MagicFramePreview, replacing the deferred TextBlock from #500.
    /// </summary>
    [Fact]
    public void View_HasGbaImageControl_MagicFramePreview()
    {
        string axaml = ReadAxaml();
        Assert.Contains("controls:GbaImageControl", axaml);
        Assert.Contains("Name=\"MagicFramePreview\"", axaml);
        Assert.Contains("ImageMagicFEditor_SamplePreview_Label", axaml);
    }

    /// <summary>
    /// The Export PNG button must be present in the AXAML with the correct
    /// automation id and wired click handler.
    /// </summary>
    [Fact]
    public void View_HasExportPngButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_ExportPng_Button", axaml);
        Assert.Contains("Content=\"Export PNG\"", axaml);
        Assert.Contains("Click=\"ExportPng_Click\"", axaml);
    }

    /// <summary>
    /// The old deferred "#500" marker text must be removed from the AXAML
    /// (it was replaced by the live GbaImageControl in #852).
    /// </summary>
    [Fact]
    public void View_DeferredX500_PreviewLabel_Removed()
    {
        string axaml = ReadAxaml();
        // The old text "(preview deferred - tracked by #500)" must be gone.
        Assert.DoesNotContain("preview deferred", axaml);
    }

    /// <summary>
    /// The FrameBox NumericUpDown must have a ValueChanged handler so
    /// frame-number changes trigger a re-render.
    /// </summary>
    [Fact]
    public void View_FrameBox_HasValueChangedHandler()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ValueChanged=\"FrameBox_ValueChanged\"", axaml);
    }

    /// <summary>
    /// The BinInfo log TextBlock must be present in the AXAML.
    /// </summary>
    [Fact]
    public void View_HasBinInfoLabel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("ImageMagicFEditor_BinInfo_Label", axaml);
        Assert.Contains("Name=\"BinInfo\"", axaml);
    }

    // -------------------------------------------------------------------
    // Code-behind: ExportPng_Click and FrameBox_ValueChanged handlers present
    // -------------------------------------------------------------------

    [Fact]
    public void CodeBehind_HasExportPngClickHandler()
    {
        string cs = ReadCodeBehind();
        Assert.Contains("void ExportPng_Click", cs);
        Assert.Contains("MagicFramePreview.ExportPng", cs);
    }

    [Fact]
    public void CodeBehind_HasFrameBoxValueChangedHandler()
    {
        string cs = ReadCodeBehind();
        Assert.Contains("void FrameBox_ValueChanged", cs);
        Assert.Contains("RenderPreview", cs);
    }

    [Fact]
    public void CodeBehind_HasRenderPreviewMethod()
    {
        string cs = ReadCodeBehind();
        Assert.Contains("void RenderPreview()", cs);
        Assert.Contains("_vm.RenderMagicFramePreview", cs);
    }

    // -------------------------------------------------------------------
    // #881: Import now wired; Export/OpenSource/SelectSource also wired
    // -------------------------------------------------------------------

    /// <summary>
    /// Import button is now WIRED in #881 — no longer hard-coded disabled.
    /// It is gated at runtime (magic-system presence via UpdateWriteControlsEnabled).
    /// </summary>
    [Fact]
    public void View_ImportButton_IsWired_NotHardDisabled()
    {
        string axaml = ReadAxaml();
        const string id = "ImageMagicFEditor_MagicAnimeImport_Button";
        var pattern = new Regex(
            @"<Button[^>]*AutomationId=""" + Regex.Escape(id) + @"""[^>]*?/>",
            RegexOptions.Singleline);
        var match = pattern.Match(axaml);
        Assert.True(match.Success,
            $"Expected a <Button AutomationId=\"{id}\" .../> element");
        // #881: The button must NOT have static IsEnabled="False".
        Assert.DoesNotContain("IsEnabled=\"False\"", match.Value);
        // Must have a wired click handler.
        Assert.Contains("Click=\"MagicAnimeImport_Click\"", match.Value);
    }

    /// <summary>
    /// Export button is no longer marked IsEnabled="False" (#878 PR1 wired).
    /// </summary>
    [Fact]
    public void View_ExportButton_NotDisabled()
    {
        string axaml = ReadAxaml();
        const string id = "ImageMagicFEditor_MagicAnimeExport_Button";
        var pattern = new Regex(
            @"<Button[^>]*AutomationId=""" + Regex.Escape(id) + @"""[^>]*?/>",
            RegexOptions.Singleline);
        var match = pattern.Match(axaml);
        Assert.True(match.Success);
        Assert.DoesNotContain("IsEnabled=\"False\"", match.Value);
    }

    /// <summary>
    /// OpenSource/SelectSource use IsVisible="False" (hidden until cached), not IsEnabled.
    /// </summary>
    [Theory]
    [InlineData("ImageMagicFEditor_OpenSource_Button")]
    [InlineData("ImageMagicFEditor_SelectSource_Button")]
    public void View_SourceButton_UsesIsVisibleNotIsEnabled(string id)
    {
        string axaml = ReadAxaml();
        var pattern = new Regex(
            @"<Button[^>]*AutomationId=""" + Regex.Escape(id) + @"""[^>]*?/>",
            RegexOptions.Singleline);
        var match = pattern.Match(axaml);
        Assert.True(match.Success,
            $"Expected a <Button AutomationId=\"{id}\" .../> element");
        // Buttons are now hidden (IsVisible="False") not disabled.
        Assert.Contains("IsVisible=\"False\"", match.Value);
        Assert.DoesNotContain("IsEnabled=\"False\"", match.Value);
    }

    // -------------------------------------------------------------------
    // ViewModel — RenderMagicFramePreview + CanExportMagicFrame
    // -------------------------------------------------------------------

    /// <summary>
    /// When no magic system is detected, RenderMagicFramePreview returns null.
    /// </summary>
    [Fact]
    public void ViewModel_RenderMagicFramePreview_NoMagic_ReturnsNull()
    {
        var prevRom = CoreState.ROM;
        try
        {
            // ROM without magic system patch.
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[0x1100000], "BE8E01");
            CoreState.ROM = rom;

            var vm = new ImageMagicFEditorViewModel();
            vm.RefreshPatchState();
            Assert.False(vm.MagicSystemDetected);

            string log;
            var img = vm.RenderMagicFramePreview(out log);
            Assert.Null(img);
            Assert.False(string.IsNullOrEmpty(log));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// CanExportMagicFrame is false when no magic system is detected.
    /// </summary>
    [Fact]
    public void ViewModel_CanExportMagicFrame_FalseWhenNoMagic()
    {
        var prevRom = CoreState.ROM;
        try
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[0x1100000], "BE8E01");
            CoreState.ROM = rom;

            var vm = new ImageMagicFEditorViewModel();
            vm.RefreshPatchState();
            Assert.False(vm.CanExportMagicFrame);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// CanExportMagicFrame is true only when both MagicSystemDetected and IsLoaded.
    /// </summary>
    [Fact]
    public void ViewModel_CanExportMagicFrame_RequiresBothMagicDetectedAndLoaded()
    {
        var prevRom = CoreState.ROM;
        try
        {
            var rom = MakeFe8uRomWithMagic();
            CoreState.ROM = rom;

            var vm = new ImageMagicFEditorViewModel();
            vm.RefreshPatchState();

            // Before loading an entry: IsLoaded = false → CanExport = false.
            Assert.True(vm.MagicSystemDetected, "Expected magic system to be detected");
            Assert.False(vm.IsLoaded);
            Assert.False(vm.CanExportMagicFrame);

            // After loading an entry: IsLoaded = true → CanExport = true.
            uint csaEntry = 0x00410000u;
            vm.LoadEntry(csaEntry, 0u);
            Assert.True(vm.IsLoaded);
            Assert.True(vm.CanExportMagicFrame);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicFEditorView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ImageMagicFEditorView.axaml.cs"));

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln");
        return dir;
    }

    static ROM MakeFe8uRomWithMagic()
    {
        var data = new byte[0x1100000];
        byte[] sig = {
            0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08,
            0x03, 0x00, 0x00, 0x00, 0x39, 0xD9, 0x95, 0x08,
        };
        Array.Copy(sig, 0, data, 0x95d780, sig.Length);
        byte[] csaPat = {
            0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02,
            0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02,
        };
        Array.Copy(csaPat, 0, data, 0x00200000, csaPat.Length);
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(data, 0x00200000 + csaPat.Length);
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }
}
