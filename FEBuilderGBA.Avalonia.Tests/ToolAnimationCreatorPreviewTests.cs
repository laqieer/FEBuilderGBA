// SPDX-License-Identifier: GPL-3.0-or-later
// #1024 headless Avalonia tests for the Animation Creator live frame preview
// (Map-Action kind). Covers:
//   - the preview GbaImageControl is present + carries the expected AutomationId,
//   - the old "Animation preview (deferred ..." placeholder TextBlock is gone,
//   - selecting a frame whose Image/Palette pointers reference a valid compressed
//     OBJ + palette renders a non-empty image into the preview control,
//   - selecting no frame (or clearing) leaves the preview empty.
//
// The render assertions use the real SkiaSharp decoder (the Core stub only
// produces synthetic blank images); they are guarded so they no-op cleanly when
// the native decoder is unavailable, matching the codebase pattern.
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ToolAnimationCreatorPreviewTests
{
    const int OBJ_OFFSET = 0x400;
    const int PAL_OFFSET = 0x900;
    const int ROM_SIZE = 0x2000;
    const int OBJ_UNCOMPRESSED_LEN = (64 * 64) / 2; // 2048 bytes for 64x64 @4bpp

    static List<string> CollectAutomationIds(Control root)
        => root.GetLogicalDescendants()
               .OfType<Control>()
               .Select(c => AutomationProperties.GetAutomationId(c))
               .Where(id => !string.IsNullOrEmpty(id))
               .ToList()!;

    /// <summary>
    /// Build a synthetic ROM with one map-action frame row at 0x210 whose img/pal
    /// pointers reference a valid LZ77-compressed 64x64 4bpp OBJ + 0x20 palette.
    /// </summary>
    static ROM BuildRomWithOneFrame(uint baseAddr)
    {
        byte[] data = new byte[ROM_SIZE];

        byte[] raw = new byte[OBJ_UNCOMPRESSED_LEN];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i & 0xFF);
        byte[] compressed = LZ77.compress(raw);
        System.Array.Copy(compressed, 0, data, OBJ_OFFSET, compressed.Length);

        // Frame row: wait=1, img -> OBJ_OFFSET, pal -> PAL_OFFSET (GBA pointers).
        data[baseAddr] = 1;
        WriteU32(data, baseAddr + 4u, U.toPointer((uint)OBJ_OFFSET));
        WriteU32(data, baseAddr + 8u, U.toPointer((uint)PAL_OFFSET));
        // Terminator at baseAddr+12 (already zero).

        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);
        return rom;
    }

    static void WriteU32(byte[] data, uint addr, uint v)
    {
        data[addr + 0] = (byte)(v & 0xFF);
        data[addr + 1] = (byte)((v >> 8) & 0xFF);
        data[addr + 2] = (byte)((v >> 16) & 0xFF);
        data[addr + 3] = (byte)((v >> 24) & 0xFF);
    }

    // ====================================================================
    // Structure: preview control present, correct AutomationId, no placeholder
    // ====================================================================
    [AvaloniaFact]
    public void PreviewControl_Present_WithAutomationId()
    {
        var view = new ToolAnimationCreatorView();

        var preview = view.FindControl<GbaImageControl>("MapActionPreview");
        Assert.NotNull(preview);

        var ids = CollectAutomationIds(view);
        Assert.Contains("ToolAnimationCreator_Preview_Image", ids);
    }

    [AvaloniaFact]
    public void Preview_NoDeferredPlaceholderTextBlock()
    {
        var view = new ToolAnimationCreatorView();

        // The old deferred-preview placeholder must be gone — no TextBlock in the
        // view should carry that text.
        bool hasPlaceholder = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Any(tb => tb.Text != null && tb.Text.Contains("Animation preview (deferred"));
        Assert.False(hasPlaceholder);
    }

    // ====================================================================
    // Functional: selecting a frame renders into the preview (real decoder)
    // ====================================================================
    [AvaloniaFact]
    public void SelectingFrame_RendersPreviewImage()
    {
        var origRom = CoreState.ROM;
        var origService = CoreState.ImageService;
        try
        {
            CoreState.ImageService = new SkiaImageService();
            uint baseAddr = 0x210;
            CoreState.ROM = BuildRomWithOneFrame(baseAddr);

            var view = new ToolAnimationCreatorView();
            view.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0, "hint", baseAddr);

            var list = view.FindControl<ListBox>("FramesList");
            var preview = view.FindControl<GbaImageControl>("MapActionPreview");
            Assert.NotNull(list);
            Assert.NotNull(preview);
            Assert.Equal(1, list!.ItemCount);

            // Nothing selected yet -> preview empty.
            Assert.False(preview!.HasImage);

            // Select the single frame -> SelectionChanged drives RenderPreview.
            list.SelectedIndex = 0;

            Assert.True(preview.HasImage);
        }
        finally
        {
            CoreState.ROM = origRom;
            CoreState.ImageService = origService;
        }
    }

    [AvaloniaFact]
    public void ChangingSelectedFramePointer_ReRendersPreview()
    {
        var origRom = CoreState.ROM;
        var origService = CoreState.ImageService;
        try
        {
            CoreState.ImageService = new SkiaImageService();
            uint baseAddr = 0x210;
            CoreState.ROM = BuildRomWithOneFrame(baseAddr);

            var view = new ToolAnimationCreatorView();
            view.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0, "hint", baseAddr);

            var list = view.FindControl<ListBox>("FramesList");
            var preview = view.FindControl<GbaImageControl>("MapActionPreview");
            list!.SelectedIndex = 0;
            Assert.True(preview!.HasImage);

            // Point the image pointer at an invalid (zero) offset -> preview clears
            // via the tracked-frame PropertyChanged subscription.
            var frame = list.SelectedItem as FEBuilderGBA.Avalonia.ViewModels.EditableMapActionFrame;
            Assert.NotNull(frame);
            frame!.ImagePointer = 0;

            Assert.False(preview.HasImage);
        }
        finally
        {
            CoreState.ROM = origRom;
            CoreState.ImageService = origService;
        }
    }
}
