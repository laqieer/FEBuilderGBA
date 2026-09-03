using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class WeaponTypeComboTests
{
    [AvaloniaFact]
    public void ItemEditor_PreservesUnknownWeaponTypeRoundTrip()
    {
        var view = new ItemEditorView();
        var vm = GetViewModel<ItemEditorViewModel>(view);
        vm.WeaponType = 0x08;

        Invoke(view, "UpdateUI");

        AssertSelectedId(view, 0x08);
        Invoke(view, "ReadFromUI");
        Assert.Equal(0x08u, vm.WeaponType);
    }

    [AvaloniaFact]
    public void ItemFE6Editor_PreservesUnknownWeaponTypeRoundTrip()
    {
        var view = new ItemFE6View();
        var vm = GetViewModel<ItemFE6ViewModel>(view);
        vm.WeaponType = 0x0A;

        Invoke(view, "UpdateUI");

        AssertSelectedId(view, 0x0A);
        Invoke(view, "ReadFromUI");
        Assert.Equal(0x0Au, vm.WeaponType);
    }

    [AvaloniaFact]
    public void ItemEditors_RefreshWeaponTypeLabelsAfterDetachedLanguageChange()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "weapon_type_translation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string firstMap = Path.Combine(tempDir, "first.txt");
        string secondMap = Path.Combine(tempDir, "second.txt");
        File.WriteAllText(firstMap, ":12=Dancer's Ring\n12=[FIRST]\n\n");
        File.WriteAllText(secondMap, ":12=Dancer's Ring\n12=[SECOND]\n\n");

        EditorHostWindow? itemHost = null;
        EditorHostWindow? fe6Host = null;
        try
        {
            MyTranslateResource.LoadResource(firstMap);

            var itemView = new ItemEditorView();
            itemHost = new EditorHostWindow(itemView);
            itemHost.Show();
            var itemVm = GetViewModel<ItemEditorViewModel>(itemView);
            itemVm.WeaponType = 0x00;
            Invoke(itemView, "UpdateUI");
            SelectId(itemView, 0x12);

            var fe6View = new ItemFE6View();
            fe6Host = new EditorHostWindow(fe6View);
            fe6Host.Show();
            var fe6Vm = GetViewModel<ItemFE6ViewModel>(fe6View);
            fe6Vm.WeaponType = 0x00;
            Invoke(fe6View, "UpdateUI");
            SelectId(fe6View, 0x12);

            Assert.Equal("12 [FIRST]", SelectedLabel(itemView));
            Assert.Equal("12 [FIRST]", SelectedLabel(fe6View));
            Assert.Equal(0x00u, itemVm.WeaponType);
            Assert.Equal(0x00u, fe6Vm.WeaponType);

            itemHost.Content = null;
            fe6Host.Content = null;
            Dispatcher.UIThread.RunJobs();

            MyTranslateResource.LoadResource(secondMap);
            CoreState.RaiseLanguageChanged();
            Dispatcher.UIThread.RunJobs();

            itemHost.Content = itemView;
            fe6Host.Content = fe6View;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("12 [SECOND]", SelectedLabel(itemView));
            Assert.Equal("12 [SECOND]", SelectedLabel(fe6View));
            AssertSelectedId(itemView, 0x12);
            AssertSelectedId(fe6View, 0x12);
            Assert.Equal(0x00u, itemVm.WeaponType);
            Assert.Equal(0x00u, fe6Vm.WeaponType);
        }
        finally
        {
            itemHost?.Close();
            fe6Host?.Close();
            MyTranslateResource.Clear();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    static void AssertSelectedId(Control view, uint expectedId)
    {
        var combo = Assert.IsType<ComboBox>(
            view.FindControl<ComboBox>("WeaponTypeCombo"));
        var list = GetWeaponTypeList(view);
        Assert.InRange(combo.SelectedIndex, 0, list.Count - 1);
        Assert.Equal(expectedId, list[combo.SelectedIndex].id);
    }

    static void SelectId(Control view, uint id)
    {
        var combo = Assert.IsType<ComboBox>(
            view.FindControl<ComboBox>("WeaponTypeCombo"));
        var list = GetWeaponTypeList(view);
        combo.SelectedIndex = list.FindIndex(x => x.id == id);
        Assert.True(combo.SelectedIndex >= 0);
    }

    static string SelectedLabel(Control view)
    {
        var combo = Assert.IsType<ComboBox>(
            view.FindControl<ComboBox>("WeaponTypeCombo"));
        return Assert.IsType<string>(combo.SelectedItem);
    }

    static List<(uint id, string name)> GetWeaponTypeList(object view)
    {
        FieldInfo? field = view.GetType().GetField(
            "_weaponTypeList",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<List<(uint id, string name)>>(field!.GetValue(view));
    }

    static T GetViewModel<T>(object view) where T : class
    {
        FieldInfo? field = view.GetType().GetField(
            "_vm",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(view));
    }

    static void Invoke(object target, string methodName)
    {
        MethodInfo? method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, null);
    }
}
