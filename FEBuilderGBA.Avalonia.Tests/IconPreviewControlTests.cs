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
/// Tests originally landed with PR #351 to cover issue #342's clipping bug.
/// The #342 follow-up (2026-05-26) restores natural-aspect rendering: the
/// inner Image is sized to <c>bitmap.PixelSize * Scale</c> (not the outer
/// Border), with defaults <c>Stretch=Uniform</c> + <c>BitmapInterpolationMode=None</c>
/// (nearest-neighbour). Different source aspects render at different visual
/// sizes, all centered inside the fixed outer Border, never clipped.
/// </para>
/// <para>
/// Cross-bitmap-size invariants and consumer wiring checks live
/// in <see cref="IconPreviewNaturalSizeTests"/>.
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
    /// 16x16 source (animType=0 wait icon) renders at its natural aspect:
    /// 32x32 inner Image (Scale=2) centered inside the 64x64 outer Border.
    /// Renamed from FillsFixedPreviewBox_16x16 per the #342 follow-up
    /// (2026-05-26): the invariant is now "inner Image = bitmap * Scale",
    /// not "inner Image = outer Border" — the user wants natural-aspect
    /// rendering, not forced-uniform-size distortion.
    /// </summary>
    [AvaloniaFact]
    public void RendersAtNaturalSize_16x16()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(32, imageDisplay.Height);
    }

    /// <summary>
    /// 16x24 source (animType=1 tall wait icon) renders at natural aspect:
    /// 32x48 inner Image centered inside the 64x64 outer Border. Stretch=Uniform
    /// preserves the 2:3 aspect ratio with no distortion.
    /// </summary>
    [AvaloniaFact]
    public void RendersAtNaturalSize_16x24()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(48, imageDisplay.Height);
    }

    /// <summary>
    /// 32x32 source (animType=2 wait icon — the original #342 crop case) renders
    /// at natural aspect: 64x64 inner Image fully fills the 64x64 outer Border
    /// with no clipping.
    /// </summary>
    [AvaloniaFact]
    public void RendersAtNaturalSize_32x32()
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
    /// outer Border size AND the inner Image size at runtime. Under the
    /// natural-aspect contract (#342 follow-up 2026-05-26), the inner Image
    /// is sized to <c>bitmap.PixelSize * Scale</c> clamped to the outer
    /// Border — so changing Scale moves the inner with the source, and
    /// shrinking Max* below the scaled bitmap size clamps the inner Image.
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

        // Load a 16×16 bitmap → inner = 16 * Scale = 32x32 (natural aspect).
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(32, imageDisplay.Height);

        // Bump Scale → outer grows to 96×96, inner grows to 48x48 (16 * 3).
        control.Scale = 3;
        Assert.Equal(96, border.Width);
        Assert.Equal(96, border.Height);
        Assert.Equal(48, imageDisplay.Width);
        Assert.Equal(48, imageDisplay.Height);

        // Shrink MaxImageWidth below the scaled bitmap → outer 48 wide (3*16),
        // inner clamped to outer (48 wide, still 48 tall).
        control.MaxImageWidth = 16;
        Assert.Equal(48, border.Width);  // 3 * 16
        Assert.Equal(96, border.Height); // 3 * 32 still
        Assert.Equal(48, imageDisplay.Width); // bitmap 16 * 3 = 48 (no clamp)
        Assert.Equal(48, imageDisplay.Height); // unchanged from prior step

        // Bump MaxImageHeight large → outer height grows, inner stays at scaled bitmap.
        control.MaxImageHeight = 40;
        Assert.Equal(48, border.Width);
        Assert.Equal(120, border.Height); // 3 * 40
        Assert.Equal(48, imageDisplay.Width);
        Assert.Equal(48, imageDisplay.Height); // 16 * 3 = 48 (still bitmap-natural)
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
        // #342 follow-up (2026-05-26): defaults are natural-aspect rendering
        // — Stretch=Uniform (no distortion) + BitmapInterpolationMode=None
        // (nearest-neighbour for pixel-art crispness).
        Assert.Equal(Stretch.Uniform, control.Stretch);
        Assert.Equal(BitmapInterpolationMode.None, control.BitmapInterpolationMode);
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
