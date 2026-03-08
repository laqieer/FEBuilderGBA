using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemShopViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemShopViewerViewModel _vm = new();

        public string ViewTitle => "Item Shop";
        public bool IsLoaded => _vm.CanWrite;

        public ItemShopViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemShopList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemShopViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemShop(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemShopViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            QuantityBox.Value = _vm.Quantity;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ItemId = (uint)(ItemIdBox.Value ?? 0);
            _vm.Quantity = (uint)(QuantityBox.Value ?? 0);
            _vm.WriteItemShop();
            CoreState.Services.ShowInfo("Item Shop data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
