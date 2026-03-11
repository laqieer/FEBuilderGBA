using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusUnitsMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusUnitsMenuViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Status Units Menu";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public StatusUnitsMenuView()
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
                var items = _vm.LoadStatusUnitsMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusUnitsMenuView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusUnitsMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusUnitsMenuView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OrderBox.Value = _vm.Order;
            ItemNameTextIdBox.Value = _vm.ItemNameTextId;
            ReferenceDataBox.Value = _vm.ReferenceData;
            RMenuTextIdBox.Value = _vm.RMenuTextId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status Units Menu");
            try
            {
                _vm.Order = (uint)(OrderBox.Value ?? 0);
                _vm.ItemNameTextId = (uint)(ItemNameTextIdBox.Value ?? 0);
                _vm.ReferenceData = (uint)(ReferenceDataBox.Value ?? 0);
                _vm.RMenuTextId = (uint)(RMenuTextIdBox.Value ?? 0);
                _vm.WriteStatusUnitsMenu();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status units menu data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("StatusUnitsMenuView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
