using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Controls;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Tests for the #1726 wheel/pinch zoom fix in <see cref="GbaImageControl"/>:
/// plain wheel / macOS two-finger scroll must PAN (let the inner ScrollViewer
/// scroll), while Ctrl/⌘+wheel and pinch ZOOM. The wheel handler is registered
/// on the TUNNEL phase so it runs before the ScrollViewer even when the image is
/// zoomed-in / scrollable.
/// </summary>
public class GbaImageControlWheelZoomTests
{
    // ---- Pure helper: ShouldWheelZoom (modifier gating) ----

    [Theory]
    [InlineData(KeyModifiers.Control, true)]
    [InlineData(KeyModifiers.Meta, true)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, true)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta, true)]
    [InlineData(KeyModifiers.None, false)]
    [InlineData(KeyModifiers.Shift, false)]
    [InlineData(KeyModifiers.Alt, false)]
    public void ShouldWheelZoom_OnlyWithControlOrMeta(KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, GbaImageControl.ShouldWheelZoom(modifiers));
    }

    // ---- Pure helper: PinchScaleToZoom (cumulative scale -> clamped int zoom) ----

    [Theory]
    [InlineData(1, 2.0, 2)]    // 1 * 2.0 = 2
    [InlineData(4, 0.5, 2)]    // 4 * 0.5 = 2
    [InlineData(2, 2.0, 4)]    // 2 * 2.0 = 4
    [InlineData(8, 2.0, 8)]    // 16 -> clamp to max 8
    [InlineData(1, 0.1, 1)]    // 0.1 -> round 0 -> clamp to min 1
    [InlineData(1, 1.0, 1)]    // identity
    [InlineData(2, 1.49, 3)]   // 2.98 -> round 3
    [InlineData(2, 1.50, 3)]   // 3.00 -> 3
    [InlineData(3, 0.5, 2)]    // 1.5 -> round away-from-zero -> 2
    public void PinchScaleToZoom_RoundsAndClamps(int baseZoom, double scale, int expected)
    {
        Assert.Equal(expected, GbaImageControl.PinchScaleToZoom(baseZoom, scale));
    }

    [Fact]
    public void PinchScaleToZoom_AlwaysWithinZoomRange()
    {
        for (int b = GbaImageControl.ZoomMin; b <= GbaImageControl.ZoomMax; b++)
        {
            foreach (double s in new[] { 0.01, 0.5, 1.0, 1.7, 4.0, 100.0 })
            {
                int z = GbaImageControl.PinchScaleToZoom(b, s);
                Assert.InRange(z, GbaImageControl.ZoomMin, GbaImageControl.ZoomMax);
            }
        }
    }

    // ---- Structural: pinch gesture is wired up ----

    [AvaloniaFact]
    public void Constructor_RegistersPinchGestureRecognizer()
    {
        var control = new GbaImageControl();
        var scroller = control.FindControl<ScrollViewer>("ImageScroller");
        Assert.NotNull(scroller);

        bool hasPinch = false;
        foreach (var gr in scroller!.GestureRecognizers)
        {
            if (gr is PinchGestureRecognizer) { hasPinch = true; break; }
        }
        Assert.True(hasPinch, "ImageScroller should have a PinchGestureRecognizer for #1726 pinch-to-zoom.");
    }

    // ---- Routed-event smoke test: tunnel registration, non-extreme scroll state ----

    static PointerWheelEventArgs MakeWheelArgs(Visual source, Visual root, KeyModifiers modifiers, double deltaY)
    {
        var pointer = new Pointer(0, PointerType.Mouse, isPrimary: true);
        var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
        return new PointerWheelEventArgs(
            source, pointer, root, new Point(40, 40), 0UL, props, modifiers, new Vector(0, deltaY))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
        };
    }

    /// <summary>
    /// Deterministic logic test: the wheel handler must zoom only with Ctrl/⌘ and
    /// must leave a plain wheel UNHANDLED so the ScrollViewer can pan it (#1726).
    /// </summary>
    [AvaloniaFact]
    public void OnPointerWheelChanged_GatesZoomAndLeavesPlainWheelUnhandled()
    {
        var control = new GbaImageControl();
        var image = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(image);

        byte[] rgba = new byte[64 * 64 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = 0xFF;
        control.SetRgbaData(rgba, 64, 64);

        const int VP = 120;
        control.Measure(new Size(VP, VP));
        control.Arrange(new Rect(0, 0, VP, VP));

        // Ctrl+wheel up: zooms in (4 -> 5) and consumes the event.
        control.Zoom = 4;
        var ctrlUp = MakeWheelArgs(image!, control, KeyModifiers.Control, deltaY: 1);
        control.OnPointerWheelChanged(image, ctrlUp);
        Assert.Equal(5, control.Zoom);
        Assert.True(ctrlUp.Handled, "modifier-wheel zoom must mark the event handled (suppresses scroll)");

        // Meta(⌘)+wheel down: zooms out (5 -> 4).
        var metaDown = MakeWheelArgs(image!, control, KeyModifiers.Meta, deltaY: -1);
        control.OnPointerWheelChanged(image, metaDown);
        Assert.Equal(4, control.Zoom);
        Assert.True(metaDown.Handled);

        // Plain wheel up: does NOT zoom and is left UNHANDLED so the ScrollViewer pans.
        var plainUp = MakeWheelArgs(image!, control, KeyModifiers.None, deltaY: 1);
        control.OnPointerWheelChanged(image, plainUp);
        Assert.Equal(4, control.Zoom);
        Assert.False(plainUp.Handled, "plain wheel must stay unhandled so the ScrollViewer pans");

        // Shift+wheel (horizontal scroll intent) also must not zoom.
        var shiftUp = MakeWheelArgs(image!, control, KeyModifiers.Shift, deltaY: 1);
        control.OnPointerWheelChanged(image, shiftUp);
        Assert.Equal(4, control.Zoom);
        Assert.False(shiftUp.Handled);
    }

    [AvaloniaFact]
    public void OnPointerWheelChanged_RespectsZoomClamp()
    {
        var control = new GbaImageControl();
        var image = control.FindControl<Image>("ImageDisplay");
        Assert.NotNull(image);

        byte[] rgba = new byte[16 * 16 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = 0xFF;
        control.SetRgbaData(rgba, 16, 16);
        control.Measure(new Size(120, 120));
        control.Arrange(new Rect(0, 0, 120, 120));

        // At max zoom, Ctrl+wheel up stays clamped (and still consumes the event).
        control.Zoom = GbaImageControl.ZoomMax;
        var up = MakeWheelArgs(image!, control, KeyModifiers.Control, deltaY: 1);
        control.OnPointerWheelChanged(image, up);
        Assert.Equal(GbaImageControl.ZoomMax, control.Zoom);
        Assert.True(up.Handled);

        // At min zoom, Ctrl+wheel down stays clamped (and still consumes the event).
        control.Zoom = GbaImageControl.ZoomMin;
        var down = MakeWheelArgs(image!, control, KeyModifiers.Control, deltaY: -1);
        control.OnPointerWheelChanged(image, down);
        Assert.Equal(GbaImageControl.ZoomMin, control.Zoom);
        Assert.True(down.Handled);
    }

    /// <summary>
    /// Routing proof for the #1726 fix: with the image zoomed-in (scrollable) and
    /// at a NON-EXTREME scroll offset, a Ctrl+wheel event RAISED ON THE INNER IMAGE
    /// must still change Zoom. This only works because the handler is registered on
    /// the TUNNEL phase (it runs before the ScrollViewer); a bubble-phase
    /// registration would let the ScrollViewer consume the wheel first and zoom
    /// would silently fail — the exact v1 defect the review board caught. The
    /// control is hosted in a headless window so the routed event traverses a real,
    /// rooted visual tree.
    /// </summary>
    [AvaloniaFact]
    public void CtrlWheel_RoutesThroughTunnel_EvenWhenScrollable()
    {
        var control = new GbaImageControl();
        var image = control.FindControl<Image>("ImageDisplay");
        var scroller = control.FindControl<ScrollViewer>("ImageScroller");
        Assert.NotNull(image);
        Assert.NotNull(scroller);

        byte[] rgba = new byte[64 * 64 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = 0xFF;
        control.SetRgbaData(rgba, 64, 64);
        control.Zoom = 4; // 256x256 content in a 120x120 viewport => scrollable

        var window = new Window { Width = 140, Height = 140, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            // Park at a non-extreme offset so the ScrollViewer COULD scroll the wheel
            // (the case where a bubble-phase handler would never see the event).
            scroller!.Offset = new Vector(30, 30);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            control.Zoom = 4;
            image!.RaiseEvent(MakeWheelArgs(image, control, KeyModifiers.Control, deltaY: 1));
            Assert.Equal(5, control.Zoom);

            control.Zoom = 4;
            image.RaiseEvent(MakeWheelArgs(image, control, KeyModifiers.None, deltaY: 1));
            Assert.Equal(4, control.Zoom);
        }
        finally
        {
            // Always close the window — if an assertion above throws, leaving it
            // open can leak visuals/dispatcher state and make later AvaloniaFact
            // tests flaky (Copilot review on PR #1738).
            window.Close();
        }
    }
}
