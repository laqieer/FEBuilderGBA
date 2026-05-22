using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for <see cref="IconPreviewControl"/>.
///
/// <para>
/// Tests originally landed with PR #351 to cover issue #342's clipping bug
/// (Stretch="Uniform" with insufficient outer box size). Updated by the
/// #342 follow-up (2026-05-21) to assert the WinForms-equivalent
/// "consistent visual size" invariant: the inner Image always matches the
/// outer Border, with Stretch=Fill + BitmapInterpolationMode=HighQuality as
/// the defaults that mirror WinForms PictureBoxSizeMode.StretchImage +
/// InterpolationMode.Bicubic.
/// </para>
/// <para>
/// Cross-bitmap-size consistency assertions and consumer wiring checks live
/// in <see cref="IconPreviewConsistentSizeTests"/>.
/// </para>
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

    /// <summary>
    /// Disposable host that owns a transient <see cref="Window"/> for layout-pass
    /// testing of the embedded <see cref="IconPreviewControl"/>. Disposing closes
    /// the window so the headless runner does not leak resources between tests
    /// (PR #351 review).
    /// </summary>
    sealed class TestHost : System.IDisposable
    {
        public Window Window { get; }
        public IconPreviewControl Control { get; }

        public TestHost(Window window, IconPreviewControl control)
        {
            Window = window;
            Control = control;
        }

        public void Dispose()
        {
            try { Window.Close(); } catch { /* best-effort */ }
        }
    }

    /// <summary>Force a layout pass on the freshly-created control so Bounds become populated.</summary>
    static TestHost HostInWindow(IconPreviewControl control,
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
        return new TestHost(window, control);
    }

    [AvaloniaFact]
    public void MeasuresToConfiguredSize()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        var border = control.FindControl<Border>("OuterBorder");
        Assert.NotNull(border);
        Assert.Equal(64, border!.Width);
        Assert.Equal(64, border.Height);
    }

    /// <summary>
    /// 16x16 source (animType=0 wait icon) fills the fixed 64x64 preview box.
    /// Renamed from RendersFullBitmapNoCrop_16x16 per the #342 follow-up: the
    /// invariant is now "inner Image matches outer Border", not "inner Image
    /// equals bitmap pixel size × Scale".
    /// </summary>
    [AvaloniaFact]
    public void FillsFixedPreviewBox_16x16()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(64, imageDisplay!.Width);
        Assert.Equal(64, imageDisplay.Height);
    }

    /// <summary>
    /// 16x24 source (animType=1 tall wait icon) fills the fixed 64x64 box.
    /// Stretch=Fill stretches the bitmap to 64x64 with bicubic-equivalent
    /// interpolation, matching WinForms.
    /// </summary>
    [AvaloniaFact]
    public void FillsFixedPreviewBox_16x24()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(64, imageDisplay!.Width);
        Assert.Equal(64, imageDisplay.Height);
    }

    /// <summary>
    /// 32x32 source (animType=2 wait icon — the original #342 crop case) fills
    /// the fixed 64x64 box.
    /// </summary>
    [AvaloniaFact]
    public void FillsFixedPreviewBox_32x32()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(32, 32), 32, 32);
        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(64, imageDisplay!.Width);
        Assert.Equal(64, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void HandlesNullImage()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

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
    /// XAML-set Scale/MaxImageWidth/MaxImageHeight must recompute BOTH the
    /// outer Border size AND the inner Image size at runtime. Under the new
    /// WinForms-matching contract (#342 follow-up) the inner Image always
    /// equals the outer Border, so this asserts they stay locked together.
    /// </summary>
    [AvaloniaFact]
    public void PropertyChange_RecomputesSize()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        var border = control.FindControl<Border>("OuterBorder");
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(border);
        Assert.NotNull(imageDisplay);

        // Initial state: 64×64 outer (no bitmap yet → inner has NaN size).
        Assert.Equal(64, border!.Width);
        Assert.Equal(64, border.Height);

        // Load a 16×16 bitmap → inner now equals outer (64x64).
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        Assert.Equal(64, imageDisplay!.Width);
        Assert.Equal(64, imageDisplay.Height);

        // Bump Scale → outer grows to 96×96 and inner stays locked at 96×96.
        control.Scale = 3;
        Assert.Equal(96, border.Width);
        Assert.Equal(96, border.Height);
        Assert.Equal(96, imageDisplay.Width);
        Assert.Equal(96, imageDisplay.Height);

        // Change MaxImageWidth → outer recomputes on width; inner stays locked.
        control.MaxImageWidth = 16;
        Assert.Equal(48, border.Width);  // 3 * 16
        Assert.Equal(96, border.Height); // unchanged
        Assert.Equal(48, imageDisplay.Width);
        Assert.Equal(96, imageDisplay.Height);

        // Change MaxImageHeight → outer height recomputes; inner stays locked.
        control.MaxImageHeight = 40;
        Assert.Equal(48, border.Width);
        Assert.Equal(120, border.Height); // 3 * 40
        Assert.Equal(48, imageDisplay.Width);
        Assert.Equal(120, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void InvalidPalette_DoesNotThrow()
    {
        // FromImage with an indexed image whose palette is empty must
        // return a (transparent) bitmap instead of throwing — matching the
        // original GbaImageControl.SetImage behaviour.
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

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
        // #342 follow-up: defaults match WinForms PictureBoxSizeMode.StretchImage
        // + InterpolationMode.Bicubic.
        Assert.Equal(Stretch.Fill, control.Stretch);
        Assert.Equal(BitmapInterpolationMode.HighQuality, control.BitmapInterpolationMode);
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
        using var host = HostInWindow(control, hostWidth: 250, hostHeight: 120);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);

        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        // 64×64 inner image must fit fully inside the 250×120 host.
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
