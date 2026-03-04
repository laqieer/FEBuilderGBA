using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemPromotionViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemPromotionViewerViewModel _vm = new();

        public string ViewTitle => "Item Promotion";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemPromotionViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemPromotionList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemPromotion(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TargetClassIdLabel.Text = $"0x{_vm.TargetClassId:X02} ({_vm.TargetClassId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
