// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class OptionsViewTilesetNavigationTests
    {
        [AvaloniaFact]
        public void SetTilesetContext_SelectsExternalToolsAndScrollsToFEMapCreator()
        {
            var view = new OptionsView();
            var host = new Window
            {
                Width = 620,
                Height = 560,
                Content = view,
            };
            host.Show();
            try
            {
                host.UpdateLayout();
                TilesetFingerprint fingerprint = TilesetFingerprint.Compute(
                    8,
                    new byte[] { 1 },
                    new byte[] { 2 },
                    new byte[] { 3 });

                view.SetTilesetContext(fingerprint);
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                TabControl tabs = view.FindControl<TabControl>("OptionsTabControl")!;
                TabItem externalTools = view.FindControl<TabItem>("ExternalToolsTabItem")!;
                ScrollViewer scroller = view.FindControl<ScrollViewer>("ExternalToolsScrollViewer")!;
                Control section = view.FindControl<Control>("FEMapCreatorSectionPanel")!;

                Assert.Same(externalTools, tabs.SelectedItem);
                Assert.True(section.IsEffectivelyVisible);
                Assert.True(scroller.Offset.Y > 0,
                    "Map Tileset navigation should scroll past the earlier tool sections to FEMapCreator.");
            }
            finally
            {
                host.Close();
            }
        }
    }
}
