// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class UpdateCheckMenuTests
    {
        [AvaloniaFact]
        public void HelpMenu_ContainsCheckForUpdatesItem()
        {
            var window = new MainWindow();

            var item = window.FindControl<MenuItem>("CheckUpdatesMenuItem");
            Assert.NotNull(item);
            Assert.Equal("Main_CheckUpdatesMenuItem_Button", AutomationProperties.GetAutomationId(item));
            Assert.Contains("Updates", item!.Header?.ToString() ?? "");

            var help = window.FindControl<MenuItem>("HelpMenu");
            Assert.NotNull(help);
            var children = help!.Items.OfType<MenuItem>().ToList();
            int aboutIdx = children.FindIndex(m => m.Name == "AboutMenuItem");
            int updateIdx = children.FindIndex(m => m.Name == "CheckUpdatesMenuItem");
            Assert.True(updateIdx >= 0, "CheckUpdatesMenuItem must be a child of the Help menu");
            Assert.True(aboutIdx >= 0 && aboutIdx < updateIdx,
                "Check for Updates should sit after About in the Help menu");
        }
    }
}
