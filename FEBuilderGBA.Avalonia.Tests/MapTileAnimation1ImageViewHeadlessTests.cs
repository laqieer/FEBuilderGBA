using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for the Map Tile Animation Type 1 editor's new
    /// image surface (#1602): the GbaImageControl preview + sample-palette combo
    /// + the four image buttons (Export / Import single PNG, Export All / Import
    /// All batch). Verifies the controls exist and carry their AutomationIds so
    /// screenshot / UIAutomation tooling can locate them.
    /// </summary>
    public class MapTileAnimation1ImageViewHeadlessTests
    {
        static List<string> CollectAutomationIds(Control root)
        {
            var result = new List<string>();
            foreach (var child in root.GetLogicalDescendants().OfType<Control>())
            {
                var id = AutomationProperties.GetAutomationId(child);
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }

        [AvaloniaFact]
        public void View_CanInstantiate()
        {
            var view = new MapTileAnimation1View();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void View_HasPreviewImageControl()
        {
            var view = new MapTileAnimation1View();
            var preview = view.FindControl<GbaImageControl>("PreviewImage");
            Assert.NotNull(preview);
        }

        [AvaloniaFact]
        public void View_HasSamplePaletteCombo_Populated()
        {
            var view = new MapTileAnimation1View();
            var combo = view.FindControl<ComboBox>("SamplePaletteComboBox");
            Assert.NotNull(combo);
            // The constructor seeds 16 sub-palette entries and selects index 0.
            Assert.Equal(0, combo.SelectedIndex);
            Assert.NotNull(combo.ItemsSource);
            Assert.Equal(16, combo.ItemsSource!.Cast<object>().Count());
        }

        [AvaloniaTheory]
        [InlineData("MapTileAnimation1_Export_Button")]
        [InlineData("MapTileAnimation1_Import_Button")]
        [InlineData("MapTileAnimation1_ExportAll_Button")]
        [InlineData("MapTileAnimation1_ImportAll_Button")]
        [InlineData("MapTileAnimation1_Preview_Image")]
        [InlineData("MapTileAnimation1_SamplePalette_Combo")]
        public void View_HasImageAutomationId(string id)
        {
            var view = new MapTileAnimation1View();
            var ids = CollectAutomationIds(view);
            Assert.Contains(id, ids);
        }

        [AvaloniaTheory]
        [InlineData("ExportButton")]
        [InlineData("ImportButton")]
        [InlineData("ExportAllButton")]
        [InlineData("ImportAllButton")]
        public void View_HasImageButton(string name)
        {
            var view = new MapTileAnimation1View();
            var button = view.FindControl<Button>(name);
            Assert.NotNull(button);
        }

        /// <summary>
        /// #1602 deferred half: the Export-All flow now also offers a composited-map
        /// animated GIF. The VM exposes <c>ExportGif</c> (the affordance the
        /// Export-All save dialog routes to when the user picks the .gif filter).
        /// Without a ROM / selected PLIST it must return a non-empty localized
        /// error rather than throwing.
        /// </summary>
        [AvaloniaFact]
        public void ViewModel_ExportGif_NoRomOrPlist_ReturnsError_DoesNotThrow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new MapTileAnimation1ViewModel();
                string gif = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "maptileanim1_test_" + System.Guid.NewGuid().ToString("N") + ".gif");
                string err = vm.ExportGif(gif);
                Assert.NotEqual("", err);
                Assert.False(System.IO.File.Exists(gif));
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
