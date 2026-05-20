using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for <see cref="IconPreviewControl"/> covering the
/// issue #342 regression: list-preview icons were being clipped because the
/// previous <see cref="GbaImageControl"/> didn't reserve enough space for the
/// up-scaled bitmap. These tests verify the outer box sizes correctly for
/// each (Scale, MaxImageWidth, MaxImageHeight) combination, that property
/// changes recompute both outer and inner sizes at runtime (Copilot CLI
/// review note 1), and that bitmaps of various dimensions render without
/// crop.
/// </summary>
public class IconPreviewControlTests
{
    /// <summary>Create a solid-red RGBA byte array of the given dimensions.</summary>
    static byte[] MakeRgba(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = 0;
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    /// <summary>Force a layout pass on the freshly-created control so Bounds become populated.</summary>
    static (Window window, IconPreviewControl control) HostInWindow(IconPreviewControl control,
        double hostWidth = 250, double hostHeight = 120)
    {
        var window = new Window
        {
            Width = hostWidth,
            Height = hostHeight,
            Content = control,
        };
        window.Show();
        window.Measure(new Size(hostWidth, hostHeight));
        window.Arrange(new Rect(0, 0, hostWidth, hostHeight));
        window.UpdateLayout();
        return (window, control);
    }

    [AvaloniaFact]
    public void MeasuresToConfiguredSize()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        var border = control.FindControl<Border>("OuterBorder");
        Assert.NotNull(border);
        Assert.Equal(64, border!.Width);
        Assert.Equal(64, border.Height);
    }

    [AvaloniaFact]
    public void RendersFullBitmapNoCrop_16x16()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(32, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void RendersFullBitmapNoCrop_16x24()
    {
        // ClassEditor wait-icon variant (animType=1 produces 16×24 tall icons).
        // With Scale=2 → 32×48 scaled; with the 32×32 source cap (Scale*max = 64×64),
        // the 32×48 fits entirely. Issue #342 was a smaller cap clipping these.
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 24), 16, 24);

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(48, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void RendersFullBitmapNoCrop_32x32()
    {
        // Original bug repro: a 32×32 source clipped because the outer slot
        // was sized for a 16×16 source. With MaxImageWidth=32 and Scale=2
        // we now reserve a 64×64 outer box, so a 32×32 source up-scaled to
        // 64×64 must render in full.
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        control.SetRgbaData(MakeRgba(32, 32), 32, 32);

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(64, imageDisplay!.Width);
        Assert.Equal(64, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void HandlesNullImage()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        Assert.True(control.HasImage);

        // Should not throw and must clear the source.
        control.SetImage(null);
        Assert.False(control.HasImage);

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Null(imageDisplay!.Source);
    }

    /// <summary>
    /// Copilot CLI review note 1: XAML-set Scale/MaxImageWidth/MaxImageHeight
    /// must recompute BOTH the outer Border size AND the inner Image size when
    /// they change at runtime — not only at construction.
    /// </summary>
    [AvaloniaFact]
    public void PropertyChange_RecomputesSize()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        var border = control.FindControl<Border>("OuterBorder");
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(border);
        Assert.NotNull(imageDisplay);

        // Initial state: 64×64 outer.
        Assert.Equal(64, border!.Width);
        Assert.Equal(64, border.Height);

        // Load a 16×16 bitmap (scaled to 32×32 at Scale=2).
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(32, imageDisplay.Height);

        // Bump Scale → outer must grow to 96×96 and the scaled image to 48×48.
        control.Scale = 3;
        Assert.Equal(96, border.Width);
        Assert.Equal(96, border.Height);
        Assert.Equal(48, imageDisplay.Width);
        Assert.Equal(48, imageDisplay.Height);

        // Change MaxImageWidth → outer must recompute on width axis only.
        control.MaxImageWidth = 16;
        Assert.Equal(48, border.Width);  // 3 * 16
        Assert.Equal(96, border.Height); // unchanged
        // Image was 16 source px wide; scaled = 48, cap is 48 → 48.
        Assert.Equal(48, imageDisplay.Width);

        // Change MaxImageHeight → outer height recomputes.
        control.MaxImageHeight = 40;
        Assert.Equal(48, border.Width);
        Assert.Equal(120, border.Height); // 3 * 40
    }

    [AvaloniaFact]
    public void InvalidPalette_DoesNotThrow()
    {
        // FromImage with an indexed image whose palette is empty must
        // return a (transparent) bitmap instead of throwing — matching the
        // original GbaImageControl.SetImage behaviour.
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control);

        var image = new EmptyPaletteFakeImage(8, 8);
        var exception = Record.Exception(() => control.SetImage(image));
        Assert.Null(exception);
    }

    [AvaloniaFact]
    public void DefaultValues_AreReasonable()
    {
        var control = new IconPreviewControl();
        Assert.Equal(2, control.Scale);
        Assert.Equal(32, control.MaxImageWidth);
        Assert.Equal(32, control.MaxImageHeight);
    }

    [AvaloniaFact]
    public void ImageDisplay_UsesNearestNeighborInterpolation()
    {
        // GBA pixel art must use nearest-neighbour to stay crisp at scaled sizes.
        var control = new IconPreviewControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(global::Avalonia.Media.Imaging.BitmapInterpolationMode.None,
            RenderOptions.GetBitmapInterpolationMode(imageDisplay!));
    }

    /// <summary>
    /// Layout regression test: place the control inside a 250×120 host (the
    /// real editor left-column layout) and assert the rendered image fits
    /// inside the host bounds with no negative clip rect.
    /// </summary>
    [AvaloniaFact]
    public void LayoutInsideLeftColumn()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        HostInWindow(control, hostWidth: 250, hostHeight: 120);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);

        // Force a second layout pass so Bounds reflect the loaded bitmap.
        ((Window)control.Parent!).UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        // 32×32 image must fit fully inside the 250×120 host.
        Assert.True(imageDisplay!.Bounds.Width <= 250,
            $"image width {imageDisplay.Bounds.Width} exceeds host 250");
        Assert.True(imageDisplay.Bounds.Height <= 120,
            $"image height {imageDisplay.Bounds.Height} exceeds host 120");

        // And the image bounds must be non-negative (no negative-clip layout).
        Assert.True(imageDisplay.Bounds.Width >= 0);
        Assert.True(imageDisplay.Bounds.Height >= 0);
    }

    // ---- Helpers ----

    /// <summary>
    /// Fake IImage that claims to be indexed but exposes an empty palette,
    /// used for the InvalidPalette_DoesNotThrow test.
    /// </summary>
    sealed class EmptyPaletteFakeImage : global::FEBuilderGBA.IImage
    {
        readonly byte[] _data;

        public EmptyPaletteFakeImage(int w, int h)
        {
            Width = w;
            Height = h;
            _data = new byte[w * h];
        }

        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => true;
        public byte[] GetPixelData() => _data;
        public void SetPixelData(byte[] data) { }
        public byte[] GetPaletteGBA() => System.Array.Empty<byte>();
        public void SetPaletteGBA(byte[] gbaPalette) { }
        public byte[] GetPaletteRGBA() => System.Array.Empty<byte>();
        public void Save(string filePath) { }
        public byte[] EncodePng() => System.Array.Empty<byte>();
        public void Dispose() { }
    }
}
