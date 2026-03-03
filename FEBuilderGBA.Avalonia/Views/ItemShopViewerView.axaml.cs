using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemShopViewerView : Window, IEditorView
    {
        readonly ItemShopViewerViewModel _vm = new();

        public string ViewTitle => "Item Shop";
        public bool IsLoaded => _vm.IsLoaded;

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
            ItemIdLabel.Text = $"0x{_vm.ItemId:X02} ({_vm.ItemId})";
            QuantityLabel.Text = _vm.Quantity.ToString();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
