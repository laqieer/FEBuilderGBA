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
/// Issue #342 follow-up (2026-05-21): wait icons in the Avalonia list-preview
/// slot rendered at three different sizes (32x32 / 32x48 / 64x64) depending on
/// the source bitmap's animType, because <see cref="IconPreviewControl"/> used
/// <c>Stretch="Uniform"</c> + nearest-neighbour and sized the inner Image to
/// <c>bitmap.PixelSize * Scale</c>.
///
/// WinForms ships <c>PictureBoxSizeMode.StretchImage</c> with
/// <c>InterpolationMode.Bicubic</c>: the picture-box always paints the bitmap
/// at the picture-box's own size, regardless of source dimensions
/// (FEBuilderGBA/ClassForm.Designer.cs:2477-2486; ItemForm.Designer.cs:2096-2104;
/// ClassFE6Form.Designer.cs:2055-2065). All animTypes render at the same
/// visual size in WinForms.
///
/// These tests enforce the WinForms-equivalent invariant in Avalonia:
/// <c>ImageDisplay.Bounds == OuterBorder.Bounds</c> for ALL source sizes, with
/// <c>Stretch.Fill</c> and <c>BitmapInterpolationMode.HighQuality</c> as the
/// defaults.
/// </summary>
public class IconPreviewConsistentSizeTests
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
    // CORE INVARIANT: inner Image always matches outer Border, for ALL sizes
    // ----------------------------------------------------------------------

    /// <summary>
    /// animType=0 wait icon (16x16 source). With the WinForms-matching contract,
    /// the inner Image must equal the outer Border size (64x64 for Scale=2,
    /// Max=32x32) regardless of source dimensions.
    /// </summary>
    [AvaloniaFact]
    public void Source16x16_InnerImage_MatchesOuterBorder()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(border.Width, image.Width);
        Assert.Equal(border.Height, image.Height);
    }

    /// <summary>
    /// animType=1 wait icon (16x24 tall source — myrm/cavalier shape).
    /// </summary>
    [AvaloniaFact]
    public void Source16x24_InnerImage_MatchesOuterBorder()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(border.Width, image.Width);
        Assert.Equal(border.Height, image.Height);
    }

    /// <summary>
    /// animType=2 wait icon (32x32 source — Demon King and other monster classes).
    /// This was the original #342 crop case; now must also equal the outer Border.
    /// </summary>
    [AvaloniaFact]
    public void Source32x32_InnerImage_MatchesOuterBorder()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        control.SetRgbaData(MakeRgba(32, 32), 32, 32);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(border.Width, image.Width);
        Assert.Equal(border.Height, image.Height);
    }

    /// <summary>
    /// THE bug reported on 2026-05-21: three different source sizes must NOT
    /// produce three different inner-Image sizes in the same preview slot.
    /// Creating three identically-configured controls and feeding each a
    /// different animType source, the resulting inner-Image sizes must all
    /// match each other.
    /// </summary>
    [AvaloniaFact]
    public void AllAnimTypes_RenderAtSameVisualSize()
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

        Assert.Equal(i0.Width, i1.Width);
        Assert.Equal(i0.Width, i2.Width);
        Assert.Equal(i0.Height, i1.Height);
        Assert.Equal(i0.Height, i2.Height);
    }

    // ----------------------------------------------------------------------
    // Default render settings (WinForms parity)
    // ----------------------------------------------------------------------

    /// <summary>WinForms ships <c>PictureBoxSizeMode.StretchImage</c>; Avalonia equivalent is <c>Stretch.Fill</c>.</summary>
    [AvaloniaFact]
    public void DefaultStretchMode_IsFill()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(Stretch.Fill, image.Stretch);
    }

    /// <summary>WinForms ships <c>InterpolationMode.Bicubic</c>; Avalonia equivalent is <c>BitmapInterpolationMode.HighQuality</c>.</summary>
    [AvaloniaFact]
    public void DefaultInterpolationMode_IsHighQuality()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(BitmapInterpolationMode.HighQuality,
            RenderOptions.GetBitmapInterpolationMode(image));
    }

    // ----------------------------------------------------------------------
    // Runtime property changes — Stretch + BitmapInterpolationMode
    // ----------------------------------------------------------------------

    /// <summary>
    /// The new <c>Stretch</c> styled property must apply to the inner Image
    /// both BEFORE and AFTER a bitmap is loaded.
    /// </summary>
    [AvaloniaFact]
    public void Stretch_RuntimeChange_AppliesBeforeAndAfterLoad()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;

        // Before load — change Stretch
        control.Stretch = Stretch.Uniform;
        Assert.Equal(Stretch.Uniform, image.Stretch);

        // Load a bitmap, then change again
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        control.Stretch = Stretch.UniformToFill;
        Assert.Equal(Stretch.UniformToFill, image.Stretch);

        // Back to default
        control.Stretch = Stretch.Fill;
        Assert.Equal(Stretch.Fill, image.Stretch);
    }

    /// <summary>
    /// The new <c>BitmapInterpolationMode</c> styled property must apply to
    /// the inner Image both BEFORE and AFTER a bitmap is loaded.
    /// </summary>
    [AvaloniaFact]
    public void BitmapInterpolationMode_RuntimeChange_AppliesBeforeAndAfterLoad()
    {
        var control = new IconPreviewControl();
        using var host = HostInWindow(control);

        var image = control.FindControl<Image>("ImageDisplay")!;

        // Before load — change interpolation
        control.BitmapInterpolationMode = BitmapInterpolationMode.None;
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(image));

        // Load a bitmap, then change again
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();
        control.BitmapInterpolationMode = BitmapInterpolationMode.LowQuality;
        Assert.Equal(BitmapInterpolationMode.LowQuality, RenderOptions.GetBitmapInterpolationMode(image));

        // Back to default
        control.BitmapInterpolationMode = BitmapInterpolationMode.HighQuality;
        Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));
    }

    // ----------------------------------------------------------------------
    // Stale-state regression: null then load different size still fills
    // ----------------------------------------------------------------------

    /// <summary>
    /// Setting a bitmap, clearing via <c>SetImage(null)</c>, then loading a
    /// DIFFERENT-sized bitmap must still produce an inner Image that fills
    /// the outer Border. Guards against stale Width/Height left from the
    /// previous SetImage call.
    /// </summary>
    [AvaloniaFact]
    public void SetImage_Null_ThenLoadDifferentSize_StillFillsBox()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);

        // Load 16x16 first
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        // Clear
        control.SetImage(null);
        Assert.False(control.HasImage);

        // Load a DIFFERENT size (16x24)
        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(border.Width, image.Width);
        Assert.Equal(border.Height, image.Height);
    }

    // ----------------------------------------------------------------------
    // Scale/Max property changes after load — outer + inner stay locked
    // ----------------------------------------------------------------------

    /// <summary>
    /// After loading a bitmap, changing Scale must update BOTH the outer
    /// Border AND the inner Image — they stay locked at the same dimensions.
    /// </summary>
    [AvaloniaFact]
    public void ScaleChange_AfterLoad_OuterAndInner_StayLocked()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);
        control.SetRgbaData(MakeRgba(16, 16), 16, 16);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;
        Assert.Equal(64, border.Width);
        Assert.Equal(64, image.Width);

        control.Scale = 3;

        Assert.Equal(96, border.Width);
        Assert.Equal(96, border.Height);
        Assert.Equal(border.Width, image.Width);
        Assert.Equal(border.Height, image.Height);
    }

    /// <summary>
    /// After loading a bitmap, changing MaxImageWidth/Height must update
    /// BOTH the outer Border AND the inner Image.
    /// </summary>
    [AvaloniaFact]
    public void MaxImageChange_AfterLoad_OuterAndInner_StayLocked()
    {
        var control = new IconPreviewControl { Scale = 2, MaxImageWidth = 32, MaxImageHeight = 32 };
        using var host = HostInWindow(control);
        control.SetRgbaData(MakeRgba(16, 24), 16, 24);
        host.Window.UpdateLayout();

        var border = control.FindControl<Border>("OuterBorder")!;
        var image = control.FindControl<Image>("ImageDisplay")!;

        control.MaxImageWidth = 16;
        Assert.Equal(32, border.Width);
        Assert.Equal(border.Width, image.Width);

        control.MaxImageHeight = 40;
        Assert.Equal(80, border.Height);
        Assert.Equal(border.Height, image.Height);
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
    // AddressListControl item template — same WinForms-equivalent fix.
    // The address list itself renders class wait icons of different animTypes
    // (16x16, 16x24, 32x32) inside a fixed 32x32 Image slot. Before the #342
    // follow-up the Image used Stretch="Uniform" which left smaller icons
    // smaller — visible as the user's "different sizes" complaint. The fix
    // is to switch the template Image to Stretch="Fill" to match WinForms
    // PictureBoxSizeMode.StretchImage semantics.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Parse the AddressListControl XAML and assert the icon Image uses
    /// Stretch="Fill" (the WinForms-equivalent fix in the #342 follow-up).
    /// </summary>
    [AvaloniaFact]
    public void AddressListControl_IconImage_UsesFillStretch()
    {
        // Walk up from the tests bin/Release/net9.0 cwd to the repo root,
        // then read the source XAML and assert the inline icon template uses
        // Stretch="Fill" rather than Stretch="Uniform".
        var dir = System.IO.Directory.GetCurrentDirectory();
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(
                   dir, "FEBuilderGBA.Avalonia", "Controls", "AddressListControl.axaml")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);

        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
            dir!, "FEBuilderGBA.Avalonia", "Controls", "AddressListControl.axaml"));

        // Match WinForms PictureBoxSizeMode.StretchImage semantics.
        Assert.Contains("Stretch=\"Fill\"", xaml);
        // Ensure the previous (PR #351 era) Stretch="Uniform" isn't lingering
        // on the icon Image. Allow Uniform elsewhere if any future StackPanel
        // alignment uses it — but the icon Image must be Fill.
        var iconBlockStart = xaml.IndexOf("Source=\"{Binding Icon}\"");
        Assert.True(iconBlockStart >= 0, "Icon Image template not found in XAML");
        // Look forward 200 chars from "Source={Binding Icon}" for Stretch attribute.
        var window = xaml.Substring(iconBlockStart, System.Math.Min(300, xaml.Length - iconBlockStart));
        Assert.Contains("Stretch=\"Fill\"", window);
        Assert.DoesNotContain("Stretch=\"Uniform\"", window);
    }
}
