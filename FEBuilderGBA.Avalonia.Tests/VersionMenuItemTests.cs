// SPDX-License-Identifier: GPL-3.0-or-later
// #1797 — the Avalonia Tools menu exposes a "Version Information" item (opening
// VersionView, which shows U.getAppVersion()). This is the fix for the reported
// "Tools -> Version doesn't exist" gap (VersionView already existed but was
// unreachable from the UI). Placement matches .github/ISSUE_TEMPLATE/gui_bug.yml,
// which directs users to "Help → About, or Tools → Version Information".
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class VersionMenuItemTests
    {
        [AvaloniaFact]
        public void ToolsMenu_ContainsVersionInformationItem()
        {
            var window = new MainWindow();

            var version = window.FindControl<MenuItem>("VersionMenuItem");
            Assert.NotNull(version);
            Assert.Contains("Version", version!.Header?.ToString() ?? "");

            var toolsMenu = window.FindControl<MenuItem>("ToolsMenu");
            Assert.NotNull(toolsMenu);
            var children = toolsMenu!.Items.OfType<MenuItem>().ToList();
            int versionIdx = children.FindIndex(m => m.Name == "VersionMenuItem");
            int optionsIdx = children.FindIndex(m => m.Name == "OptionsMenuItem");
            Assert.True(versionIdx >= 0, "VersionMenuItem must be a child of the Tools menu");
            Assert.True(optionsIdx >= 0 && optionsIdx < versionIdx,
                "Version Information should sit after Options in the Tools menu");
        }
    }
}

