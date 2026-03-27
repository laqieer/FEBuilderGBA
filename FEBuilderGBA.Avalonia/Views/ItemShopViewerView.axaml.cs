using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemShopViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemShopViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
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
                _vm.IsLoading = true;
                _vm.LoadItemShop(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
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

            _undoService.Begin("Edit Item Shop");
            try
            {
                _vm.WriteItemShop();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Shop data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        void OnItemIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint itemId = (uint)(ItemIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.item_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + itemId * rom.RomInfo.item_datasize;
                if (rom.RomInfo.version == 6)
                    WindowManager.Instance.Navigate<ItemFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("OnItemIdLinkClick failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
