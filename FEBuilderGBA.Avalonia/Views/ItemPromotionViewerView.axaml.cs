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
        public bool IsLoaded => _vm.CanWrite;

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
            TargetClassIdBox.Value = _vm.TargetClassId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.TargetClassId = (uint)(TargetClassIdBox.Value ?? 0);
            _vm.WriteItemPromotion();
            CoreState.Services.ShowInfo("Item Promotion data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
