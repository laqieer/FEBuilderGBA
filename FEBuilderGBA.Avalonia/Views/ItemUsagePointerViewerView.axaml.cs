using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemUsagePointerViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemUsagePointerViewerViewModel _vm = new();

        public string ViewTitle => "Item Usage Pointer";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemUsagePointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemUsagePointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemUsagePointerViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemUsagePointer(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemUsagePointerViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UsabilityPointerLabel.Text = $"0x{_vm.UsabilityPointer:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
