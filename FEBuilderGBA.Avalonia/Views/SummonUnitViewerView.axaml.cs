using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonUnitViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SummonUnitViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Summon Unit";
        public bool IsLoaded => _vm.CanWrite;

        public SummonUnitViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonUnitList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            // UnitId is a 1-based ROM unit ID; GetUnitNameByOneBasedId handles
            // the 0/bounds cases and the 1-based → 0-based conversion (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.UnitId); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
            // #950 T4: Summoned Unit (B1) is also a 1-based unit ID; populate the
            // IdFieldControl value + inline unit-name preview like Summoner (B0).
            UnknownBox.Value = _vm.Unknown;
            try { UnknownBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.Unknown); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Summon Unit");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                // #950 T4: IdFieldControl.Value is a non-nullable uint.
                _vm.Unknown = UnknownBox.Value;
                _vm.WriteSummonUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Summon unit data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("SummonUnitViewerView.Write: {0}", ex.Message); }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                {
                    // PickResult.Index is 0-based; UnitId is 1-based (#937).
                    UnitIdBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
                }
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        // -- Summoned Unit IdFieldControl handlers (#950 T4) -----------------
        // B1 is a 1-based unit ID, identical handling to Summoner (B0): the
        // 1-based (id-1)+FE6-dummy-skip nav helper for Jump, and the
        // OneBasedIdFromPickIndex conversion on the Pick result.

        void SummonedUnit_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnknownBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.SummonedUnit_Jump failed: {0}", ex.Message); }
        }

        async void SummonedUnit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnknownBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                {
                    UnknownBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
                }
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.SummonedUnit_Pick failed: {0}", ex.Message); }
        }

        void SummonedUnit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { UnknownBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
