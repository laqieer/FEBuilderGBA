// SPDX-License-Identifier: GPL-3.0-or-later
// Layout/style regression coverage for Visual Map Editor button readability (#1760).
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapEditorButtonReadabilityTests
    {
        private const double EditorWidth = 1200;
        private const double EditorHeight = 800;
        private const double MinimumUsableMapHeight = 240;
        // #1998: a large synthetic map image so MapCanvasPanel's own inner ScrollViewer
        // (MapCanvasScroller, wrapping GbaImageControl) genuinely overflows its viewport
        // at zoom 1x, independently of the outer compact/desktop split under test. Note
        // this is distinct from GbaImageControl's *own* internal "ImageScroller": since
        // MapCanvasScroller gives GbaImageControl its full natural (unconstrained) size,
        // ImageScroller itself never overflows — MapCanvasScroller is the one genuinely
        // clipped to MapCanvasPanel's real (>=240px) Bounds.
        private const int SyntheticMapSize = 2000;

        [AvaloniaFact]
        public void DarkMapPanels_UseLocalDarkThemeScope()
        {
            var view = new MapEditorView();

            Assert.Equal(ThemeVariant.Dark, Required<ThemeVariantScope>(view, "TilePaletteThemeScope").RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, Required<ThemeVariantScope>(view, "TileEditorThemeScope").RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, Required<ThemeVariantScope>(view, "MapCanvasThemeScope").RequestedThemeVariant);
        }

        [AvaloniaFact]
        public void EmbeddedMapZoomControls_InheritReadableDarkTheme()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                view.UpdateLayout();

                var map = Required<GbaImageControl>(view, "MapImageControl");
                var zoomOut = Required<Button>(map, "ZoomOutButton");
                var zoomLabel = Required<TextBlock>(map, "ZoomLabel");
                var zoomIn = Required<Button>(map, "ZoomInButton");
                var zoomReset = Required<Button>(map, "ZoomResetButton");

                Assert.Equal(ThemeVariant.Dark, map.ActualThemeVariant);
                Assert.Equal(ThemeVariant.Dark, zoomOut.ActualThemeVariant);
                Assert.Equal(ThemeVariant.Dark, zoomLabel.ActualThemeVariant);
                Assert.Equal(ThemeVariant.Dark, zoomIn.ActualThemeVariant);
                Assert.Equal(ThemeVariant.Dark, zoomReset.ActualThemeVariant);
            }
            finally
            {
                view.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData("ExportCsvButton")]
        [InlineData("ImportCsvButton")]
        [InlineData("ExportTmxButton")]
        [InlineData("ImportTmxButton")]
        [InlineData("ResizeMapButton")]
        [InlineData("GenerateRandomMapButton")]
        [InlineData("WriteTileBtn")]
        [InlineData("RefreshMapBtn")]
        public void MapEditorButtons_KeepThemeBackgroundsAndDoNotHardCodeBrushes(string name)
        {
            var view = new MapEditorView();
            var button = Required<Button>(view, name);
            string buttonTag = FindButtonTag(name);

            Assert.NotNull(button);
            Assert.DoesNotContain("Foreground=", buttonTag);
            Assert.DoesNotContain("Background=", buttonTag);
        }

        [AvaloniaFact]
        public void CommandToolbar_FitsDeclaredEditorWidth()
        {
            var view = new MapEditorView();
            Assert.Equal(SizeToContent.Manual, view.Descriptor.SizeToContent);

            // #1998: MapEditorInfoPanel's toolbars now live inside MapUpperControlsScroller.
            // A ScrollViewer's content only realizes real (non-zero) Bounds once it is
            // attached to a real headless TopLevel/LayoutManager (via view.Show()) and
            // driven through UpdateLayout() — measuring/arranging a detached subtree
            // (e.g. just "MapEditorRootGrid") in isolation leaves the ScrollViewer's
            // content presenter at DesiredSize/Bounds (0,0), same as every other
            // #1998 test in this file that reads Bounds from inside the scroller.
            view.Show();
            try
            {
                ArrangeAt(view, EditorWidth, EditorHeight);

                var navToolbar = Required<StackPanel>(view, "MapNavigationToolbar");
                var csvToolbar = Required<StackPanel>(view, "MapCsvCommandToolbar");
                var tmxToolbar = Required<StackPanel>(view, "MapTmxCommandToolbar");
                var randomToolbar = Required<StackPanel>(view, "MapRandomCommandToolbar");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");

                double navWidth = navToolbar.Bounds.Width;
                double csvWidth = csvToolbar.Bounds.Width;
                double tmxWidth = tmxToolbar.Bounds.Width;
                double randomWidth = randomToolbar.Bounds.Width;

                // #1998 follow-up (review): compare against MapUpperControlsScroller's actual
                // VIEWPORT width — NOT MapEditorInfoPanel.Bounds.Width. Because
                // HorizontalScrollBarVisibility="Auto" gives the scroller's content its natural
                // (unconstrained) width, MapEditorInfoPanel.Bounds.Width is itself derived FROM
                // the widest toolbar row, so comparing a toolbar's width against it was close to
                // tautological (a child's width can never meaningfully exceed a parent whose own
                // width is defined as "the widest child"). ScrollViewer.Viewport.Width is the real,
                // visible, viewport-bounded area — the only direct measurement of whether a
                // toolbar row actually fits without horizontal scrolling at this arrangement.
                double availableViewportWidth = ArrangeAndGetUpperControlsViewportWidth(view, EditorWidth, EditorHeight);

                Assert.True(availableViewportWidth > 0,
                    $"MapUpperControlsScroller should report a real, non-zero viewport width at EditorWidth ({availableViewportWidth:F1}).");
                Assert.True(csvWidth > 0, "CSV command toolbar should have a real, non-zero measured width.");
                Assert.True(navWidth <= availableViewportWidth,
                    $"Navigation toolbar desired width ({navWidth:F1}) exceeds " +
                    $"the upper-controls scroller's actual viewport width ({availableViewportWidth:F1}).");
                Assert.True(csvWidth <= availableViewportWidth,
                    $"CSV command toolbar desired width ({csvWidth:F1}) exceeds " +
                    $"the upper-controls scroller's actual viewport width ({availableViewportWidth:F1}).");
                Assert.True(tmxWidth <= availableViewportWidth,
                    $"TMX command toolbar desired width ({tmxWidth:F1}) exceeds " +
                    $"the upper-controls scroller's actual viewport width ({availableViewportWidth:F1}).");
                Assert.True(randomWidth <= availableViewportWidth,
                    $"Random-map command toolbar desired width ({randomWidth:F1}) exceeds " +
                    $"the upper-controls scroller's actual viewport width ({availableViewportWidth:F1}).");

                // #1998: re-measure the toolbar rows at MinWidth (rather than reusing the
                // widths captured at EditorWidth above) so this compares widths from the
                // SAME arrangement pass as availableMinViewportWidth. Comparing widths from two
                // different arrangements would be invalid once ArrangeAt genuinely honors
                // the requested size, since these toolbars stretch to fill available width.
                double availableMinViewportWidth = ArrangeAndGetUpperControlsViewportWidth(
                    view,
                    view.Descriptor.MinWidth,
                    view.Descriptor.MinHeight);
                double navWidthAtMinWidth = navToolbar.Bounds.Width;
                double csvWidthAtMinWidth = csvToolbar.Bounds.Width;
                double tmxWidthAtMinWidth = tmxToolbar.Bounds.Width;
                double randomWidthAtMinWidth = randomToolbar.Bounds.Width;
                double widestSplitToolbarRowAtMinWidth =
                    new[] { navWidthAtMinWidth, csvWidthAtMinWidth, tmxWidthAtMinWidth, randomWidthAtMinWidth }.Max();

                Assert.True(availableMinViewportWidth > 0,
                    $"MapUpperControlsScroller should report a real, non-zero viewport width at MinWidth ({availableMinViewportWidth:F1}).");
                Assert.True(widestSplitToolbarRowAtMinWidth <= availableMinViewportWidth,
                    $"Widest split toolbar row at MinWidth ({widestSplitToolbarRowAtMinWidth:F1}) exceeds the MinWidth " +
                    $"upper-controls scroller's actual viewport width ({availableMinViewportWidth:F1}); manual resize could clip the toolbar.");
                Assert.True(view.Descriptor.MinHeight >= MinimumUsableMapHeight,
                    $"MapEditorView descriptor MinHeight ({view.Descriptor.MinHeight:F1}) should leave a usable map canvas area.");

                Assert.True(view.Descriptor.MinWidth < view.Descriptor.PreferredWidth,
                    "The hosted editor descriptor should preserve a smaller MinWidth than the preferred width.");

                ArrangeAt(view, view.Descriptor.MinWidth, view.Descriptor.MinHeight);

                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Map canvas height at MinHeight ({mapCanvas.Bounds.Height:F1}) should remain usable.");
            }
            finally
            {
                view.Close();
            }
        }

        // ------------------------------------------------------------------
        // #1998: compact browser-viewport split — upper controls scroll,
        // MapCanvasPanel stays pinned + independently scrollable, >=240px.
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public void CompactViewport_UpperControlsOverflowAndScroll_MapCanvasStaysPinnedAndUsable()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                var map = Required<GbaImageControl>(view, "MapImageControl");
                map.SetRgbaData(MakeSyntheticMapRgba(SyntheticMapSize, SyntheticMapSize), SyntheticMapSize, SyntheticMapSize);

                double naturalControlsHeight = MeasureNaturalUpperControlsHeight(view);
                // Deliberately shorter than (naturalControlsHeight + canvas minimum) so the
                // upper region MUST overflow and scroll rather than pushing/squeezing the
                // pinned map canvas.
                double compactHeight = naturalControlsHeight + MinimumUsableMapHeight - 40;
                ArrangeAt(view, EditorWidth, compactHeight);

                var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");
                var innerMapScroller = Required<ScrollViewer>(view, "MapCanvasScroller");

                Assert.True(upperScroller.Extent.Height > upperScroller.Viewport.Height,
                    $"Upper controls scroller should genuinely overflow when compact " +
                    $"(Extent={upperScroller.Extent.Height:F1}, Viewport={upperScroller.Viewport.Height:F1}).");
                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Map canvas must stay >= {MinimumUsableMapHeight}px even when the upper controls overflow " +
                    $"(actual={mapCanvas.Bounds.Height:F1}).");
                Assert.True(innerMapScroller.Extent.Height > innerMapScroller.Viewport.Height,
                    "MapCanvasPanel's own inner ScrollViewer must independently overflow with a large synthetic map " +
                    $"(Extent={innerMapScroller.Extent.Height:F1}, Viewport={innerMapScroller.Viewport.Height:F1}).");
            }
            finally
            {
                view.Close();
            }
        }

        [AvaloniaFact]
        public void CompactViewport_ScrollingUpperControls_DoesNotMoveOrCollapseMapCanvas()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                var map = Required<GbaImageControl>(view, "MapImageControl");
                map.SetRgbaData(MakeSyntheticMapRgba(SyntheticMapSize, SyntheticMapSize), SyntheticMapSize, SyntheticMapSize);

                double naturalControlsHeight = MeasureNaturalUpperControlsHeight(view);
                double compactHeight = naturalControlsHeight + MinimumUsableMapHeight - 40;
                ArrangeAt(view, EditorWidth, compactHeight);

                var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");

                Rect boundsBefore = mapCanvas.Bounds;
                Assert.True(upperScroller.Extent.Height > upperScroller.Viewport.Height,
                    "Precondition: upper controls scroller must have real overflow to scroll.");

                upperScroller.Offset = new Vector(0, upperScroller.Extent.Height - upperScroller.Viewport.Height);
                view.UpdateLayout();

                Assert.Equal(boundsBefore.Height, mapCanvas.Bounds.Height, precision: 1);
                Assert.Equal(boundsBefore.Y, mapCanvas.Bounds.Y, precision: 1);
                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Map canvas collapsed below {MinimumUsableMapHeight}px after scrolling the upper controls " +
                    $"(actual={mapCanvas.Bounds.Height:F1}).");
            }
            finally
            {
                view.Close();
            }
        }

        [AvaloniaFact]
        public void DesktopViewport_UpperControlsDoNotOverflow_NaturalLayoutPreserved()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                ArrangeAt(view, EditorWidth, EditorHeight);

                var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");

                Assert.True(upperScroller.Extent.Height <= upperScroller.Viewport.Height + 0.5,
                    $"At the normal 1200x800 desktop size the upper controls should fit without scrolling " +
                    $"(Extent={upperScroller.Extent.Height:F1}, Viewport={upperScroller.Viewport.Height:F1}).");
                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Map canvas should remain usable at desktop size (actual={mapCanvas.Bounds.Height:F1}).");
            }
            finally
            {
                view.Close();
            }
        }

        [AvaloniaFact]
        public void CompactDesktopCompact_ConvergesWithoutStaleMaxHeight()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                var map = Required<GbaImageControl>(view, "MapImageControl");
                map.SetRgbaData(MakeSyntheticMapRgba(SyntheticMapSize, SyntheticMapSize), SyntheticMapSize, SyntheticMapSize);

                double naturalControlsHeight = MeasureNaturalUpperControlsHeight(view);
                double compactHeight = naturalControlsHeight + MinimumUsableMapHeight - 40;

                var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");

                // Compact #1: overflow expected.
                ArrangeAt(view, EditorWidth, compactHeight);
                Assert.True(upperScroller.Extent.Height > upperScroller.Viewport.Height,
                    "Compact #1 should overflow the upper controls scroller.");
                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Compact #1 map canvas must stay usable (actual={mapCanvas.Bounds.Height:F1}).");

                // Desktop: no overflow, natural layout restored — no stale cap left over.
                ArrangeAt(view, EditorWidth, EditorHeight);
                Assert.True(upperScroller.Extent.Height <= upperScroller.Viewport.Height + 0.5,
                    $"Resizing back to desktop should clear the compact overflow " +
                    $"(Extent={upperScroller.Extent.Height:F1}, Viewport={upperScroller.Viewport.Height:F1}).");

                // Compact #2: must re-overflow identically — no drift/leak from the first compaction.
                ArrangeAt(view, EditorWidth, compactHeight);
                Assert.True(upperScroller.Extent.Height > upperScroller.Viewport.Height,
                    "Compact #2 should overflow the upper controls scroller again, matching compact #1.");
                Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                    $"Compact #2 map canvas must stay usable (actual={mapCanvas.Bounds.Height:F1}).");
            }
            finally
            {
                view.Close();
            }
        }

        // Actual compact browser smoke viewport (600x500, per FEBuilderGBA.Browser/tests/smoke/smoke.mjs
        // SMOKE_VIEWPORT_WIDTH/HEIGHT defaults) — distinct from the wide EditorWidth=1200 used by the
        // height-only compact tests above. At this genuinely narrow width the upper controls toolbar rows
        // overflow horizontally (measured natural extent ~910px vs a 342px viewport), which the pre-existing
        // 1200-wide compact tests could never exercise. This proves the horizontal axis of
        // MapUpperControlsScroller's independent Auto scroll — not just the vertical axis — while the
        // pinned MapCanvasPanel stays unaffected and usable.
        private const double CompactBrowserWidth = 600;
        private const double CompactBrowserHeight = 500;

        [AvaloniaFact]
        public void CompactWidth600_UpperControlsOverflowHorizontallyAndScroll_MapCanvasStaysPinnedAndUsable()
        {
            var view = new MapEditorView();
            view.Show();
            try
            {
                var map = Required<GbaImageControl>(view, "MapImageControl");
                map.SetRgbaData(MakeSyntheticMapRgba(SyntheticMapSize, SyntheticMapSize), SyntheticMapSize, SyntheticMapSize);
                ArrangeAt(view, CompactBrowserWidth, CompactBrowserHeight);

                var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
                var mapCanvas = Required<Border>(view, "MapCanvasPanel");

                // Genuine horizontal overflow at the real 600px compact width — not a synthetic prop.
                Assert.True(upperScroller.Extent.Width > upperScroller.Viewport.Width,
                    $"Expected upper controls to overflow horizontally at {CompactBrowserWidth}px width " +
                    $"(Extent.Width={upperScroller.Extent.Width:F1} should exceed Viewport.Width={upperScroller.Viewport.Width:F1}).");

                double horizontalScrollRange = upperScroller.Extent.Width - upperScroller.Viewport.Width;
                Assert.True(horizontalScrollRange > 0,
                    $"Expected a positive horizontal scroll range at {CompactBrowserWidth}px width (was {horizontalScrollRange:F1}).");

                var mapCanvasBoundsBefore = mapCanvas.Bounds;
                Assert.True(mapCanvasBoundsBefore.Height >= MinimumUsableMapHeight,
                    $"Expected MapCanvasPanel height >= {MinimumUsableMapHeight} at {CompactBrowserWidth}px width (was {mapCanvasBoundsBefore.Height:F1}).");

                // Preserve independent vertical scroll behavior at this same narrow width (the axis the
                // pre-existing 1200-wide compact tests already cover) — the two axes must not interfere.
                Assert.True(upperScroller.Extent.Height > upperScroller.Viewport.Height,
                    $"Expected upper controls to also overflow vertically at {CompactBrowserHeight}px height " +
                    $"(Extent.Height={upperScroller.Extent.Height:F1} should exceed Viewport.Height={upperScroller.Viewport.Height:F1}).");

                // Scroll the upper controls horizontally to the end of the range and confirm it actually moved.
                upperScroller.Offset = new Vector(horizontalScrollRange, upperScroller.Offset.Y);
                view.UpdateLayout();

                Assert.True(upperScroller.Offset.X > 0,
                    $"Expected MapUpperControlsScroller.Offset.X to move off zero after a horizontal scroll (was {upperScroller.Offset.X:F1}).");

                var mapCanvasBoundsAfter = mapCanvas.Bounds;
                Assert.Equal(mapCanvasBoundsBefore.Width, mapCanvasBoundsAfter.Width);
                Assert.Equal(mapCanvasBoundsBefore.Height, mapCanvasBoundsAfter.Height);
                Assert.Equal(mapCanvasBoundsBefore.Y, mapCanvasBoundsAfter.Y);
                Assert.True(mapCanvasBoundsAfter.Height >= MinimumUsableMapHeight,
                    $"Expected MapCanvasPanel to remain usable (height >= {MinimumUsableMapHeight}) after scrolling the upper controls horizontally (was {mapCanvasBoundsAfter.Height:F1}).");
            }
            finally
            {
                view.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(800, 240, 80, 560)]   // ample room: budget (800-240=560) exceeds the controls floor.
        [InlineData(320, 240, 80, 80)]    // exact floor boundary: budget == controlsMin.
        [InlineData(200, 240, 80, 80)]    // pathologically short viewport: controls floor wins over the
                                          // canvas budget (documented edge case — canvas may dip under 240
                                          // only in this extreme case; the controls region never collapses).
        public void ComputeUpperControlsMaxHeight_FiniteEdgeCases(double availableHeight, double canvasMin, double controlsMin, double expected)
        {
            double actual = MapEditorView.ComputeUpperControlsMaxHeight(availableHeight, canvasMin, controlsMin);
            Assert.Equal(expected, actual, precision: 3);
        }

        [AvaloniaTheory]
        [InlineData(0)]              // not yet measured (0 height): no cap.
        [InlineData(double.NaN)]     // no cap while unmeasured.
        [InlineData(double.PositiveInfinity)] // unconstrained measure pass: no cap.
        [InlineData(-10)]            // defensively reject negative heights: no cap.
        public void ComputeUpperControlsMaxHeight_UnusableAvailableHeight_ReturnsNoCap(double availableHeight)
        {
            double actual = MapEditorView.ComputeUpperControlsMaxHeight(availableHeight, MinimumUsableMapHeight, 80);
            Assert.True(double.IsPositiveInfinity(actual), $"Expected no cap (PositiveInfinity) but got {actual}.");
        }

        [AvaloniaTheory]
        [InlineData("MapEditor_ExportCsv_Button", "Export Map (CSV)")]
        [InlineData("MapEditor_ImportCsv_Button", "Import Map (CSV)")]
        [InlineData("MapEditor_ExportTmx_Button", "Export Map (Tiled)")]
        [InlineData("MapEditor_ImportTmx_Button", "Import Map (Tiled)")]
        [InlineData("MapEditor_ResizeMap_Button", "Resize Map…")]
        [InlineData("MapEditor_GenerateRandomMap_Button", "Generate Random Map…")]
        [InlineData("MapEditor_WriteTileBtn_Button", "Write Tile")]
        [InlineData("MapEditor_RefreshMapBtn_Button", "Refresh Map")]
        public void AffectedButtons_RemainDiscoverableByAutomationId(string automationId, string content)
        {
            var view = new MapEditorView();
            var button = view.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == automationId);

            Assert.NotNull(button);
            Assert.Equal(content, button!.Content);
        }

        private static T Required<T>(Control root, string name) where T : Control
        {
            var control = root.FindControl<T>(name);
            Assert.NotNull(control);
            return control!;
        }

        // #1772: the map-resize dialog family declares fixed-width action buttons
        // (Resize 134px, Apply 99px). Without explicit content alignment the short
        // label sits left-shifted inside the wide box (visible on macOS). Assert the
        // button's effective content alignment centers its label, mirroring the #1703 fix.
        // (The app-wide sweep of other dialogs with the same signature is tracked in #1784.)
        [AvaloniaTheory]
        [InlineData(typeof(MapEditorResizeDialogView), "MapEditorResizeDialog_OK_Button")]
        [InlineData(typeof(MapEditorMarSizeDialogView), "MapEditorMarSizeDialog_Apply_Button")]
        public void MapResizeDialogActionButton_CentersItsLabel(Type dialogType, string automationId)
        {
            var dialog = (Control)Activator.CreateInstance(dialogType)!;

            var button = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == automationId);

            Assert.NotNull(button);
            Assert.Equal(HorizontalAlignment.Center, button!.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
        }


        private static double MeasureNaturalWidth(Control control)
        {
            control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return control.DesiredSize.Width;
        }

        // #1998 follow-up (review): returns MapUpperControlsScroller's actual VIEWPORT width —
        // the real, visible, viewport-bounded area — rather than MapEditorInfoPanel.Bounds.Width.
        // The scroller's content (MapEditorInfoPanel and its toolbar children) is given its
        // natural/unconstrained width because HorizontalScrollBarVisibility="Auto", so
        // MapEditorInfoPanel.Bounds.Width is itself DERIVED from the widest toolbar child; a
        // toolbar's width can never meaningfully exceed a parent whose own width IS "the widest
        // child" (a near-tautological comparison the previous version made). Viewport.Width is
        // the only direct measurement of whether a toolbar actually fits without horizontal
        // scrolling at a given arrangement.
        private static double ArrangeAndGetUpperControlsViewportWidth(MapEditorView view, double width, double height)
        {
            ArrangeAt(view, width, height);
            return Required<ScrollViewer>(view, "MapUpperControlsScroller").Viewport.Width;
        }

        // #1998: resize by wrapping the view in an explicitly-sized Border rather than
        // resizing the host Window. Avalonia's headless windowing platform bakes in a
        // fixed simulated ClientSize when a window is shown (observed to stay at
        // 1024x768 regardless of Window.Width/Height, whether set before or after
        // Show()), so the host Window itself cannot be resized for a test. A Border
        // with an explicit Width/Height, however, is measured/arranged at that exact
        // size by Avalonia's normal layout system (which we've already established
        // works correctly once attached to a real TopLevel/LayoutManager via
        // view.Show()) regardless of the ambient host Window's own fixed ClientSize.
        private static void ArrangeAt(MapEditorView view, double width, double height)
        {
            var host = view.GetHeadlessHost();
            if (host.Content is Border sizer && ReferenceEquals(sizer.Child, view))
            {
                sizer.Width = width;
                sizer.Height = height;
            }
            else
            {
                host.Content = null;
                host.Content = new Border
                {
                    Width = width,
                    Height = height,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = view
                };
            }

            host.UpdateLayout();
        }

        // #1998: measure the upper controls' own natural (unconstrained) height so
        // compact-viewport tests can pick a height guaranteed to be shorter than the
        // controls' natural content + the map canvas minimum, without hard-coding a
        // pixel value that could drift with font metrics.
        private static double MeasureNaturalUpperControlsHeight(MapEditorView view)
        {
            // A tall arrangement leaves the upper controls scroller uncapped (well above
            // its natural content height), so its Extent reports the true natural height.
            ArrangeAt(view, EditorWidth, 4000);
            var upperScroller = Required<ScrollViewer>(view, "MapUpperControlsScroller");
            return upperScroller.Extent.Height;
        }

        private static byte[] MakeSyntheticMapRgba(int width, int height)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4 + 0] = 80;  // R
                rgba[i * 4 + 1] = 120; // G
                rgba[i * 4 + 2] = 160; // B
                rgba[i * 4 + 3] = 255; // A
            }
            return rgba;
        }

        private static string FindButtonTag(string name)
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);

            string axamlPath = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia", "Views", "MapEditorView.axaml");
            string source = File.ReadAllText(axamlPath);
            var match = Regex.Match(source, $@"<Button\b(?=[^>]*\bName=""{Regex.Escape(name)}"")[^>]*>",
                RegexOptions.Singleline);
            Assert.True(match.Success, $"Button {name} was not found in MapEditorView.axaml.");
            return match.Value;
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
