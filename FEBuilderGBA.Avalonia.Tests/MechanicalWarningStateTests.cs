using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

public class MechanicalWarningStateTests
{
    [AvaloniaFact]
    public void EmbeddableDialogs_ReportReadyThroughEditorInterfaceBeforeVisualAttach()
    {
        var message = new MessageBoxContent();
        var number = new NumberInputContent();

        Assert.True(((IEditorView)message).IsLoaded);
        Assert.True(((IEditorView)number).IsLoaded);
        Assert.False(((Control)message).IsLoaded);
        Assert.False(((Control)number).IsLoaded);
    }

    [AvaloniaFact]
    public void EventTemplateView_ReportsReadyThroughEditorInterfaceBeforeWindowOpens()
    {
        var view = new EventTemplate1View();

        Assert.True(((IEditorView)view).IsLoaded);
        Assert.False(((Window)view).IsLoaded);
    }

    [Fact]
    public void EventScriptViewModel_DirtyFlagRemainsExplicitStructuralState()
    {
        var vm = new EventScriptViewModel();
        var changed = TrackChangedProperties(vm);

        vm.CurrentAddr = 0x123456;

        Assert.False(vm.IsDirty);
        Assert.Contains(nameof(EventScriptViewModel.CurrentAddr), changed);

        vm.IsDirty = true;

        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(EventScriptViewModel.IsDirty), changed);
    }

    [Fact]
    public void AIScriptViewModel_LoadingFlagNotifiesAndDoesNotSuppressInheritedDirtyState()
    {
        var vm = new AIScriptViewModel();
        var changed = TrackChangedProperties(vm);

        vm.IsLoading = true;
        vm.FilterIndex = 1;

        Assert.True(vm.IsLoading);
        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(AIScriptViewModel.IsLoading), changed);
        Assert.Contains(nameof(AIScriptViewModel.FilterIndex), changed);
    }

    [Fact]
    public void MapTerrainBGLookupTableViewModel_LoadingFlagNotifiesAndDoesNotSuppressInheritedDirtyState()
    {
        var vm = new MapTerrainBGLookupTableViewModel();
        var changed = TrackChangedProperties(vm);

        vm.IsLoading = true;
        vm.FilterIndex = 1;

        Assert.True(vm.IsLoading);
        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(MapTerrainBGLookupTableViewModel.IsLoading), changed);
        Assert.Contains(nameof(MapTerrainBGLookupTableViewModel.FilterIndex), changed);
    }

    [Fact]
    public void MapTerrainFloorLookupTableViewModel_LoadingFlagNotifiesAndDoesNotSuppressInheritedDirtyState()
    {
        var vm = new MapTerrainFloorLookupTableViewModel();
        var changed = TrackChangedProperties(vm);

        vm.IsLoading = true;
        vm.FilterIndex = 1;

        Assert.True(vm.IsLoading);
        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(MapTerrainFloorLookupTableViewModel.IsLoading), changed);
        Assert.Contains(nameof(MapTerrainFloorLookupTableViewModel.FilterIndex), changed);
    }

    static List<string> TrackChangedProperties(ViewModelBase vm)
    {
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changed.Add(e.PropertyName);
            }
        };
        return changed;
    }
}
