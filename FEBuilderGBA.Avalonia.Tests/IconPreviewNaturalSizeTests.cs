using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Issue #342 follow-up (2026-05-26): the user clarified that class unit wait
/// icons "have different sizes in the game, so you cannot display them all in
/// the same size." PR #452's <c>Stretch="Fill"</c> approach (forcing every
/// source bitmap into the same outer-Border canvas) was rejected because it
/// distorts non-square sources (e.g. 16x24 cavalier icons squashed into 64x64).
///
/// The corrected invariant: <see cref="IconPreviewControl"/> renders each
/// bitmap at <c>bitmap.PixelSize * Scale</c> with <c>Stretch=Uniform</c>
/// (centered inside the outer Border). 16x16 → 32x32, 16x24 → 32x48,
/// 32x32 → 64x64 — three different visual sizes, all aspect-preserved, all
/// fully visible, none clipped.
///
/// These tests enforce that natural-aspect contract end-to-end:
///   * inner Image size = bitmap * Scale for the three documented animTypes;
///   * default Stretch is Uniform and default interpolation is None;
///   * runtime property changes propagate;
///   * stale-state regression covered (null → reload different size);
///   * all four list-preview consumer views still wire to IconPreviewControl
///     with the expected Scale/MaxImageWidth/MaxImageHeight;
///   * AddressListControl item-template Image uses Stretch="Uniform".
/// </summary>
public class IconPreviewNaturalSizeTests
{
    /// <summary>Create a solid-blue RGBA byte array of the given dimensions.</summary>
    static byte[] MakeRgba(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = 0;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

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

    // ----------------------------------------------------------------------
    // CORE INVARIANT: inner Image = bitmap.PixelSize * Scale (natural aspect)
    // ----------------------------------------------------------------------

    /// <summary>
    /// animType=0 wait icon (16x16 source). Natural-aspect contract: inner
    /// Image is 32x32 (Scale=2), CENTERED inside the 64x64 outer Border.
    /// </summary>
    [AvaloniaFact]
    public void Source16x16_InnerImage_MatchesBitmapScale()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(64, border.Width);
        Assert.Equal(64, border.Height);
        Assert.Equal(32, image.Width);
        Assert.Equal(32, image.Height);
    }

    /// <summary>
    /// animType=1 wait icon (16x24 tall source). Inner Image is 32x48,
    /// centered inside the 64x64 outer Border — 2:3 aspect preserved.
    /// </summary>
    [AvaloniaFact]
    public void Source16x24_InnerImage_MatchesBitmapScale()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(64, border.Width);
        Assert.Equal(64, border.Height);
        Assert.Equal(32, image.Width);
        Assert.Equal(48, image.Height);
    }

    /// <summary>
    /// animType=2 wait icon (32x32 source — was the original #342 crop case).
    /// Inner Image is 64x64, fully filling the 64x64 outer Border with no
    /// clipping.
    /// </summary>
    [AvaloniaFact]
    public void Source32x32_InnerImage_MatchesBitmapScale()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(32, 32), 32, 32);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(64, border.Width);
        Assert.Equal(64, border.Height);
        Assert.Equal(64, image.Width);
        Assert.Equal(64, image.Height);
    }

    /// <summary>
    /// Per Copilot CLI plan v5 review (refinement #1): the regression guard for
    /// PR #452's rejected "all icons same visual size" approach should be the
    /// OPPOSITE invariant — three different source aspects produce three
    /// different inner-Image sizes. This is the strong inverse of the old
    /// AllAnimTypes_RenderAtSameVisualSize test from PR #452.
    /// </summary>
    [AvaloniaFact]
    public void AllAnimTypes_RenderAtNaturalDifferentSizes()
    {
        var c0 = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        var c1 = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        var c2 = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var h0 = HostInWindow(c0);
        using var h1 = HostInWindow(c1);
        using var h2 = HostInWindow(c2);

        c0.SetRgbaData(MakeRgba(16, 16), 16, 16);
        c1.SetRgbaData(MakeRgba(16, 24), 16, 24);
        c2.SetRgbaData(MakeRgba(32, 32), 32, 32);

        h0.Window.UpdateLayout();
        h1.Window.UpdateLayout();
        h2.Window.UpdateLayout();

        var i0 = c0.FindControl<Image>("ImageDisplay")!;
        var i1 = c1.FindControl<Image>("ImageDisplay")!;
        var i2 = c2.FindControl<Image>("ImageDisplay")!;

        // Expected: 32x32, 32x48, 64x64. Three different visual sizes.
        Assert.Equal(32, i0.Width);
        Assert.Equal(32, i0.Height);
        Assert.Equal(32, i1.Width);
        Assert.Equal(48, i1.Height);
        Assert.Equal(64, i2.Width);
        Assert.Equal(64, i2.Height);

        // And explicitly NOT all equal (regression guard against PR #452).
        Assert.NotEqual(i0.Height, i1.Height);
        Assert.NotEqual(i0.Width, i2.Width);
        Assert.NotEqual(i1.Height, i2.Height);
    }

    /// <summary>
    /// Aspect-ratio invariant: source aspect must equal rendered aspect for
    /// every animType. With Stretch=Uniform and inner Image sized to
    /// bitmap*Scale, aspect must be preserved exactly.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(16, 16)]
    [InlineData(16, 24)]
    [InlineData(32, 32)]
    [InlineData(8, 16)]   // edge: very small icon
    public void PreservesAspectRatio_AllAnimTypes(int srcW, int srcH)
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);
        control.SetRgbaData(MakeRgba(srcW, srcH), srcW, srcH);
        host.Window.UpdateLayout();

        var image = control.FindControl<Image>("ImageDisplay")!;
        // bitmap * Scale, no distortion.
        Assert.Equal(srcW * 2, image.Width);
        Assert.Equal(srcH * 2, image.Height);
        // Aspect equals source aspect (within float precision).
        double srcAspect = (double)srcW / srcH;
        double imgAspect = image.Width / image.Height;
        Assert.Equal(srcAspect, imgAspect, 6);
    }

    /// <summary>
    /// Outer Border clamp: when the source is LARGER than Max*, the inner
    /// Image is clamped so the icon never paints outside the outer slot —
    /// the no-clipping promise of #342.
    /// </summary>
    [AvaloniaFact]
    public void OversizedSource_InnerImage_ClampedToOuterBorder()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);
        // 64x64 source — bigger than the 32x32 Max box.
        control.SetRgbaData(MakeRgba(64, 64), 64, 64);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        // Inner stays clamped to the outer (64x64), Stretch=Uniform downscales.
        Assert.Equal(64, border.Width);
        Assert.Equal(64, border.Height);
        Assert.Equal(64, image.Width);
        Assert.Equal(64, image.Height);
        // Inner never exceeds outer.
        Assert.True(image.Width <= border.Width);
        Assert.True(image.Height <= border.Height);
    }

    // ----------------------------------------------------------------------
    // Default render settings — natural-aspect contract
    // ----------------------------------------------------------------------

    /// <summary>Default is <c>Stretch.Uniform</c> for natural aspect.</summary>
    [AvaloniaFact]
    public void DefaultStretchMode_IsUniform()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(Stretch.Uniform, image.Stretch);
    }

    /// <summary>
    /// Default is <c>BitmapInterpolationMode.None</c> (nearest-neighbour) so
    /// GBA pixel-art stays crisp at integer up-scale.
    /// </summary>
    [AvaloniaFact]
    public void DefaultInterpolationMode_IsNone()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(BitmapInterpolationMode.None,
            RenderOptions.GetBitmapInterpolationMode(image));
    }

    // ----------------------------------------------------------------------
    // Runtime property changes — Stretch + BitmapInterpolationMode
    // ----------------------------------------------------------------------

    /// <summary>
    /// The <c>Stretch</c> styled property must apply to the inner Image
    /// both BEFORE and AFTER a bitmap is loaded.
    /// </summary>
    [AvaloniaFact]
    public void Stretch_RuntimeChange_AppliesBeforeAndAfterLoad()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;

        // Before load — change Stretch
        control.Stretch = Stretch.Fill;
        Assert.Equal(Stretch.Fill, image.Stretch);

        // Load a bitmap, then change again
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        control.Stretch = Stretch.UniformToFill;
        Assert.Equal(Stretch.UniformToFill, image.Stretch);

        // Back to default
        control.Stretch = Stretch.Uniform;
        Assert.Equal(Stretch.Uniform, image.Stretch);
    }

    /// <summary>
    /// The <c>BitmapInterpolationMode</c> styled property must apply to
    /// the inner Image both BEFORE and AFTER a bitmap is loaded.
    /// </summary>
    [AvaloniaFact]
    public void BitmapInterpolationMode_RuntimeChange_AppliesBeforeAndAfterLoad()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;

        // Before load — change interpolation
        control.BitmapInterpolationMode = BitmapInterpolationMode.HighQuality;
        Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));

        // Load a bitmap, then change again
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        control.BitmapInterpolationMode = BitmapInterpolationMode.LowQuality;
        Assert.Equal(BitmapInterpolationMode.LowQuality, RenderOptions.GetBitmapInterpolationMode(image));

        // Back to default
        control.BitmapInterpolationMode = BitmapInterpolationMode.None;
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(image));
    }

    // ----------------------------------------------------------------------
    // Stale-state regression: null then load different size still renders right
    // ----------------------------------------------------------------------

    /// <summary>
    /// Setting a bitmap, clearing via <c>SetImage(null)</c>, then loading a
    /// DIFFERENT-sized bitmap must produce an inner Image sized to the NEW
    /// bitmap's natural scale (not the previous one's). Guards against stale
    /// Width/Height left from the previous SetImage call.
    /// </summary>
    [AvaloniaFact]
    public void SetImage_Null_ThenLoadDifferentSize_RendersAtNewNaturalSize()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        // Load 16x16 first → 32x32 inner
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(32, image.Width);
        Assert.Equal(32, image.Height);

        // Clear
        control.SetImage(null);
        Assert.False(control.HasImage);

        // Load 16x24 → inner should be 32x48 (NOT 32x32 from the previous one)
        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();
        Assert.Equal(32, image.Width);
        Assert.Equal(48, image.Height);
    }

    // ----------------------------------------------------------------------
    // Scale/Max property changes after load
    // ----------------------------------------------------------------------

    /// <summary>
    /// After loading a bitmap, changing Scale must update BOTH the outer
    /// Border AND the inner Image (inner = bitmap * Scale).
    /// </summary>
    [AvaloniaFact]
    public void ScaleChange_AfterLoad_OuterAndInner_BothUpdate()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(64, border.Width);
        Assert.Equal(32, image.Width);

        control.Scale = 3;

        Assert.Equal(96, border.Width);
        Assert.Equal(96, border.Height);
        Assert.Equal(48, image.Width);  // 16 * 3
        Assert.Equal(48, image.Height); // 16 * 3
    }

    // ----------------------------------------------------------------------
    // Consumer wiring tests — all four list-preview slots
    // ----------------------------------------------------------------------

    /// <summary>ClassEditor preview slot: Scale=2, Max=32x32 → 64x64 box.</summary>
    [AvaloniaFact]
    public void ClassEditorView_ListPreview_Config()
    {
        var view = new ClassEditorView();
        var preview = view.FindControl<IconPreviewControl>("ListPreviewImage")!;
        Assert.NotNull(preview);
        Assert.Equal(2, preview.Scale);
        Assert.Equal(32, preview.MaxImageWidth);
        Assert.Equal(32, preview.MaxImageHeight);
    }

    /// <summary>ItemEditor preview slot: Scale=2, Max=32x32 → 64x64 box.</summary>
    [AvaloniaFact]
    public void ItemEditorView_ListPreview_Config()
    {
        var view = new ItemEditorView();
        var preview = view.FindControl<IconPreviewControl>("ListPreviewImage")!;
        Assert.NotNull(preview);
        Assert.Equal(2, preview.Scale);
        Assert.Equal(32, preview.MaxImageWidth);
        Assert.Equal(32, preview.MaxImageHeight);
    }

    /// <summary>ItemFE6 preview slot: Scale=2, Max=32x32 → 64x64 box.</summary>
    [AvaloniaFact]
    public void ItemFE6View_ListPreview_Config()
    {
        var view = new ItemFE6View();
        var preview = view.FindControl<IconPreviewControl>("ListPreviewImage")!;
        Assert.NotNull(preview);
        Assert.Equal(2, preview.Scale);
        Assert.Equal(32, preview.MaxImageWidth);
        Assert.Equal(32, preview.MaxImageHeight);
    }

    /// <summary>
    /// UnitEditor preview slot: Scale=1, Max=32x32 → 32x32 box (mini portrait
    /// is already 32x32, unlike class wait icons which are upscaled 2x).
    /// </summary>
    [AvaloniaFact]
    public void UnitEditorView_ListPreview_Config()
    {
        var view = new UnitEditorView();
        var preview = view.FindControl<IconPreviewControl>("ListPreviewImage")!;
        Assert.NotNull(preview);
        Assert.Equal(1, preview.Scale);
        Assert.Equal(32, preview.MaxImageWidth);
        Assert.Equal(32, preview.MaxImageHeight);
    }

    // ----------------------------------------------------------------------
    // AddressListControl item template — same natural-aspect fix.
    // Per Copilot CLI plan v5 review (refinement #2): the no-clipping
    // verification must also cover the composed UI, not just the standalone
    // control. The AddressListControl item template uses Stretch="Uniform"
    // so source bitmaps of different aspects (16x16, 16x24, 32x32 wait icons)
    // render at their natural aspect inside the fixed 32x32 list slot.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Parse the AddressListControl XAML and assert the icon Image uses
    /// <c>Stretch="Uniform"</c> (the natural-aspect fix in the #342 follow-up
    /// 2026-05-26).
    /// </summary>
    [AvaloniaFact]
    public void AddressListControl_IconImage_UsesUniformStretch()
    {
        // Locate repo root by walking up from the test assembly's directory
        // (AppDomain.CurrentDomain.BaseDirectory — the bin/Release/netN.0
        // folder) until we find the solution file. This pattern is shared
        // with other tests in this repo (see AutomationIdTests.cs) and is
        // less sensitive than relying on the current working directory.
        var dir = System.AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);

        var axamlPath = System.IO.Path.Combine(
            dir!, "FEBuilderGBA.Avalonia", "Controls", "AddressListControl.axaml");
        Assert.True(System.IO.File.Exists(axamlPath),
            $"AddressListControl.axaml not found under repo root '{dir}'");
        var xaml = System.IO.File.ReadAllText(axamlPath);

        // Match natural-aspect rendering — Stretch=Uniform, never Fill.
        Assert.Contains("Stretch=\"Uniform\"", xaml);
        var iconBlockStart = xaml.IndexOf("Source=\"{Binding Icon}\"");
        Assert.True(iconBlockStart >= 0, "Icon Image template not found in XAML");
        // Look forward 300 chars from "Source={Binding Icon}" for Stretch attribute.
        var window = xaml.Substring(iconBlockStart, System.Math.Min(300, xaml.Length - iconBlockStart));
        Assert.Contains("Stretch=\"Uniform\"", window);
        // Regression guard: the icon Image template must NOT use Stretch="Fill"
        // (the rejected PR #452 approach).
        Assert.DoesNotContain("Stretch=\"Fill\"", window);
    }
}
