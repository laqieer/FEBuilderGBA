// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;

namespace FEBuilderGBA.Avalonia.Tests;

public sealed class MainWindowTrimmingTests
{
    [AvaloniaFact]
    public void SelectFirstItemOnView_RoutesThroughIEditorView()
    {
        var editor = new ExplicitEditorView();

        InvokeSelectFirstItemOnView(editor);

        Assert.Equal(1, editor.SelectFirstItemCalls);
    }

    [AvaloniaFact]
    public void SelectItemByIndex_UsesTypedAddressListControlVisualTraversal()
    {
        var list = new DerivedAddressListControl();
        list.SetItems(new()
        {
            new AddrResult(0x1000, "First"),
            new AddrResult(0x1004, "Second"),
            new AddrResult(0x1008, "Third"),
        });
        var host = new StackPanel();
        NameScope.SetNameScope(host, new NameScope());
        host.Children.Add(list);

        bool selected = InvokeSelectItemByIndex(host, 2);

        Assert.True(selected);
        Assert.Equal(2, list.SelectedOriginalIndex);
    }

    [Fact]
    public void MainWindowSource_DoesNotUseReflectionForEditorSelection()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "FEBuilderGBA.Avalonia",
            "Views",
            "MainWindow.axaml.cs"));

        Assert.DoesNotContain("GetMethod(\"SelectFirstItem\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod(\"SelectByIndex\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection.MethodInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Reflection;", source, StringComparison.Ordinal);
    }

    static void InvokeSelectFirstItemOnView(Control control)
    {
        typeof(MainWindow)
            .GetMethod("SelectFirstItemOnView", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { control });
    }

    static bool InvokeSelectItemByIndex(Control control, int index)
    {
        var method = typeof(MainWindow).GetMethod(
            "SelectItemByIndex",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Control), typeof(int) },
            modifiers: null)!;
        return (bool)method.Invoke(null, new object[] { control, index })!;
    }

    static string FindRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate FEBuilderGBA.sln from " + AppContext.BaseDirectory);
    }

    sealed class ExplicitEditorView : UserControl, IEditorView
    {
        public int SelectFirstItemCalls { get; private set; }
        public string ViewTitle => "Explicit editor";
        bool IEditorView.IsLoaded => true;
        public void NavigateTo(uint address) { }
        void IEditorView.SelectFirstItem() => SelectFirstItemCalls++;
    }

    sealed class DerivedAddressListControl : AddressListControl;
}
