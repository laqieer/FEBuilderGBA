using System;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public sealed class MainWindowSaveShortcutTests
{
    public enum SaveAction
    {
        Save,
        SaveAs,
    }

    sealed class MainWindowHarness : IDisposable
    {
        readonly bool _prevSmokeTestMode = App.SmokeTestMode;
        readonly string? _prevStartupRomPath = App.StartupRomPath;
        readonly string? _prevStartupProjectDir = App.StartupProjectDir;
        readonly Window? _prevMainWindow = WindowManager.Instance.MainWindow;

        public MainWindow Window { get; }

        public MainWindowHarness(bool show = true)
        {
            App.SmokeTestMode = true;
            App.StartupRomPath = null;
            App.StartupProjectDir = null;

            Window = new MainWindow
            {
                Width = 900,
                Height = 700,
            };

            if (show)
            {
                Window.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            }
        }

        public void Dispose()
        {
            Window.SaveRomOperationOverride = null;
            Window.SaveAsRomOperationOverride = null;
            try
            {
                Window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            catch { }

            WindowManager.Instance.MainWindow = _prevMainWindow;
            App.StartupProjectDir = _prevStartupProjectDir;
            App.StartupRomPath = _prevStartupRomPath;
            App.SmokeTestMode = _prevSmokeTestMode;
        }
    }

    static ToggleButton FocusInertChild(MainWindow window)
    {
        var button = window.FindControl<ToggleButton>("EasyModeToggle");
        Assert.NotNull(button);
        Assert.True(button!.Focus());
        return button;
    }

    static Func<KeyEventArgs?> AttachKeyRecorder(Window window)
    {
        KeyEventArgs? lastArgs = null;
        window.AddHandler(InputElement.KeyDownEvent,
            (_, e) => lastArgs = e,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        return () => lastArgs;
    }

    static RawInputModifiers ToRawModifiers(KeyModifiers modifiers)
    {
        RawInputModifiers rawModifiers = RawInputModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0)
            rawModifiers |= RawInputModifiers.Control;
        if ((modifiers & KeyModifiers.Meta) != 0)
            rawModifiers |= RawInputModifiers.Meta;
        if ((modifiers & KeyModifiers.Shift) != 0)
            rawModifiers |= RawInputModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            rawModifiers |= RawInputModifiers.Alt;
        return rawModifiers;
    }

    static void PressKey(Window window, Key key, KeyModifiers modifiers)
    {
        var rawModifiers = ToRawModifiers(modifiers);
        window.KeyPress(key, rawModifiers);
        window.KeyRelease(key, rawModifiers);
        Dispatcher.UIThread.RunJobs();
    }

    static Task<bool> InvokeAsync(MainWindow window, SaveAction action)
        => action == SaveAction.Save ? window.SaveRomAsync() : window.SaveAsRomAsync();

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public void PrimarySaveGesture_WithClosedMenuAndFocusedChild_RunsSaveOnceAndMarksHandled(
        KeyModifiers modifiers)
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        var mainMenu = window.FindControl<Menu>("MainMenu");
        Assert.NotNull(saveMenuItem);
        Assert.NotNull(mainMenu);
        saveMenuItem!.IsEnabled = true;

        var getLastArgs = AttachKeyRecorder(window);
        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            return Task.CompletedTask;
        };

        PressKey(window, Key.S, modifiers);

        var args = getLastArgs();
        Assert.NotNull(args);
        Assert.Equal(1, saveCalls);
        Assert.True(args!.Handled);
        Assert.False(mainMenu!.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public void PrimarySaveGesture_WithFileMenuOpen_RunsSaveOnceAndClosesRootMenu(
        KeyModifiers modifiers)
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var mainMenu = window.FindControl<Menu>("MainMenu");
        var fileMenu = window.FindControl<MenuItem>("FileMenu");
        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(mainMenu);
        Assert.NotNull(fileMenu);
        Assert.NotNull(saveMenuItem);

        saveMenuItem!.IsEnabled = true;
        mainMenu!.Open();
        fileMenu!.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(mainMenu.IsOpen);
        Assert.True(fileMenu.IsSubMenuOpen);

        var getLastArgs = AttachKeyRecorder(window);
        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            Assert.False(mainMenu.IsOpen);
            Assert.False(fileMenu.IsSubMenuOpen);
            return Task.CompletedTask;
        };

        PressKey(window, Key.S, modifiers);

        var args = getLastArgs();
        Assert.NotNull(args);
        Assert.Equal(1, saveCalls);
        Assert.True(args!.Handled);
        Assert.False(mainMenu.IsOpen);
        Assert.False(fileMenu.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void CtrlS_WithNestedToolsMenuOpen_RunsSaveOnceAndClosesNestedMenus()
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var mainMenu = window.FindControl<Menu>("MainMenu");
        var toolsMenu = window.FindControl<MenuItem>("ToolsMenu");
        var externalToolsMenu = window.FindControl<MenuItem>("ExternalToolsMenuItem");
        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(mainMenu);
        Assert.NotNull(toolsMenu);
        Assert.NotNull(externalToolsMenu);
        Assert.NotNull(saveMenuItem);

        saveMenuItem!.IsEnabled = true;
        mainMenu!.Open();
        toolsMenu!.IsSubMenuOpen = true;
        externalToolsMenu!.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(mainMenu.IsOpen);
        Assert.True(toolsMenu.IsSubMenuOpen);
        Assert.True(externalToolsMenu.IsSubMenuOpen);

        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            Assert.False(mainMenu.IsOpen);
            Assert.False(toolsMenu.IsSubMenuOpen);
            Assert.False(externalToolsMenu.IsSubMenuOpen);
            return Task.CompletedTask;
        };

        PressKey(window, Key.S, KeyModifiers.Control);

        Assert.Equal(1, saveCalls);
        Assert.False(mainMenu.IsOpen);
        Assert.False(toolsMenu.IsSubMenuOpen);
        Assert.False(externalToolsMenu.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void CtrlS_AfterMenuWasOpenedAndClosed_RunsSaveOnce()
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var mainMenu = window.FindControl<Menu>("MainMenu");
        var fileMenu = window.FindControl<MenuItem>("FileMenu");
        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(mainMenu);
        Assert.NotNull(fileMenu);
        Assert.NotNull(saveMenuItem);
        saveMenuItem!.IsEnabled = true;

        mainMenu!.Open();
        fileMenu!.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        mainMenu.Close();
        Dispatcher.UIThread.RunJobs();

        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            return Task.CompletedTask;
        };

        PressKey(window, Key.S, KeyModifiers.Control);

        Assert.Equal(1, saveCalls);
        Assert.False(mainMenu.IsOpen);
    }

    [AvaloniaFact]
    public void ChildHandledShortcut_WinsOverWindowSaveFallback()
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        var child = FocusInertChild(window);

        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(saveMenuItem);
        saveMenuItem!.IsEnabled = true;

        var getLastArgs = AttachKeyRecorder(window);
        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            return Task.CompletedTask;
        };
        child.KeyDown += (_, e) => e.Handled = true;

        PressKey(window, Key.S, KeyModifiers.Control);

        var args = getLastArgs();
        Assert.NotNull(args);
        Assert.Equal(0, saveCalls);
        Assert.True(args!.Handled);
    }

    [AvaloniaFact]
    public void DisabledSaveShortcut_RemainsUnhandled()
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(saveMenuItem);
        saveMenuItem!.IsEnabled = false;

        var getLastArgs = AttachKeyRecorder(window);
        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            return Task.CompletedTask;
        };

        PressKey(window, Key.S, KeyModifiers.Control);

        var args = getLastArgs();
        Assert.NotNull(args);
        Assert.Equal(0, saveCalls);
        Assert.False(args!.Handled);
    }

    [AvaloniaTheory]
    [InlineData(Key.S, KeyModifiers.None)]
    [InlineData(Key.S, KeyModifiers.Shift)]
    [InlineData(Key.S, KeyModifiers.Alt)]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.S, KeyModifiers.Meta | KeyModifiers.Alt)]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.S, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Meta)]
    [InlineData(Key.D, KeyModifiers.Control)]
    public void NonPrimarySaveGestures_DoNotInvokeSave(Key key, KeyModifiers modifiers)
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(saveMenuItem);
        saveMenuItem!.IsEnabled = true;

        var getLastArgs = AttachKeyRecorder(window);
        int saveCalls = 0;
        window.SaveRomOperationOverride = () =>
        {
            saveCalls++;
            return Task.CompletedTask;
        };

        PressKey(window, key, modifiers);

        var args = getLastArgs();
        Assert.NotNull(args);
        Assert.Equal(0, saveCalls);
        Assert.False(args!.Handled);
    }

    [AvaloniaTheory]
    [InlineData(SaveAction.Save, SaveAction.Save)]
    [InlineData(SaveAction.Save, SaveAction.SaveAs)]
    [InlineData(SaveAction.SaveAs, SaveAction.Save)]
    [InlineData(SaveAction.SaveAs, SaveAction.SaveAs)]
    public async Task SaveOperations_AreSingleFlightAcrossAllOverlapDirections(SaveAction firstAction, SaveAction secondAction)
    {
        using var harness = new MainWindowHarness(show: false);
        var window = harness.Window;

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int saveCalls = 0;
        int saveAsCalls = 0;

        window.SaveRomOperationOverride = async () =>
        {
            saveCalls++;
            started.TrySetResult(true);
            await release.Task;
        };
        window.SaveAsRomOperationOverride = async () =>
        {
            saveAsCalls++;
            started.TrySetResult(true);
            await release.Task;
        };

        Task<bool> firstTask = InvokeAsync(window, firstAction);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<bool> secondTask = InvokeAsync(window, secondAction);
        Assert.False(await secondTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(firstAction == SaveAction.Save ? 1 : 0, saveCalls);
        Assert.Equal(firstAction == SaveAction.SaveAs ? 1 : 0, saveAsCalls);

        release.TrySetResult(true);
        Assert.True(await firstTask);
    }

    [AvaloniaFact]
    public async Task RoutedCtrlS_WhilePending_DoesNotReenter_AndRetriesAfterCompletion()
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);

        var saveMenuItem = window.FindControl<MenuItem>("SaveMenuItem");
        Assert.NotNull(saveMenuItem);
        saveMenuItem!.IsEnabled = true;

        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int saveCalls = 0;
        window.SaveRomOperationOverride = async () =>
        {
            saveCalls++;
            if (saveCalls != 1)
                return;

            entered.TrySetResult(true);
            await release.Task;
            completed.TrySetResult(true);
        };

        PressKey(window, Key.S, KeyModifiers.Control);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        PressKey(window, Key.S, KeyModifiers.Control);
        Assert.Equal(1, saveCalls);

        release.TrySetResult(true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();

        PressKey(window, Key.S, KeyModifiers.Control);
        Assert.Equal(2, saveCalls);
    }

    [AvaloniaFact]
    public async Task SaveGuard_ResetsAfterFaultAndAllowsRetry()
    {
        using var harness = new MainWindowHarness(show: false);
        var window = harness.Window;

        int attempts = 0;
        window.SaveRomOperationOverride = () =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException(new InvalidOperationException("boom"))
                : Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await window.SaveRomAsync());
        Assert.True(await window.SaveRomAsync());

        Assert.Equal(2, attempts);
    }
}
