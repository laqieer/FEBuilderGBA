using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemWeaponTriangleViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemWeaponTriangleViewerViewModel _vm = new();

        public string ViewTitle => "Weapon Triangle";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemWeaponTriangleViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemWeaponTriangleList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemWeaponTriangleViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemWeaponTriangle(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemWeaponTriangleViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponType1Label.Text = $"0x{_vm.WeaponType1:X02} ({_vm.WeaponType1})";
            WeaponType2Label.Text = $"0x{_vm.WeaponType2:X02} ({_vm.WeaponType2})";
            BonusLabel.Text = _vm.Bonus.ToString();
            PenaltyLabel.Text = _vm.Penalty.ToString();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
