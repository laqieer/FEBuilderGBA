using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Regression coverage for #2098: the desktop lifetime must opt into
/// ShutdownMode.OnMainWindowClose so closing the shell tears down managed
/// editor windows instead of leaving them orphaned under the default
/// OnLastWindowClose behavior.
/// </summary>
[Collection("WindowManagerSerial")]
public sealed class DesktopLifetimeShutdownModeTests
{
    sealed class ClassicDesktopLifetimeApp : Application
    {
        public ClassicDesktopLifetimeApp()
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Program.ConfigureDesktopLifetime(lifetime);
            ApplicationLifetime = lifetime;
        }

        public override void Initialize() { }
    }

    sealed class ClassicDesktopLifetimeEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<ClassicDesktopLifetimeApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    sealed class LifecycleEditorWindow : Window, IEditorView
    {
        public string ViewTitle => "Lifecycle";
        public new bool IsLoaded => true;
        public int ClosedCount { get; private set; }

        public void NavigateTo(uint address) { }

        protected override void OnClosed(EventArgs e)
        {
            ClosedCount++;
            base.OnClosed(e);
        }
    }

    [Fact]
    public void ConfigureDesktopLifetime_sets_OnMainWindowClose()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime();

        Program.ConfigureDesktopLifetime(lifetime);

        Assert.Equal(ShutdownMode.OnMainWindowClose, lifetime.ShutdownMode);
    }

    static void WithClassicDesktopLifetime(Action<ClassicDesktopStyleApplicationLifetime, DesktopNavigationService> body)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(ClassicDesktopLifetimeEntryPoint),
            AvaloniaTestIsolationLevel.PerTest);

        session.Dispatch(() =>
        {
            var originalService = WindowManager.Instance.Service;
            var originalMainWindow = WindowManager.Instance.MainWindow;
            var service = new DesktopNavigationService();
            WindowManager.Instance.SetService(service);

            try
            {
                var lifetime = Assert.IsType<ClassicDesktopStyleApplicationLifetime>(
                    Application.Current!.ApplicationLifetime);
                body(lifetime, service);
            }
            finally
            {
                try
                {
                    WindowManager.Instance.CloseAll();
                    Dispatcher.UIThread.RunJobs();
                }
                catch { }

                if (WindowManager.Instance.MainWindow is { } mainWindow)
                {
                    try
                    {
                        mainWindow.Close();
                        Dispatcher.UIThread.RunJobs();
                    }
                    catch { }
                }

                WindowManager.Instance.SetService(originalService);
                WindowManager.Instance.MainWindow = originalMainWindow;
            }
        }, default);
    }

    [Fact]
    public void Closing_main_window_shuts_down_desktop_lifetime_and_closes_managed_editor()
    {
        WithClassicDesktopLifetime((lifetime, service) =>
        {
            Assert.Equal(ShutdownMode.OnMainWindowClose, lifetime.ShutdownMode);

            var mainWindow = new Window
            {
                Width = 240,
                Height = 160,
                Title = "Main",
            };

            lifetime.MainWindow = mainWindow;
            WindowManager.Instance.MainWindow = mainWindow;

            int exitCount = 0;
            lifetime.Exit += (_, _) => exitCount++;

            mainWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var secondary = service.Open<LifecycleEditorWindow>();
            Dispatcher.UIThread.RunJobs();

            Assert.True(mainWindow.IsVisible);
            Assert.True(secondary.IsVisible);
            Assert.Same(secondary, service.FindOpen<LifecycleEditorWindow>());
            Assert.Same(secondary, service.ActiveEditorWindow);
            Assert.Contains(mainWindow, lifetime.Windows);
            Assert.Contains(secondary, lifetime.Windows);

            mainWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, exitCount);
            Assert.False(mainWindow.IsVisible);
            Assert.False(secondary.IsVisible);
            Assert.Equal(1, secondary.ClosedCount);
            Assert.Empty(lifetime.Windows);
            Assert.Null(service.FindOpen<LifecycleEditorWindow>());
            Assert.Null(service.ActiveEditorWindow);
        });
    }
}
