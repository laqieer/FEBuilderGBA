// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — MainView (Android single-view shell) headless smoke test. It builds
// the shell, forces the Android single-view nav service, navigates to a page,
// and asserts the host reflects the navigation (back enabled, content swapped).
//
// NOTE: the canonical PR screenshot is produced by the desktop app's
// `--render-mainview=<path>` flag (App.RenderMainViewToPng) using the REAL
// desktop Skia platform — the Avalonia.Headless test platform's
// UseHeadlessDrawing does not rasterize, so a PNG saved here would be blank.
// This test therefore validates the shell WIRING headlessly; the pixels come
// from the desktop render. Neither proves on-device runtime UX (tracked #1070).
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class MainViewSingleViewShellTest
{
    [AvaloniaFact]
    public void MainView_wires_single_view_host_and_navigates()
    {
        var original = WindowManager.Instance.Service;
        try
        {
            // Force the Android single-view service so MainView wires its host.
            WindowManager.Instance.SetService(new AndroidNavigationService());

            var view = new MainView { Background = Brushes.White, Width = 360, Height = 640 };
            Assert.NotNull(view);

            // The active service is now an AndroidNavigationService implementing
            // the host seam, seeded with the launcher root (no back available).
            var host = (INavigationHost)WindowManager.Instance.Service;
            Assert.NotNull(host.CurrentContent); // launcher root page
            Assert.False(host.CanGoBack);

            // Navigate to a synthetic editor page — back must enable and the top
            // content must swap to that page.
            var content0 = host.CurrentContent;
            ((AndroidNavigationService)WindowManager.Instance.Service).Open<ShellShotEditorView>();
            Assert.True(host.CanGoBack);
            Assert.NotSame(content0, host.CurrentContent);

            // Back returns to the launcher root.
            Assert.True(host.GoBack());
            Assert.False(host.CanGoBack);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    // A synthetic editor page used to render/validate a navigated state.
    public class ShellShotEditorView : Window, IEditorView
    {
        public ShellShotEditorView()
        {
            Content = new StackPanel
            {
                Margin = new global::Avalonia.Thickness(16),
                Children =
                {
                    new TextBlock { Text = "Unit Editor (single-view page)", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Pushed onto the nav host's back stack." },
                },
            };
        }

        public string ViewTitle => "Unit Editor";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { }
    }
}
