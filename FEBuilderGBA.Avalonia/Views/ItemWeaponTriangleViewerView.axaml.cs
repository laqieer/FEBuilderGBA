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
        public bool IsLoaded => _vm.CanWrite;

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
            WeaponType1Box.Value = _vm.WeaponType1;
            WeaponType2Box.Value = _vm.WeaponType2;
            BonusBox.Value = _vm.Bonus;
            PenaltyBox.Value = _vm.Penalty;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.WeaponType1 = (uint)(WeaponType1Box.Value ?? 0);
            _vm.WeaponType2 = (uint)(WeaponType2Box.Value ?? 0);
            _vm.Bonus = (uint)(BonusBox.Value ?? 0);
            _vm.Penalty = (uint)(PenaltyBox.Value ?? 0);
            _vm.WriteItemWeaponTriangle();
            CoreState.Services.ShowInfo("Weapon Triangle data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
