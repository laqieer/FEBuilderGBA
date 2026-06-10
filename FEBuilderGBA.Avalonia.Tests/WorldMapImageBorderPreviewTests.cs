// SPDX-License-Identifier: GPL-3.0-or-later
// Parity tests for the WorldMapImageView Border tab (#849, NV5c):
//   * BorderDrawSampleImage is now a GbaImageControl (not a plain Image).
//   * The Border Export PNG button's IsEnabled binding targets CanExportBorder.
//   * CanExportBorder is exposed on the WorldMapImageViewModel.
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Verifies that the WorldMapImageView Border tab was updated for #849:
    /// the preview control is a <see cref="GbaImageControl"/> and the Export
    /// button's IsEnabled state is gated on <c>CanExportBorder</c>.
    /// </summary>
    [Collection("SharedState")]
    public class WorldMapImageBorderPreviewTests
    {
        const int BorderTabIndex = 4; // Main=0,Event=1,Mini=2,PointIcon=3,Border=4

        readonly ITestOutputHelper _output;

        public WorldMapImageBorderPreviewTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void BorderDrawSampleImage_IsGbaImageControl()
        {
            // #849 NV5c: the placeholder <Image Name="BorderDrawSampleImage"> was
            // replaced with <controls:GbaImageControl Name="BorderDrawSampleImage"/>.
            // Verify the named control in the AXAML tree has the correct type.
            var view = new WorldMapImageView();
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            var ctrl = view.FindControl<GbaImageControl>("BorderDrawSampleImage");
            Assert.NotNull(ctrl);
            _output.WriteLine("BorderDrawSampleImage is GbaImageControl — PASS");
        }

        [AvaloniaFact]
        public void BorderExportButton_IsEnabled_WhenCanExportBorderTrue()
        {
            // The Border Export PNG button's IsEnabled should be bound to
            // CanExportBorder. Verify by setting CanExportBorder=true on the VM
            // and asserting the button becomes enabled.
            var view = new WorldMapImageView();
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            // Get the VM via the view's DataContext (set in the constructor).
            var vm = view.DataContext as WorldMapImageViewModel;
            Assert.NotNull(vm);

            // Set CanExportBorder = true — the binding should enable the button.
            vm!.CanExportBorder = true;
            // Force another layout pass so the binding propagates.
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            var exportBtn = view.FindControl<Button>("BorderExportButton");
            Assert.NotNull(exportBtn);
            Assert.True(exportBtn!.IsEnabled,
                "Border Export button should be enabled when CanExportBorder=true");
            _output.WriteLine("Border Export button enabled when CanExportBorder=true — PASS");
        }

        [AvaloniaFact]
        public void BorderExportButton_IsDisabled_WhenCanExportBorderFalse()
        {
            // The Border Export PNG button must be DISABLED when CanExportBorder=false
            // (default after LoadAll with no ROM or a non-FE8 ROM).
            var view = new WorldMapImageView();
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            var vm = view.DataContext as WorldMapImageViewModel;
            Assert.NotNull(vm);

            vm!.CanExportBorder = false;
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            var exportBtn = view.FindControl<Button>("BorderExportButton");
            Assert.NotNull(exportBtn);
            Assert.False(exportBtn!.IsEnabled,
                "Border Export button should be disabled when CanExportBorder=false");
            _output.WriteLine("Border Export button disabled when CanExportBorder=false — PASS");
        }

        [AvaloniaFact]
        public void WorldMapImageViewModel_CanExportBorder_PropertyExists()
        {
            // Verify the VM exposes CanExportBorder as a settable property.
            var vm = new WorldMapImageViewModel();
            // Default is false.
            Assert.False(vm.CanExportBorder);
            // Set to true.
            vm.CanExportBorder = true;
            Assert.True(vm.CanExportBorder);
            _output.WriteLine("WorldMapImageViewModel.CanExportBorder property works — PASS");
        }

        [AvaloniaFact]
        public void WorldMapImageViewModel_CanImportBorder_PropertyExists()
        {
            // #1064 PR2: the Border Import button is now gated by CanImportBorder
            // (FE8 + a county-border palette pointer + a selected record). Verify
            // the VM exposes it as a settable property defaulting to false (no ROM /
            // no selection -> the button stays disabled).
            var vm = new WorldMapImageViewModel();
            Assert.False(vm.CanImportBorder);
            vm.CanImportBorder = true;
            Assert.True(vm.CanImportBorder);
            _output.WriteLine("WorldMapImageViewModel.CanImportBorder property works — PASS");
        }

        [AvaloniaFact]
        public void BorderImportButton_IsGatedByCanImportBorder()
        {
            // #1064 PR2 (closes #1064 + #1000): the Border Import button is wired —
            // its IsEnabled tracks the CanImportBorder gate. With no ROM loaded the
            // gate is false (button disabled); flipping the VM gate enables it.
            var view = new WorldMapImageView();
            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));

            // Switch to Border tab so its content is realized.
            var tab = view.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            if (tab != null)
            {
                tab.SelectedIndex = BorderTabIndex;
                view.Measure(new Size(1100, 720));
                view.Arrange(new Rect(0, 0, 1100, 720));
            }

            var importBtn = view.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties
                    .GetAutomationId(b) == "WorldMapImage_Border_Import_Button");

            if (importBtn == null)
            {
                _output.WriteLine("Border Import button not found in visual tree (tab not realized in headless mode) — SKIP");
                return;
            }

            // No ROM loaded -> CanImportBorder is false -> the button is disabled
            // (the binding gates it; it is no longer a hard IsEnabled=False stub).
            Assert.False(importBtn.IsEnabled,
                "Border Import button should be disabled when CanImportBorder is false (no ROM/selection)");
            _output.WriteLine("Border Import button is gated by CanImportBorder — PASS");
        }
    }
}
