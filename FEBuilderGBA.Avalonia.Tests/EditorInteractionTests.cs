using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests that instantiate the 5 core editor Views, set their
/// DataContext to a loaded ViewModel, and verify View-level control state.
///
/// Uses RomFixture to share ROM initialization; tests skip when no ROM is available.
/// </summary>
[Collection("SharedState")]
public class EditorInteractionTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;

    public EditorInteractionTests(RomFixture fixture)
    {
        _fixture = fixture;
    }

    // =================================================================
    // UnitEditorView (8 tests)
    // =================================================================

    [AvaloniaFact]
    public void UnitEditorView_CanInstantiateWithDataContext()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new UnitEditorViewModel();
        var list = vm.LoadUnitList();
        if (list.Count < 2) return;
        vm.LoadUnit(list[1].addr);

        var view = new UnitEditorView();
        view.DataContext = vm;

        Assert.NotNull(view);
        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void UnitEditorView_HasUnitListControl()
    {
        var view = new UnitEditorView();
        var unitList = view.FindControl<AddressListControl>("UnitList");
        Assert.NotNull(unitList);
    }

    [AvaloniaFact]
    public void UnitEditorView_HasAddrLabel()
    {
        var view = new UnitEditorView();
        var addrLabel = view.FindControl<TextBlock>("AddrLabel");
        Assert.NotNull(addrLabel);
    }

    [AvaloniaFact]
    public void UnitEditorView_HasNameIdBox()
    {
        var view = new UnitEditorView();
        var nameIdBox = view.FindControl<NumericUpDown>("NameIdBox");
        Assert.NotNull(nameIdBox);
    }

    [AvaloniaFact]
    public void UnitEditorView_HasWriteButton()
    {
        var view = new UnitEditorView();
        var writeButton = view.FindControl<Button>("WriteButton");
        Assert.NotNull(writeButton);
    }

    [AvaloniaFact]
    public void UnitEditorView_HasStatBoxes()
    {
        var view = new UnitEditorView();
        Assert.NotNull(view.FindControl<NumericUpDown>("HPBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("StrBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("SklBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("SpdBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("DefBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("ResBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("LckBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("ConBox"));
    }

    [AvaloniaFact]
    public void UnitEditorView_DataContextBindsToProperties()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new UnitEditorViewModel();
        var list = vm.LoadUnitList();
        if (list.Count < 2) return;
        vm.LoadUnit(list[1].addr);

        var view = new UnitEditorView();
        view.DataContext = vm;

        // The VM should be accessible through DataContext
        var boundVm = view.DataContext as UnitEditorViewModel;
        Assert.NotNull(boundVm);
        Assert.True(boundVm!.CanWrite);
        Assert.NotEqual(0u, boundVm.CurrentAddr);
        Assert.NotEqual(0u, boundVm.NameId);
    }

    [AvaloniaFact]
    public void UnitEditorView_DirtyFlagReflectsVMState()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new UnitEditorViewModel();
        var list = vm.LoadUnitList();
        if (list.Count < 2) return;
        vm.LoadUnit(list[1].addr);
        vm.MarkClean();

        Assert.False(vm.IsDirty);

        // Modifying a property should set dirty
        uint original = vm.Level;
        vm.Level = original + 1;
        Assert.True(vm.IsDirty);

        // MarkClean resets
        vm.MarkClean();
        Assert.False(vm.IsDirty);

        // Restore
        vm.Level = original;
    }

    // =================================================================
    // ClassEditorView (8 tests)
    // =================================================================

    [AvaloniaFact]
    public void ClassEditorView_CanInstantiateWithDataContext()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ClassEditorViewModel();
        var list = vm.LoadClassList();
        if (list.Count < 2) return;
        vm.LoadClass(list[1].addr);

        var view = new ClassEditorView();
        view.DataContext = vm;

        Assert.NotNull(view);
        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void ClassEditorView_HasClassListControl()
    {
        var view = new ClassEditorView();
        var classList = view.FindControl<AddressListControl>("ClassList");
        Assert.NotNull(classList);
    }

    [AvaloniaFact]
    public void ClassEditorView_HasAddrLabel()
    {
        var view = new ClassEditorView();
        var addrLabel = view.FindControl<TextBlock>("AddrLabel");
        Assert.NotNull(addrLabel);
    }

    [AvaloniaFact]
    public void ClassEditorView_HasNameIdBox()
    {
        var view = new ClassEditorView();
        var nameIdBox = view.FindControl<NumericUpDown>("NameIdBox");
        Assert.NotNull(nameIdBox);
    }

    [AvaloniaFact]
    public void ClassEditorView_HasBaseStatBoxes()
    {
        var view = new ClassEditorView();
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseHpBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseStrBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseSklBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseSpdBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseDefBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseResBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseMovBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("BaseConBox"));
    }

    [AvaloniaFact]
    public void ClassEditorView_HasGrowthBoxes()
    {
        var view = new ClassEditorView();
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowHpBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowStrBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowSklBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowSpdBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowDefBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowResBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("GrowLckBox"));
    }

    [AvaloniaFact]
    public void ClassEditorView_DataContextBindsToProperties()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ClassEditorViewModel();
        var list = vm.LoadClassList();
        if (list.Count < 2) return;
        vm.LoadClass(list[1].addr);

        var view = new ClassEditorView();
        view.DataContext = vm;

        var boundVm = view.DataContext as ClassEditorViewModel;
        Assert.NotNull(boundVm);
        Assert.True(boundVm!.CanWrite);
        Assert.NotEqual(0u, boundVm.CurrentAddr);
        Assert.NotEqual(0u, boundVm.ClassNumber);
    }

    [AvaloniaFact]
    public void ClassEditorView_DirtyFlagReflectsVMState()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ClassEditorViewModel();
        var list = vm.LoadClassList();
        if (list.Count < 2) return;
        vm.LoadClass(list[1].addr);

        // LoadClass calls MarkClean at the end
        Assert.False(vm.IsDirty);

        uint original = vm.BaseMov;
        vm.BaseMov = original + 1;
        Assert.True(vm.IsDirty);

        vm.MarkClean();
        Assert.False(vm.IsDirty);

        vm.BaseMov = original;
    }

    // =================================================================
    // ItemEditorView (8 tests)
    // =================================================================

    [AvaloniaFact]
    public void ItemEditorView_CanInstantiateWithDataContext()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEditorViewModel();
        var list = vm.LoadItemList();
        if (list.Count < 2) return;
        vm.LoadItem(list[1].addr);

        var view = new ItemEditorView();
        view.DataContext = vm;

        Assert.NotNull(view);
        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void ItemEditorView_HasItemListControl()
    {
        var view = new ItemEditorView();
        var itemList = view.FindControl<AddressListControl>("ItemList");
        Assert.NotNull(itemList);
    }

    [AvaloniaFact]
    public void ItemEditorView_HasAddrLabel()
    {
        var view = new ItemEditorView();
        var addrLabel = view.FindControl<TextBlock>("AddrLabel");
        Assert.NotNull(addrLabel);
    }

    [AvaloniaFact]
    public void ItemEditorView_HasCombatStatBoxes()
    {
        var view = new ItemEditorView();
        Assert.NotNull(view.FindControl<NumericUpDown>("UsesBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("MightBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("HitBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("WeightBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("CritBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("RangeBox"));
    }

    [AvaloniaFact]
    public void ItemEditorView_HasPriceAndRankBoxes()
    {
        var view = new ItemEditorView();
        Assert.NotNull(view.FindControl<NumericUpDown>("PriceBox"));
        Assert.NotNull(view.FindControl<NumericUpDown>("WeaponRankBox"));
    }

    [AvaloniaFact]
    public void ItemEditorView_HasWeaponTypeCombo()
    {
        var view = new ItemEditorView();
        var combo = view.FindControl<ComboBox>("WeaponTypeCombo");
        Assert.NotNull(combo);
    }

    [AvaloniaFact]
    public void ItemEditorView_DataContextBindsToProperties()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEditorViewModel();
        var list = vm.LoadItemList();
        if (list.Count < 2) return;
        vm.LoadItem(list[1].addr);

        var view = new ItemEditorView();
        view.DataContext = vm;

        var boundVm = view.DataContext as ItemEditorViewModel;
        Assert.NotNull(boundVm);
        Assert.True(boundVm!.CanWrite);
        Assert.NotEqual(0u, boundVm.CurrentAddr);
    }

    [AvaloniaFact]
    public void ItemEditorView_DirtyFlagReflectsVMState()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEditorViewModel();
        var list = vm.LoadItemList();
        if (list.Count < 2) return;
        vm.LoadItem(list[1].addr);
        vm.MarkClean();

        Assert.False(vm.IsDirty);

        uint original = vm.Might;
        vm.Might = original + 1;
        Assert.True(vm.IsDirty);

        vm.MarkClean();
        Assert.False(vm.IsDirty);

        vm.Might = original;
    }

    // =================================================================
    // MapSettingFE6View (8 tests)
    // =================================================================

    [AvaloniaFact]
    public void MapSettingFE6View_CanInstantiateWithDataContext()
    {
        if (!_fixture.IsAvailable) return;
        if (CoreState.ROM?.RomInfo?.version != 6) return;

        var vm = new MapSettingFE6ViewModel();
        var list = vm.LoadMapSettingList();
        if (list.Count < 1) return;
        vm.LoadEntry(list[0].addr);

        var view = new MapSettingFE6View();
        view.DataContext = vm;

        Assert.NotNull(view);
        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_HasEntryListControl()
    {
        var view = new MapSettingFE6View();
        var entryList = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(entryList);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_HasAddrLabel()
    {
        var view = new MapSettingFE6View();
        var addrLabel = view.FindControl<TextBlock>("AddrLabel");
        Assert.NotNull(addrLabel);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_CanInstantiateWithoutCrash()
    {
        var ex = Record.Exception(() => new MapSettingFE6View());
        Assert.Null(ex);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_DataContextAcceptsFE6ViewModel()
    {
        var vm = new MapSettingFE6ViewModel();
        var view = new MapSettingFE6View();
        view.DataContext = vm;

        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_DataContextBindsToProperties()
    {
        if (!_fixture.IsAvailable) return;
        if (CoreState.ROM?.RomInfo?.version != 6) return;

        var vm = new MapSettingFE6ViewModel();
        var list = vm.LoadMapSettingList();
        if (list.Count < 1) return;
        vm.LoadEntry(list[0].addr);

        var view = new MapSettingFE6View();
        view.DataContext = vm;

        var boundVm = view.DataContext as MapSettingFE6ViewModel;
        Assert.NotNull(boundVm);
        Assert.True(boundVm!.IsLoaded);
        Assert.NotEqual(0u, boundVm.CurrentAddr);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_DirtyFlagReflectsVMState()
    {
        var vm = new MapSettingFE6ViewModel();
        vm.MarkClean();
        Assert.False(vm.IsDirty);

        vm.Weather = 5;
        Assert.True(vm.IsDirty);

        vm.MarkClean();
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public void MapSettingFE6View_VMPropertiesDefaultToZero()
    {
        var vm = new MapSettingFE6ViewModel();
        Assert.Equal(0u, vm.CurrentAddr);
        Assert.Equal(0u, vm.FogLevel);
        Assert.Equal(0u, vm.Weather);
        Assert.Equal(0u, vm.PlayerPhaseBGM);
        Assert.Equal(0u, vm.ChapterNumber);
        Assert.False(vm.IsLoaded);
    }

    // =================================================================
    // MapSettingFE7UView (8 tests)
    // =================================================================

    [AvaloniaFact]
    public void MapSettingFE7UView_CanInstantiateWithDataContext()
    {
        if (!_fixture.IsAvailable) return;
        // FE7U version code is 7 with US locale, but we just check non-FE6
        if (CoreState.ROM?.RomInfo?.version == 6) return;
        // Only test on FE7U ROMs
        string romFile = CoreState.ROM?.RomInfo?.VersionToFilename ?? "";
        if (!romFile.Contains("FE7") || romFile.Contains("JP")) return;

        var vm = new MapSettingFE7UViewModel();
        var list = vm.LoadMapSettingList();
        if (list.Count < 1) return;
        vm.LoadEntry(list[0].addr);

        var view = new MapSettingFE7UView();
        view.DataContext = vm;

        Assert.NotNull(view);
        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_HasEntryListControl()
    {
        var view = new MapSettingFE7UView();
        var entryList = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(entryList);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_HasAddrLabel()
    {
        var view = new MapSettingFE7UView();
        var addrLabel = view.FindControl<TextBlock>("AddrLabel");
        Assert.NotNull(addrLabel);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_HasWriteButton()
    {
        var view = new MapSettingFE7UView();
        var writeButton = view.FindControl<Button>("WriteButton");
        Assert.NotNull(writeButton);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_CanInstantiateWithoutCrash()
    {
        var ex = Record.Exception(() => new MapSettingFE7UView());
        Assert.Null(ex);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_DataContextAcceptsFE7UViewModel()
    {
        var vm = new MapSettingFE7UViewModel();
        var view = new MapSettingFE7UView();
        view.DataContext = vm;

        Assert.Same(vm, view.DataContext);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_DirtyFlagReflectsVMState()
    {
        var vm = new MapSettingFE7UViewModel();
        vm.MarkClean();
        Assert.False(vm.IsDirty);

        vm.Weather = 3;
        Assert.True(vm.IsDirty);

        vm.MarkClean();
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public void MapSettingFE7UView_VMPropertiesDefaultToZero()
    {
        var vm = new MapSettingFE7UViewModel();
        Assert.Equal(0u, vm.CurrentAddr);
        Assert.Equal(0u, vm.FogLevel);
        Assert.Equal(0u, vm.Weather);
        Assert.Equal(0u, vm.PlayerPhaseBGM);
        Assert.Equal(0u, vm.BreakableWallHP);
        Assert.Equal(0u, vm.ChapterPointer);
        Assert.False(vm.IsLoaded);
    }
}
