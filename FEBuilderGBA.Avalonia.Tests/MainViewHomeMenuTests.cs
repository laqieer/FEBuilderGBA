// SPDX-License-Identifier: GPL-3.0-or-later
// #1895 — the web-app home page (single-view MainView shell) gains an overflow
// "More" menu: a live Language switcher plus Wiki / Discussions / Issue Report
// links. These headless tests assert the More button + flyout are wired with the
// right items/AutomationIds, and that the external-link URLs are exactly the fork's
// (never upstream).
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class MainViewHomeMenuTests
{
    [Fact]
    public void HomePageLinks_are_exactly_the_fork_urls()
    {
        Assert.Equal("https://github.com/laqieer/FEBuilderGBA/wiki", MainView.WikiUrl);
        Assert.Equal("https://github.com/laqieer/FEBuilderGBA/discussions", MainView.DiscussionsUrl);
        // Issue Report uses /issues/new/choose because the fork disables blank issues
        // (.github/ISSUE_TEMPLATE/config.yml).
        Assert.Equal("https://github.com/laqieer/FEBuilderGBA/issues/new/choose", MainView.IssueReportUrl);

        var links = MainView.HomePageLinks.ToList();
        Assert.Equal(3, links.Count);
        Assert.Equal(("Wiki", MainView.WikiUrl), links[0]);
        Assert.Equal(("Discussions", MainView.DiscussionsUrl), links[1]);
        Assert.Equal(("Issue Report", MainView.IssueReportUrl), links[2]);

        // Never point at the upstream org repo.
        Assert.DoesNotContain(links, l => l.Url.Contains("FEBuilderGBA/FEBuilderGBA/", StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopDownloadLink_points_at_the_latest_release()
    {
        // GitHub's version-less "latest release" redirect (all platform assets). #1907.
        Assert.Equal("https://github.com/laqieer/FEBuilderGBA/releases/latest", MainView.LatestReleaseUrl);

        var (label, url) = MainView.DesktopDownloadLink;
        Assert.Equal("Download the desktop app", label);
        Assert.Equal(MainView.LatestReleaseUrl, url);
        Assert.Contains("/releases/latest", url, StringComparison.Ordinal);
        Assert.DoesNotContain("FEBuilderGBA/FEBuilderGBA/", url, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void More_menu_has_language_submenu_and_link_items()
    {
        var originalService = WindowManager.Instance.Service;
        var prevBase = CoreState.BaseDirectory;
        try
        {
            CoreState.BaseDirectory = AppContext.BaseDirectory;
            WindowManager.Instance.SetService(new AndroidNavigationService());

            var view = new MainView { Width = 420, Height = 900 };

            var moreButton = view.GetLogicalDescendants().OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "Main_AndroidMore_Button");
            Assert.NotNull(moreButton);

            var flyout = Assert.IsType<MenuFlyout>(moreButton!.Flyout);

            // #1907: the "download the desktop app" CTA is the FIRST flyout item,
            // immediately followed by a Separator, then the Language submenu + links.
            var rawItems = flyout.Items.Cast<object>().ToList();
            var firstItem = Assert.IsType<MenuItem>(rawItems[0]);
            Assert.Equal("Main_AndroidDownloadDesktop_Button", AutomationProperties.GetAutomationId(firstItem));
            Assert.IsType<Separator>(rawItems[1]);

            var items = flyout.Items.OfType<MenuItem>().ToList();

            // Language submenu present, with one child per available language.
            var language = items.FirstOrDefault(i => AutomationProperties.GetAutomationId(i) == "Main_AndroidLanguage_Button");
            Assert.NotNull(language);
            Assert.Equal(OptionsViewModel.EnumerateLanguages().Count, language!.Items.OfType<MenuItem>().Count());

            // The three external-link items are present with their AutomationIds.
            foreach (var id in new[] { "Main_AndroidWiki_Button", "Main_AndroidDiscussions_Button", "Main_AndroidIssueReport_Button" })
                Assert.Contains(items, i => AutomationProperties.GetAutomationId(i) == id);
        }
        finally
        {
            WindowManager.Instance.SetService(originalService);
            CoreState.BaseDirectory = prevBase;
        }
    }
}
