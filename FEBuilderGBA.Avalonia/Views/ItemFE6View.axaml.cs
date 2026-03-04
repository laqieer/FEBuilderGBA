using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly ItemFE6ViewModel _vm = new();

        public string ViewTitle => "Items (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItem(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameIdLabel.Text = $"0x{_vm.NameId:X04}";
            DescIdLabel.Text = $"0x{_vm.DescId:X04}";
            DescId2Label.Text = $"0x{_vm.DescId2:X04}";
            ItemTypeLabel.Text = $"0x{_vm.ItemType:X02} ({_vm.ItemType})";
            ItemNumberLabel.Text = $"0x{_vm.ItemNumber:X02} ({_vm.ItemNumber})";
            StatBonusPtrLabel.Text = $"0x{_vm.StatBonusPtr:X08}";
            EffectivenessPtrLabel.Text = $"0x{_vm.EffectivenessPtr:X08}";
            PricePerUseLabel.Text = $"{_vm.PricePerUse}";
            UsesLabel.Text = $"{_vm.Uses}";
            WeaponEffectLabel.Text = $"0x{_vm.WeaponEffect:X02} ({_vm.WeaponEffect})";
            DecodedNameLabel.Text = _vm.Name;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
