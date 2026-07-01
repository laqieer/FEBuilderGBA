// SPDX-License-Identifier: GPL-3.0-or-later
// #1747 — DesktopNavigationService.ActiveEditorWindow tracking.
//
// The in-app "Report a Bug" tool must screenshot/label the editor the user is
// working in, not the main window. DesktopNavigationService tracks the
// most-recently-activated managed IEditorView window; these headless
// ([AvaloniaFact]) tests prove the MRU behavior, the close→fallback rule (so a
// still-open earlier editor is used instead of the main window), and that the
// main window and non-editor windows are never tracked.
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class DesktopNavigationServiceActiveEditorTests
{
    // A managed editor window (implements IEditorView, so it is tracked).
    public class EditorDoubleA : Window, IEditorView
    {
        public EditorDoubleA() { Title = "Editor A"; Content = new TextBlock { Text = "a" }; }
        public string ViewTitle => "Editor A";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { }
    }

    public class EditorDoubleB : Window, IEditorView
    {
        public EditorDoubleB() { Title = "Editor B"; Content = new TextBlock { Text = "b" }; }
        public string ViewTitle => "Editor B";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { }
    }

    // A non-editor window (does NOT implement IEditorView) — must never be tracked.
    public class ToolDouble : Window
    {
        public ToolDouble() { Title = "Tool"; Content = new TextBlock { Text = "t" }; }
    }

    [AvaloniaFact]
    public void ActiveEditorWindow_IsNull_WhenNoEditorOpen()
    {
        var svc = new DesktopNavigationService();
        Assert.Null(svc.ActiveEditorWindow);
    }

    [AvaloniaFact]
    public void ActiveEditorWindow_TracksMostRecentlyActivatedEditor()
    {
        var svc = new DesktopNavigationService();

        var a = svc.Open<EditorDoubleA>();
        Assert.Same(a, svc.ActiveEditorWindow);

        var b = svc.Open<EditorDoubleB>();
        Assert.Same(b, svc.ActiveEditorWindow);

        // Re-opening A re-activates it → it becomes the current editor again.
        var a2 = svc.Open<EditorDoubleA>();
        Assert.Same(a, a2); // singleton reuse
        Assert.Same(a, svc.ActiveEditorWindow);

        a.Close();
        b.Close();
    }

    [AvaloniaFact]
    public void ClosingActiveEditor_FallsBackToPreviousOpenEditor_NotMainWindow()
    {
        var svc = new DesktopNavigationService { MainWindow = new ToolDouble() };

        var a = svc.Open<EditorDoubleA>();
        var b = svc.Open<EditorDoubleB>();
        Assert.Same(b, svc.ActiveEditorWindow);

        // Closing the active editor B must fall back to the still-open A — NOT null
        // (which would make the bug reporter capture the main window instead).
        b.Close();
        Assert.Same(a, svc.ActiveEditorWindow);

        // Closing the last editor clears it.
        a.Close();
        Assert.Null(svc.ActiveEditorWindow);
    }

    [AvaloniaFact]
    public void ActiveEditorWindow_Reorders_WhenAlreadyOpenEditorIsReactivated()
    {
        var svc = new DesktopNavigationService();
        var a = svc.Open<EditorDoubleA>();
        var b = svc.Open<EditorDoubleB>();
        Assert.Same(b, svc.ActiveEditorWindow);

        // The user clicks back into the already-open editor A — a focus switch
        // that does NOT go through Open<T>. In the app Window.Activated drives
        // this; here we invoke the same internal hook the service subscribes,
        // because Window.Activated does not fire reliably under Avalonia.Headless.
        svc.NoteActivated(a);
        Assert.Same(a, svc.ActiveEditorWindow);

        a.Close();
        b.Close();
    }

    [AvaloniaFact]
    public void MainWindow_IsNeverTracked()
    {
        var svc = new DesktopNavigationService();
        var main = new EditorDoubleA(); // even an IEditorView must be ignored as MainWindow
        svc.MainWindow = main;

        svc.NoteActivated(main);

        Assert.Null(svc.ActiveEditorWindow);
    }

    [AvaloniaFact]
    public void NonEditorWindow_IsNotTracked()
    {
        var svc = new DesktopNavigationService();
        var tool = new ToolDouble();

        svc.NoteActivated(tool);

        Assert.Null(svc.ActiveEditorWindow);
    }
}
