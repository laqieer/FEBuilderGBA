using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectivenessViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemEffectivenessViewerViewModel _vm = new();

        public string ViewTitle => "Item Effectiveness";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemEffectivenessViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemEffectivenessList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemEffectiveness(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdLabel.Text = $"0x{_vm.ClassId:X02} ({_vm.ClassId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
