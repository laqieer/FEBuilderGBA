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
            Assert.False(view.Descriptor.SizeToContent);

            var navToolbar = Required<StackPanel>(view, "MapNavigationToolbar");
            var csvToolbar = Required<StackPanel>(view, "MapCsvCommandToolbar");
            var tmxToolbar = Required<StackPanel>(view, "MapTmxCommandToolbar");
            var mapCanvas = Required<Border>(view, "MapCanvasPanel");

            double navWidth = MeasureNaturalWidth(navToolbar);
            double csvWidth = MeasureNaturalWidth(csvToolbar);
            double tmxWidth = MeasureNaturalWidth(tmxToolbar);

            double availableBodyWidth = ArrangeAndGetToolbarWidth(view, EditorWidth, EditorHeight);
            double availableMinBodyWidth = ArrangeAndGetToolbarWidth(
                view,
                view.Descriptor.MinWidth,
                view.Descriptor.MinHeight);
            double widestSplitToolbarRow = Math.Max(navWidth, Math.Max(csvWidth, tmxWidth));

            Assert.True(csvWidth <= availableBodyWidth,
                $"CSV command toolbar desired width ({csvWidth:F1}) exceeds " +
                $"the arranged toolbar content width ({availableBodyWidth:F1}).");
            Assert.True(tmxWidth <= availableBodyWidth,
                $"TMX command toolbar desired width ({tmxWidth:F1}) exceeds " +
                $"the arranged toolbar content width ({availableBodyWidth:F1}).");
            Assert.True(widestSplitToolbarRow <= availableMinBodyWidth,
                $"Widest split toolbar row ({widestSplitToolbarRow:F1}) exceeds the MinWidth toolbar content width " +
                $"({availableMinBodyWidth:F1}); manual resize could clip the toolbar.");
            Assert.True(view.Descriptor.MinHeight >= MinimumUsableMapHeight,
                $"MapEditorView descriptor MinHeight ({view.Descriptor.MinHeight:F1}) should leave a usable map canvas area.");

            Assert.True(view.Descriptor.MinWidth < view.Descriptor.PreferredWidth,
                "The hosted editor descriptor should preserve a smaller MinWidth than the preferred width.");

            view.Measure(new Size(view.Descriptor.MinWidth, view.Descriptor.MinHeight));
            view.Arrange(new Rect(0, 0, view.Descriptor.MinWidth, view.Descriptor.MinHeight));
            view.UpdateLayout();

            Assert.True(mapCanvas.Bounds.Height >= MinimumUsableMapHeight,
                $"Map canvas height at MinHeight ({mapCanvas.Bounds.Height:F1}) should remain usable.");
        }

        [AvaloniaTheory]
        [InlineData("MapEditor_ExportCsv_Button", "Export Map (CSV)")]
        [InlineData("MapEditor_ImportCsv_Button", "Import Map (CSV)")]
        [InlineData("MapEditor_ExportTmx_Button", "Export Map (Tiled)")]
        [InlineData("MapEditor_ImportTmx_Button", "Import Map (Tiled)")]
        [InlineData("MapEditor_ResizeMap_Button", "Resize Map…")]
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
            var dialog = (Window)Activator.CreateInstance(dialogType)!;

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

        private static double ArrangeAndGetToolbarWidth(MapEditorView view, double width, double height)
        {
            var root = Required<Grid>(view, "MapEditorRootGrid");
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            return Required<StackPanel>(view, "MapEditorInfoPanel").Bounds.Width;
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
