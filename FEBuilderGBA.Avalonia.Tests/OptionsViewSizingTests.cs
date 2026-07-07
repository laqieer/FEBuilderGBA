using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1781 regression guard: the Options window (Tool → Options) declared
    /// <c>SizeToContent="Height"</c> together with <c>CanResize="False"</c> and no
    /// height cap, so on a modest/scaled display the window grew past the bottom of
    /// the screen and the OK/Cancel row was unreachable with no way to resize.
    /// The fix pins a screen-safe fixed height and makes the window resizable; the
    /// per-tab ScrollViewers absorb any content overflow.
    /// </summary>
    public class OptionsViewSizingTests
    {
        [AvaloniaFact]
        public void OptionsView_DoesNotAutoGrowHeight_AndIsResizable()
        {
            var v = new OptionsView();

            // Must NOT auto-size its height — that is exactly what grew the window
            // off the bottom of the screen in #1781.
            Assert.NotEqual(SizeToContent.Height, v.Descriptor.SizeToContent);
            Assert.NotEqual(SizeToContent.WidthAndHeight, v.Descriptor.SizeToContent);

            // Must be resizable so a small/high-DPI display can always reach OK/Cancel.
            Assert.True(v.Descriptor.CanResize, "Options window must be resizable so OK/Cancel stay reachable.");

            // The fixed height must be screen-safe (fits a 720p display with a taskbar).
            Assert.True(v.Descriptor.PreferredHeight > 0 && v.Descriptor.PreferredHeight <= 640,
                $"Options window Height ({v.Descriptor.PreferredHeight}) should be a screen-safe fixed height, not unbounded.");
        }
    }
}
