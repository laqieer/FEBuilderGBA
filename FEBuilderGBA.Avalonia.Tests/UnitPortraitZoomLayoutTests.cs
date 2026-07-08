// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class UnitPortraitZoomLayoutTests
    {
        const int PortraitWidth = 128;
        const int PortraitHeight = 112;
        // #1912: the portrait viewport now grows with zoom up to this cap (was 160/176),
        // so a portrait is fully visible at 2x before the internal scroll takes over.
        const double ViewportMaxWidth = 256;
        const double ViewportMaxHeight = 272;

        [AvaloniaFact]
        public void UnitEditor_PortraitZoom_IsBoundedAndScrollable()
        {
            var view = new UnitEditorView();
            AssertPortraitZoomIsBoundedAndScrollable(view);
        }

        [AvaloniaFact]
        public void UnitFE6_PortraitZoom_IsBoundedAndScrollable()
        {
            var view = new UnitFE6View();
            AssertPortraitZoomIsBoundedAndScrollable(view);
        }

        static void AssertPortraitZoomIsBoundedAndScrollable(Control view)
        {
            var viewport = view.FindControl<Border>("PortraitViewport");
            var portrait = view.FindControl<GbaImageControl>("PortraitImage");
            Assert.NotNull(viewport);
            Assert.NotNull(portrait);

            var scroller = portrait!.FindControl<ScrollViewer>("ImageScroller");
            var image = portrait.FindControl<Image>("ImageDisplay");
            Assert.NotNull(scroller);
            Assert.NotNull(image);

            portrait.SetRgbaData(MakePortraitRgba(), PortraitWidth, PortraitHeight);
            portrait.Zoom = 1;

            var host = view as Window ?? new Window { Content = view };
            host.Show();
            try
            {
                host.UpdateLayout();
                view.UpdateLayout();

                Assert.Equal(ViewportMaxWidth, viewport!.MaxWidth);
                Assert.Equal(ViewportMaxHeight, viewport.MaxHeight);
                Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    scroller!.HorizontalScrollBarVisibility);
                Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    scroller.VerticalScrollBarVisibility);
                Assert.True(viewport.Bounds.Width <= ViewportMaxWidth + 0.5,
                    $"1x portrait viewport should stay within {ViewportMaxWidth}px; actual {viewport.Bounds.Width:F1}");
                Assert.True(viewport.Bounds.Height <= ViewportMaxHeight + 0.5,
                    $"1x portrait viewport should stay within {ViewportMaxHeight}px; actual {viewport.Bounds.Height:F1}");
                Assert.True(scroller.Bounds.Width >= PortraitWidth,
                    "1x full portrait sheet should fit horizontally without clipping.");
                Assert.True(scroller.Bounds.Height >= PortraitHeight,
                    "1x full portrait sheet should fit vertically without clipping.");

                // #1912: at 2x the enlarged portrait now fits fully WITHOUT scrolling —
                // the viewport grows past the old 160/176 cap (up to the new cap) instead
                // of panning a tiny window.
                portrait.Zoom = 2;
                host.UpdateLayout();
                view.UpdateLayout();
                Assert.True(viewport.Bounds.Width > 160,
                    $"2x portrait viewport should grow past the old 160px cap; actual {viewport.Bounds.Width:F1}");
                Assert.True(scroller.Bounds.Width + 0.5 >= image!.Width,
                    $"2x portrait should fit horizontally without scrolling; scroller {scroller.Bounds.Width:F1} vs image {image.Width:F1}");
                Assert.True(scroller.Bounds.Height + 0.5 >= image.Height,
                    $"2x portrait should fit vertically without scrolling; scroller {scroller.Bounds.Height:F1} vs image {image.Height:F1}");

                portrait.Zoom = GbaImageControl.ZoomMax;
                host.UpdateLayout();
                view.UpdateLayout();

                Assert.Equal(PortraitWidth * GbaImageControl.ZoomMax, image!.Width);
                Assert.Equal(PortraitHeight * GbaImageControl.ZoomMax, image.Height);
                Assert.True(viewport.Bounds.Width <= ViewportMaxWidth + 0.5,
                    $"zoomed portrait viewport should not expand the Unit editor auto column; actual {viewport.Bounds.Width:F1}");
                Assert.True(viewport.Bounds.Height <= ViewportMaxHeight + 0.5,
                    $"zoomed portrait viewport should not expand the Unit editor auto column; actual {viewport.Bounds.Height:F1}");
                Assert.True(scroller.Bounds.Width < image.Width,
                    "zoomed portrait must overflow horizontally inside the internal ScrollViewer.");
                Assert.True(scroller.Bounds.Height < image.Height,
                    "zoomed portrait must overflow vertically inside the internal ScrollViewer.");
            }
            finally
            {
                host.Close();
            }
        }

        static byte[] MakePortraitRgba()
        {
            byte[] rgba = new byte[PortraitWidth * PortraitHeight * 4];
            for (int y = 0; y < PortraitHeight; y++)
            {
                for (int x = 0; x < PortraitWidth; x++)
                {
                    int i = (y * PortraitWidth + x) * 4;
                    rgba[i + 0] = (byte)(x * 255 / (PortraitWidth - 1));
                    rgba[i + 1] = (byte)(y * 255 / (PortraitHeight - 1));
                    rgba[i + 2] = 0x80;
                    rgba[i + 3] = 0xFF;
                }
            }
            return rgba;
        }
    }
}
