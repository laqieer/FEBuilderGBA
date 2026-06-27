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
    public partial class LinkArenaDenyUnitViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly LinkArenaDenyUnitViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Link Arena Deny Unit";
        public bool IsLoaded => _vm.CanWrite;

        public LinkArenaDenyUnitViewerView()
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
                var items = _vm.LoadLinkArenaDenyUnitList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("LinkArenaDenyUnitViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadLinkArenaDenyUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("LinkArenaDenyUnitViewerView.OnSelected failed: {0}", ex.Message);
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
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Link Arena Deny Unit");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                _vm.WriteLinkArenaDenyUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Link Arena Deny Unit data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("LinkArenaDenyUnitViewerView.Write: {0}", ex.Message); }
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
            catch (Exception ex) { Log.ErrorF("LinkArenaDenyUnitViewerView.UnitId_Jump failed: {0}", ex.Message); }
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
            catch (Exception ex) { Log.ErrorF("LinkArenaDenyUnitViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
