using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for GbaImageControl.
/// These tests instantiate real Avalonia controls and verify rendering behavior.
/// The Stretch="None" -> Stretch="Fill" bug (issue #183) would have been caught by these tests.
/// </summary>
public class GbaImageControlTests
{
    /// <summary>Create a solid red RGBA test image of the specified dimensions.</summary>
    static byte[] MakeTestImage(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = 255; // R
            rgba[i * 4 + 1] = 0;   // G
            rgba[i * 4 + 2] = 0;   // B
            rgba[i * 4 + 3] = 255; // A
        }
        return rgba;
    }

    [AvaloniaFact]
    public void ImageDisplay_UsesFillStretch()
    {
        // This test catches the Stretch="None" bug.
        // With Stretch="None", zoom changes the canvas but not the rendered image.
        // With Stretch="Fill", the bitmap scales to fill Width/Height.
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(Stretch.Fill, imageDisplay!.Stretch);
    }

    [AvaloniaFact]
    public void ImageDisplay_UsesNearestNeighborInterpolation()
    {
        // GBA pixel art must use nearest-neighbor to stay crisp
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);
        Assert.Equal(global::Avalonia.Media.Imaging.BitmapInterpolationMode.None,
            RenderOptions.GetBitmapInterpolationMode(imageDisplay!));
    }

    [AvaloniaFact]
    public void DefaultZoom_IsOne()
    {
        var control = new GbaImageControl();
        Assert.Equal(1, control.Zoom);
    }

    [AvaloniaFact]
    public void ZoomRange_IsOneToEight()
    {
        Assert.Equal(1, GbaImageControl.ZoomMin);
        Assert.Equal(8, GbaImageControl.ZoomMax);
    }

    [AvaloniaFact]
    public void SetRgbaData_UpdatesImageDimensions()
    {
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        byte[] rgba = MakeTestImage(16, 16);
        control.SetRgbaData(rgba, 16, 16);

        // At zoom 1, dimensions should match bitmap size
        Assert.Equal(16, imageDisplay!.Width);
        Assert.Equal(16, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void ZoomIn_ScalesImageDimensions()
    {
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        byte[] rgba = MakeTestImage(16, 16);
        control.SetRgbaData(rgba, 16, 16);

        // Zoom to 2x
        control.Zoom = 2;
        Assert.Equal(32, imageDisplay!.Width);
        Assert.Equal(32, imageDisplay.Height);

        // Zoom to 4x
        control.Zoom = 4;
        Assert.Equal(64, imageDisplay.Width);
        Assert.Equal(64, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void ZoomOut_ScalesImageDimensions()
    {
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        byte[] rgba = MakeTestImage(32, 32);
        control.SetRgbaData(rgba, 32, 32);

        control.Zoom = 3;
        Assert.Equal(96, imageDisplay!.Width);
        Assert.Equal(96, imageDisplay.Height);

        control.Zoom = 1;
        Assert.Equal(32, imageDisplay.Width);
        Assert.Equal(32, imageDisplay.Height);
    }

    [AvaloniaFact]
    public void ZoomLabel_UpdatesOnZoomChange()
    {
        var control = new GbaImageControl();
        var zoomLabel = control.FindControl<TextBlock>("ZoomLabel");
        Assert.NotNull(zoomLabel);

        Assert.Equal("1x", zoomLabel!.Text);

        control.Zoom = 3;
        Assert.Equal("3x", zoomLabel.Text);

        control.Zoom = 1;
        Assert.Equal("1x", zoomLabel.Text);
    }

    [AvaloniaFact]
    public void ZoomClamp_RespectsMinMax()
    {
        var control = new GbaImageControl();

        control.Zoom = 0; // Below min
        Assert.Equal(GbaImageControl.ZoomMin, control.Zoom);

        control.Zoom = 100; // Above max
        Assert.Equal(GbaImageControl.ZoomMax, control.Zoom);
    }

    [AvaloniaFact]
    public void ZoomButtons_Exist()
    {
        var control = new GbaImageControl();
        Assert.NotNull(control.FindControl<Button>("ZoomInButton"));
        Assert.NotNull(control.FindControl<Button>("ZoomOutButton"));
        Assert.NotNull(control.FindControl<Button>("ZoomResetButton"));
    }

    [AvaloniaFact]
    public void SetImage_Null_ClearsDisplay()
    {
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");

        // Load then clear
        byte[] rgba = MakeTestImage(8, 8);
        control.SetRgbaData(rgba, 8, 8);
        Assert.NotNull(imageDisplay!.Source);

        control.SetImage(null);
        Assert.Null(imageDisplay.Source);
    }

    [AvaloniaFact]
    public void LargeImage_ZoomScalesCorrectly()
    {
        // Test with a portrait-sized image (128x112 is common for GBA portraits)
        var control = new GbaImageControl();
        var imageDisplay = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(imageDisplay);

        byte[] rgba = MakeTestImage(128, 112);
        control.SetRgbaData(rgba, 128, 112);

        control.Zoom = 2;
        Assert.Equal(256, imageDisplay!.Width);
        Assert.Equal(224, imageDisplay.Height);
    }
}
