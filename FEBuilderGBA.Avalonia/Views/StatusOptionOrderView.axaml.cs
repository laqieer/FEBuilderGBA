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
            try
            {
                var items = _vm.LoadStatusOptionOrderList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusOptionOrderView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadStatusOptionOrder(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusOptionOrderView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OptionIdBox.Value = _vm.OptionId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.OptionId = (uint)(OptionIdBox.Value ?? 0);
            _vm.WriteStatusOptionOrder();
            CoreState.Services?.ShowInfo("Status option order data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
