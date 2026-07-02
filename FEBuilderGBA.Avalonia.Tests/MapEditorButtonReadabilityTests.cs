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
        private const double LeftColumnWidth = 250;
        private const double EditorBodyHorizontalMargin = 16;

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
            Assert.Equal(SizeToContent.Manual, view.SizeToContent);

            var navToolbar = Required<StackPanel>(view, "MapNavigationToolbar");
            var csvToolbar = Required<StackPanel>(view, "MapCsvCommandToolbar");
            var tmxToolbar = Required<StackPanel>(view, "MapTmxCommandToolbar");

            view.Measure(new Size(EditorWidth, EditorHeight));
            view.Arrange(new Rect(0, 0, EditorWidth, EditorHeight));
            view.UpdateLayout();

            navToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            csvToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tmxToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double availableBodyWidth = EditorWidth - LeftColumnWidth - EditorBodyHorizontalMargin;

            Assert.True(csvToolbar.DesiredSize.Width <= availableBodyWidth,
                $"CSV command toolbar desired width ({csvToolbar.DesiredSize.Width:F1}) exceeds " +
                $"the declared editor body width ({availableBodyWidth:F1}).");
            Assert.True(tmxToolbar.DesiredSize.Width <= availableBodyWidth,
                $"TMX command toolbar desired width ({tmxToolbar.DesiredSize.Width:F1}) exceeds " +
                $"the declared editor body width ({availableBodyWidth:F1}).");

            Assert.True(navToolbar.DesiredSize.Width + csvToolbar.DesiredSize.Width + tmxToolbar.DesiredSize.Width > availableBodyWidth,
                "The split toolbar test must discriminate against the previous single-row layout.");
        }

        [AvaloniaTheory]
        [InlineData("MapEditor_ExportCsv_Button", "Export Map (CSV)")]
        [InlineData("MapEditor_ImportCsv_Button", "Import Map (CSV)")]
        [InlineData("MapEditor_ExportTmx_Button", "Export Map (Tiled .tmx)")]
        [InlineData("MapEditor_ImportTmx_Button", "Import Map (Tiled .tmx)")]
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
