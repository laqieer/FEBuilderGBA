using System;
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
public sealed class MainWindowUndoShortcutTests
{
    const uint Address = 0x300;

    sealed class MainWindowHarness : IDisposable
    {
        readonly ROM? _prevRom = CoreState.ROM;
        readonly Undo? _prevUndo = CoreState.Undo;
        readonly bool _prevSmokeTestMode = App.SmokeTestMode;
        readonly string? _prevStartupRomPath = App.StartupRomPath;
        readonly string? _prevStartupProjectDir = App.StartupProjectDir;
        readonly Window? _prevMainWindow = WindowManager.Instance.MainWindow;
        readonly Action _prevCacheCleared = Undo.OnCacheDataCountCleared;
        readonly Action _prevInvalidated = Undo.OnAllFormsInvalidated;
        readonly Func<string> _prevActiveFormName = Undo.GetActiveFormName;

        public MainWindow Window { get; }
        public ROM Rom { get; } = new();
        public Undo UndoState { get; } = new();

        public MainWindowHarness()
        {
            App.SmokeTestMode = true;
            App.StartupRomPath = null;
            App.StartupProjectDir = null;
            CoreStateTestState.ClearRom();
            CoreStateTestState.RestoreUndo(null);
            Undo.OnCacheDataCountCleared = null!;
            Undo.OnAllFormsInvalidated = null!;
            Undo.GetActiveFormName = null!;

            Window = new MainWindow { Width = 900, Height = 700 };
            Window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            Rom.SwapNewROMDataDirect(new byte[0x1000]);
            CoreState.ROM = Rom;
            CoreState.Undo = UndoState;
            WriteGroup(1);
            WriteGroup(7);
            Assert.Equal(2, UndoState.Postion);
            Assert.Equal(7u, Rom.u8(Address));
            Window.FindControl<MenuItem>("UndoMenuItem")!.IsEnabled = true;
        }

        void WriteGroup(uint value)
        {
            var group = UndoState.NewUndoData("shortcut test");
            using (ROM.BeginUndoScope(group))
                Rom.write_u8(Address, value);
            UndoState.Push(group);
        }

        public void AssertUnchanged()
        {
            Assert.Equal(7u, Rom.u8(Address));
            Assert.Equal(2, UndoState.Postion);
        }

        public void Dispose()
        {
            try
            {
                Window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                CoreStateTestState.RestoreRom(_prevRom);
                CoreStateTestState.RestoreUndo(_prevUndo);
                Undo.OnCacheDataCountCleared = _prevCacheCleared;
                Undo.OnAllFormsInvalidated = _prevInvalidated;
                Undo.GetActiveFormName = _prevActiveFormName;
                WindowManager.Instance.MainWindow = _prevMainWindow;
                App.StartupProjectDir = _prevStartupProjectDir;
                App.StartupRomPath = _prevStartupRomPath;
                App.SmokeTestMode = _prevSmokeTestMode;
            }
        }
    }

    static ToggleButton FocusInertChild(MainWindow window)
    {
        var child = window.FindControl<ToggleButton>("EasyModeToggle");
        Assert.NotNull(child);
        Assert.True(child!.Focus());
        return child;
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

    static void PressKey(Window window, KeyModifiers modifiers, PhysicalKey key = PhysicalKey.Z)
    {
        RawInputModifiers raw = RawInputModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0) raw |= RawInputModifiers.Control;
        if ((modifiers & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
        if ((modifiers & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
        window.KeyPressQwerty(key, raw);
        window.KeyReleaseQwerty(key, raw);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, false)]
    [InlineData(KeyModifiers.Meta, false)]
    [InlineData(KeyModifiers.Control, true)]
    [InlineData(KeyModifiers.Meta, true)]
    public void PrimaryUndoGesture_UndoesExactlyOneGroupAndClosesMenu(
        KeyModifiers modifiers, bool openMenu)
    {
        using var harness = new MainWindowHarness();
        var window = harness.Window;
        FocusInertChild(window);
        var menu = window.FindControl<Menu>("MainMenu")!;
        var editMenu = window.FindControl<MenuItem>("EditMenu")!;
        if (openMenu)
        {
            menu.Open();
            editMenu.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            Assert.True(editMenu.IsSubMenuOpen);
        }

        var getArgs = AttachKeyRecorder(window);
        int invalidations = 0;
        Undo.OnAllFormsInvalidated = () =>
        {
            invalidations++;
            Assert.True(getArgs()!.Handled);
            Assert.False(menu.IsOpen);
            Assert.False(editMenu.IsSubMenuOpen);
        };

        PressKey(window, modifiers);

        Assert.Equal(1u, harness.Rom.u8(Address));
        Assert.Equal(1, harness.UndoState.Postion);
        Assert.Equal(1, invalidations);
        Assert.NotNull(getArgs());
        Assert.True(getArgs()!.Handled);
        Assert.False(menu.IsOpen);
        Assert.False(editMenu.IsSubMenuOpen);
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, true)]
    [InlineData(KeyModifiers.Meta, true)]
    [InlineData(KeyModifiers.Control, false)]
    [InlineData(KeyModifiers.Meta, false)]
    public void ChildHandledOrDisabledUndo_DoesNotUndoRom(KeyModifiers modifiers, bool childHandles)
    {
        using var harness = new MainWindowHarness();
        var child = FocusInertChild(harness.Window);
        if (childHandles)
            child.KeyDown += (_, e) => e.Handled = true;
        else
            harness.Window.FindControl<MenuItem>("UndoMenuItem")!.IsEnabled = false;
        var getArgs = AttachKeyRecorder(harness.Window);

        PressKey(harness.Window, modifiers);

        harness.AssertUnchanged();
        Assert.NotNull(getArgs());
        Assert.Equal(childHandles, getArgs()!.Handled);
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.None, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Shift, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Alt, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Alt, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Shift, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta, PhysicalKey.Z)]
    [InlineData(KeyModifiers.Control, PhysicalKey.X)]
    public void NonPrimaryUndoGestures_DoNotUndoRom(KeyModifiers modifiers, PhysicalKey key)
    {
        using var harness = new MainWindowHarness();
        FocusInertChild(harness.Window);
        var getArgs = AttachKeyRecorder(harness.Window);

        PressKey(harness.Window, modifiers, key);

        harness.AssertUnchanged();
        Assert.NotNull(getArgs());
        Assert.False(getArgs()!.Handled);
    }

    [AvaloniaFact]
    public void TextLocalUndo_OwnsShortcutWithoutUndoingRom()
    {
        using var harness = new MainWindowHarness();
        harness.Window.FindControl<DockPanel>("SearchPanel")!.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        var textBox = harness.Window.FindControl<TextBox>("FilterTextBox")!;
        Assert.True(textBox.Focus());
        textBox.Text = "before";
        textBox.CaretIndex = textBox.Text.Length;
        harness.Window.KeyTextInput("after");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("beforeafter", textBox.Text);
        Assert.True(textBox.CanUndo);
        var getArgs = AttachKeyRecorder(harness.Window);

        PressKey(harness.Window, KeyModifiers.Control);

        Assert.Equal("before", textBox.Text);
        harness.AssertUnchanged();
        Assert.NotNull(getArgs());
        Assert.True(getArgs()!.Handled);
    }
}
