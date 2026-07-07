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
// from the desktop render. Neither proves on-device runtime UX (tracked #1873).
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class MainViewSingleViewShellScreenshotTest
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

    [AvaloniaFact]
    public async Task SyntheticRom_can_open_MoveCostEditor_through_single_view_nav()
    {
        var originalService = WindowManager.Instance.Service;
        var prevBase = CoreState.BaseDirectory;
        var prevRom = CoreState.ROM;
        var prevAsm = CoreState.AsmMapFileAsmCache;
        var prevEnc = CoreState.SystemTextEncoder;
        var prevTid = CoreState.UseTextIDCache;
        var prevSkill = CoreState.SkillNameResolver;
        var prevExport = CoreState.ExportFunction;
        var prevUndo = CoreState.Undo;
        var prevEvent = CoreState.EventScript;
        try
        {
            var bytes = new byte[0x1000000];
            Encoding.ASCII.GetBytes("BE8E01").CopyTo(bytes, 0xAC);
            var rom = new ROM();
            using (var stream = new MemoryStream(bytes))
            {
                var (ok, version) = await rom.LoadFromStreamAsync(stream, "synthetic-fe8u.gba");
                Assert.True(ok, "synthetic FE8U header should be accepted by the ROM loader");
                Assert.False(string.IsNullOrEmpty(version));
            }

            CoreState.BaseDirectory = AppContext.BaseDirectory;
            RomFileService.InitializeLoadedRom(rom);
            WindowManager.Instance.SetService(new AndroidNavigationService());
            var view = WindowManager.Instance.Open<MoveCostEditorView>();

            var embeddable = Assert.IsAssignableFrom<IEmbeddableEditor>(view);
            Assert.Equal("Move Cost Editor", embeddable.Descriptor.Title);

            var host = Assert.IsAssignableFrom<INavigationHost>(WindowManager.Instance.Service);
            Assert.Same(view, host.CurrentContent);
            Assert.Equal("Move Cost Editor", host.CurrentTitle);
        }
        finally
        {
            WindowManager.Instance.SetService(originalService);
            CoreState.BaseDirectory = prevBase;
            CoreState.ROM = prevRom;
            CoreState.AsmMapFileAsmCache = prevAsm;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.UseTextIDCache = prevTid;
            CoreState.SkillNameResolver = prevSkill;
            CoreState.ExportFunction = prevExport;
            CoreState.Undo = prevUndo;
            CoreState.EventScript = prevEvent;
            PatchDetectionService.Instance.Refresh();
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
