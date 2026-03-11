using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusOptionOrderView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusOptionOrderViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Status Option Order";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public StatusOptionOrderView()
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
                var items = _vm.LoadStatusOptionOrderList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusOptionOrderView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusOptionOrder(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusOptionOrderView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OptionIdBox.Value = _vm.OptionId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status Option Order");
            try
            {
                _vm.OptionId = (uint)(OptionIdBox.Value ?? 0);
                _vm.WriteStatusOptionOrder();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status option order data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("StatusOptionOrderView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
