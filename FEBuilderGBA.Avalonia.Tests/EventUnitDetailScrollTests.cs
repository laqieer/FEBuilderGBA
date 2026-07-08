// SPDX-License-Identifier: GPL-3.0-or-later
// #1913 — the Event Unit Placement detail panel was a vertical-only ScrollViewer,
// so wide content (the Unit-Info "Growth Rate" combo row, and the FE8 1094px
// After-Coordinate move-path grid) was clipped off the right edge with no way to
// reach it. The fix sets HorizontalScrollBarVisibility="Auto" on the detail
// ScrollViewer in all three EventUnit views. These headless tests assert the
// realized property (not a source string) and, for FE8, that horizontal scrolling
// actually engages (Extent.Width > Viewport.Width) at a narrow width.
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventUnitDetailScrollTests
    {
        static ScrollViewer GetDetailScroll(Control v)
        {
            var sv = v.FindControl<ScrollViewer>("DetailScrollViewer");
            Assert.NotNull(sv);
            return sv!;
        }

        [AvaloniaFact]
        public void EventUnitView_FE8_DetailScroll_AllowsHorizontalScroll()
        {
            var v = new EventUnitView();
            var sv = GetDetailScroll(v);
            Assert.Equal(ScrollBarVisibility.Auto, sv.HorizontalScrollBarVisibility);

            // The FE8 move-path panel (fixed 1094px grid) is gated hidden until a FE8
            // ROM loads. Force it visible so the detail content is genuinely wide.
            var panel = v.FindControl<Control>("AfterCoordsPanel");
            Assert.NotNull(panel);
            panel!.IsVisible = true;

            // Narrow window: the 220+220+260 list columns leave the detail column far
            // narrower than the 1094px content, so horizontal scrolling must engage.
            v.Width = 900;
            v.Height = 800;
            v.Show();
            try
            {
                v.UpdateLayout();
                Assert.True(
                    sv.Extent.Width > sv.Viewport.Width,
                    $"detail content ({sv.Extent.Width:F0}px) should overflow the viewport " +
                    $"({sv.Viewport.Width:F0}px) so the horizontal scrollbar engages (#1913)");
            }
            finally
            {
                v.Close();
            }
        }

        [AvaloniaFact]
        public void EventUnitFE7View_DetailScroll_HorizontalAuto()
        {
            var v = new EventUnitFE7View();
            var sv = GetDetailScroll(v);
            Assert.Equal(ScrollBarVisibility.Auto, sv.HorizontalScrollBarVisibility);
        }

        [AvaloniaFact]
        public void EventUnitFE6View_DetailScroll_HorizontalAuto()
        {
            var v = new EventUnitFE6View();
            var sv = GetDetailScroll(v);
            Assert.Equal(ScrollBarVisibility.Auto, sv.HorizontalScrollBarVisibility);
        }
    }
}
