// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — WindowManager facade + INavigationService delegation tests.
//
//   - WindowManager public API stability (reflection): the ~356 call sites must
//     keep compiling, so the method names + generic constraints + return types
//     are asserted unchanged.
//   - Desktop service selection: the default service on a non-Android host is
//     the behavior-identical DesktopNavigationService.
//   - Delegation/regression: every WindowManager.* call routes to the active
//     INavigationService — proven by injecting a recording fake and asserting
//     the call landed (no real window shown). The delegation facts use
//     [AvaloniaFact] because the fake's generic returns construct a Window
//     (which needs the headless UI thread).
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

// A fake service that records which method was invoked, so we can prove the
// WindowManager facade delegates without showing any real window.
sealed class RecordingNavigationService : INavigationService
{
    public string? LastCall;
    public Type? LastType;
    public uint LastAddress;
    public Window? MainWindow { get; set; }
    public Window? ActiveEditorWindow => null;

    public T Open<T>() where T : Control, new()
    {
        LastCall = "Open"; LastType = typeof(T);
        return new T();
    }

    public T Navigate<T>(uint address) where T : Control, IEditorView, new()
    {
        LastCall = "Navigate"; LastType = typeof(T); LastAddress = address;
        return new T();
    }

    public Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
    {
        LastCall = "OpenModal"; LastType = typeof(T);
        return Task.FromResult(new T());
    }

    public Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
        where T : Control, IPickableEditor, new()
    {
        LastCall = "PickFromEditor"; LastType = typeof(T); LastAddress = navigateAddress;
        return Task.FromResult<PickResult?>(new PickResult(1, 0x100, "x"));
    }

    public T? FindOpen<T>() where T : Control
    {
        LastCall = "FindOpen"; LastType = typeof(T);
        return null;
    }

    public void CloseAll() => LastCall = "CloseAll";
}

public class NavigationServiceReflectionTests
{
    [Fact]
    public void Default_service_on_desktop_is_DesktopNavigationService()
    {
        // The tests run on a desktop TFM (net9.0, non-Android), so the service
        // WindowManager's factory chooses must be the behavior-identical
        // window-based desktop impl.
        Assert.False(OperatingSystem.IsAndroid());
        INavigationService chosen = OperatingSystem.IsAndroid()
            ? new AndroidNavigationService()
            : new DesktopNavigationService();
        Assert.IsType<DesktopNavigationService>(chosen);
    }

    [Fact]
    public void WindowManager_public_method_signatures_are_unchanged()
    {
        Type wm = typeof(WindowManager);

        AssertMethod(wm, "Open", returnIsGenericParam: true, paramCount: 0);
        AssertMethod(wm, "Navigate", returnIsGenericParam: true, paramCount: 1, firstParamType: typeof(uint));
        AssertMethod(wm, "FindOpen", returnIsGenericParam: true, paramCount: 0); // returns T? (nullable T)
        AssertMethod(wm, "OpenModal", returnIsGenericParam: false, paramCount: 1); // Task<T>
        AssertMethod(wm, "PickFromEditor", returnIsGenericParam: false, paramCount: 2); // Task<PickResult?>
        AssertMethod(wm, "CloseAll", returnIsGenericParam: false, paramCount: 0);

        // MainWindow property still present, Window?-typed.
        PropertyInfo? mainWindow = wm.GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(mainWindow);
        Assert.Equal(typeof(Window), mainWindow!.PropertyType);
        Assert.True(mainWindow.CanRead && mainWindow.CanWrite);
    }

    [Fact]
    public void WindowManager_generic_constraints_are_unchanged()
    {
        Type wm = typeof(WindowManager);

        // Navigate<T> : T must be IEditorView; PickFromEditor<T> : T must be IPickableEditor; Control enables single-view embeddable editors.
        AssertConstraint(wm, "Navigate", typeof(IEditorView));
        AssertConstraint(wm, "PickFromEditor", typeof(IPickableEditor));
    }

    static void AssertConstraint(Type t, string name, Type expectedInterface)
    {
        MethodInfo m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).First(x => x.Name == name);
        Type tArg = m.GetGenericArguments()[0];
        Type[] constraints = tArg.GetGenericParameterConstraints();
        Assert.Contains(typeof(Control), constraints);
        Assert.Contains(expectedInterface, constraints);
    }

    static void AssertMethod(Type t, string name, bool returnIsGenericParam, int paramCount, Type? firstParamType = null)
    {
        MethodInfo? m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(x => x.Name == name);
        Assert.True(m != null, $"WindowManager.{name} must remain a public instance method.");
        Assert.Equal(paramCount, m!.GetParameters().Length);
        if (firstParamType != null)
            Assert.Equal(firstParamType, m.GetParameters()[0].ParameterType);
        if (returnIsGenericParam)
            Assert.True(m.ReturnType.IsGenericParameter,
                $"WindowManager.{name} should return the generic view type T.");
    }
}

[Collection("WindowManagerSerial")]
public class NavigationServiceDelegationTests
{
    static void WithFakeService(Action<RecordingNavigationService> body)
    {
        var original = WindowManager.Instance.Service;
        var fake = new RecordingNavigationService();
        try
        {
            WindowManager.Instance.SetService(fake);
            body(fake);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public void Open_delegates_to_active_service()
    {
        WithFakeService(fake =>
        {
            WindowManager.Instance.Open<FakeEditorWindow>();
            Assert.Equal("Open", fake.LastCall);
            Assert.Equal(typeof(FakeEditorWindow), fake.LastType);
        });
    }

    [AvaloniaFact]
    public void Navigate_delegates_with_address()
    {
        WithFakeService(fake =>
        {
            WindowManager.Instance.Navigate<FakeEditorWindow>(0xABCD);
            Assert.Equal("Navigate", fake.LastCall);
            Assert.Equal(typeof(FakeEditorWindow), fake.LastType);
            Assert.Equal(0xABCDu, fake.LastAddress);
        });
    }

    [AvaloniaFact]
    public void OpenModal_delegates()
    {
        WithFakeService(fake =>
        {
            _ = WindowManager.Instance.OpenModal<FakeEditorWindow>();
            Assert.Equal("OpenModal", fake.LastCall);
        });
    }

    [AvaloniaFact]
    public async Task PickFromEditor_delegates_and_returns_result()
    {
        var original = WindowManager.Instance.Service;
        var fake = new RecordingNavigationService();
        PickResult? result;
        try
        {
            WindowManager.Instance.SetService(fake);
            result = await WindowManager.Instance.PickFromEditor<FakePickableWindow>(0x55);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
        Assert.Equal("PickFromEditor", fake.LastCall);
        Assert.Equal(0x55u, fake.LastAddress);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Index);
    }

    [AvaloniaFact]
    public void FindOpen_and_CloseAll_delegate()
    {
        WithFakeService(fake =>
        {
            WindowManager.Instance.FindOpen<FakeEditorWindow>();
            Assert.Equal("FindOpen", fake.LastCall);
            WindowManager.Instance.CloseAll();
            Assert.Equal("CloseAll", fake.LastCall);
        });
    }

    [AvaloniaFact]
    public void OpenAsTopLevel_throws_for_non_desktop_service()
    {
        WithFakeService(fake =>
        {
            var ex = Assert.Throws<NotSupportedException>(
                () => WindowManager.Instance.OpenAsTopLevel<FakeEditorWindow>());
            Assert.Equal("OpenAsTopLevel is only supported by the desktop navigation service.", ex.Message);
            Assert.Null(fake.LastCall);
        });
    }

    [AvaloniaFact]
    public void SetService_carries_MainWindow_across_swap()
    {
        var original = WindowManager.Instance.Service;
        try
        {
            var a = new RecordingNavigationService();
            WindowManager.Instance.SetService(a);
            WindowManager.Instance.MainWindow = null;
            Assert.Null(a.MainWindow);

            var b = new RecordingNavigationService();
            WindowManager.Instance.SetService(b);
            // SetService copies MainWindow from the outgoing service.
            Assert.Equal(a.MainWindow, b.MainWindow);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }
}

// Minimal fake views used by the delegation tests. They satisfy the generic
// constraints (Window + IEditorView / IPickableEditor + new()).
public class FakeEditorWindow : Window, IEditorView
{
    public string ViewTitle => "Fake";
    public bool IsLoaded => true;
    public void NavigateTo(uint address) { }
}

public class FakePickableWindow : Window, IPickableEditor
{
    public string ViewTitle => "FakePick";
    public bool IsLoaded => true;
    public void NavigateTo(uint address) { }
    public event Action<PickResult>? SelectionConfirmed;
    public void EnablePickMode() { _ = SelectionConfirmed; }
}
