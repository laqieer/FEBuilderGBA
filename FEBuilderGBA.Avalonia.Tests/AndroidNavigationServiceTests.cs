// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — AndroidNavigationService (single-view nav host) tests.
//
// These run headless ([AvaloniaFact]) because the service instantiates real
// view Windows (as content factories) and extracts their Content control. They
// prove the content-extraction bridge + the common nav paths:
//   - Open<T> returns the concrete view instance AND pushes its content as a page;
//   - Navigate<T> calls NavigateTo on the returned view;
//   - FindOpen<T> returns the open view; re-Open<T> re-surfaces it;
//   - PickFromEditor pushes a page, resolves on SelectionConfirmed, cancels to
//     null on GoBack;
//   - CloseAll drops back to root and cancels pending picks.
// The service implements INavigationHost; the shell binds to CurrentContent.
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class AndroidNavigationServiceTests
{
    // A view whose Content is a uniquely identifiable control so we can assert
    // the host renders the right page.
    public class NavTestView : Window, IEditorView
    {
        public static int NavigateCalls;
        public uint LastNavigated;
        public TextBlock Marker { get; }

        public NavTestView()
        {
            Marker = new TextBlock { Text = "nav-test-view" };
            Content = Marker;
        }

        public string ViewTitle => "NavTest";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { LastNavigated = address; NavigateCalls++; }
    }

    public class PickTestView : Window, IPickableEditor
    {
        public bool PickModeEnabled;
        public TextBlock Marker { get; }

        public PickTestView()
        {
            Marker = new TextBlock { Text = "pick-test-view" };
            Content = Marker;
        }

        public string ViewTitle => "PickTest";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { }
        public event Action<PickResult>? SelectionConfirmed;
        public void EnablePickMode() => PickModeEnabled = true;
        public void Confirm(PickResult r) => SelectionConfirmed?.Invoke(r);
    }

    static AndroidNavigationService NewServiceWithRoot()
    {
        var s = new AndroidNavigationService();
        s.SetRoot(new TextBlock { Text = "root" });
        return s;
    }

    [AvaloniaFact]
    public void Open_returns_view_instance_and_pushes_its_content()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;

        var view = service.Open<NavTestView>();
        Assert.NotNull(view);
        // The returned instance is the live view (callers invoke methods on it).
        Assert.IsType<NavTestView>(view);
        // The host now renders that view's extracted Content marker.
        Assert.Same(view.Marker, host.CurrentContent);
        Assert.True(host.CanGoBack);
    }

    [AvaloniaFact]
    public void Navigate_opens_and_calls_NavigateTo()
    {
        var service = NewServiceWithRoot();
        int before = NavTestView.NavigateCalls;
        var view = service.Navigate<NavTestView>(0xDEAD);
        Assert.Equal(0xDEADu, view.LastNavigated);
        Assert.True(NavTestView.NavigateCalls > before);
    }

    [AvaloniaFact]
    public void FindOpen_returns_open_view_then_null_after_CloseAll()
    {
        var service = NewServiceWithRoot();
        Assert.Null(service.FindOpen<NavTestView>());
        var view = service.Open<NavTestView>();
        Assert.Same(view, service.FindOpen<NavTestView>());
        service.CloseAll();
        Assert.Null(service.FindOpen<NavTestView>());
    }

    [AvaloniaFact]
    public void ReOpen_resurfaces_the_same_view_to_top()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;

        var first = service.Open<NavTestView>();
        // Push a different page on top.
        service.Open<PickTestView2>();
        Assert.NotSame(first.Marker, host.CurrentContent);
        // Re-opening the singleton resurfaces its content to the top.
        var again = service.Open<NavTestView>();
        Assert.Same(first, again);
        Assert.Same(first.Marker, host.CurrentContent);
    }

    [AvaloniaFact]
    public async Task PickFromEditor_resolves_on_SelectionConfirmed()
    {
        var service = NewServiceWithRoot();

        // Capture the (non-cached) pick view via the test seam so we can confirm.
        PickTestView? captured = null;
        service.ViewInstantiated += w => { if (w is PickTestView p) captured = p; };

        var pickTask = service.PickFromEditor<PickTestView>(0);
        Assert.NotNull(captured);
        Assert.True(captured!.PickModeEnabled);
        Assert.False(pickTask.IsCompleted);

        var expected = new PickResult(7, 0x200, "picked");
        captured.Confirm(expected);

        var result = await pickTask;
        Assert.NotNull(result);
        Assert.Equal(7, result!.Index);
        Assert.Equal(0x200u, result.Address);
        // The pick page popped after confirm — back at root, no back available.
        Assert.False(((INavigationHost)service).CanGoBack);
    }

    [AvaloniaFact]
    public async Task PickFromEditor_cancels_to_null_on_GoBack()
    {
        var service = NewServiceWithRoot();
        INavigationHost host = service;
        var pickTask = service.PickFromEditor<PickTestView>(0);
        Assert.False(pickTask.IsCompleted);
        host.GoBack();
        Assert.Null(await pickTask);
    }

    [AvaloniaFact]
    public async Task CloseAll_cancels_pending_pick_to_null_and_returns_to_root()
    {
        var service = NewServiceWithRoot();
        INavigationHost host = service;
        var pickTask = service.PickFromEditor<PickTestView>(0);
        service.CloseAll();
        Assert.Null(await pickTask);
        // Back at the root page — no back available.
        Assert.False(host.CanGoBack);
    }

    [AvaloniaFact]
    public void GoBack_pops_editor_pages_back_to_root()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;
        service.Open<NavTestView>();
        service.Open<PickTestView2>();
        Assert.True(host.CanGoBack);
        host.GoBack();
        host.GoBack();
        Assert.False(host.CanGoBack); // back to root
    }

    // A second editor type for multi-page tests.
    public class PickTestView2 : Window, IEditorView
    {
        public PickTestView2() { Content = new TextBlock { Text = "view2" }; }
        public string ViewTitle => "View2";
        public bool IsLoaded => true;
        public void NavigateTo(uint address) { }
    }
}
