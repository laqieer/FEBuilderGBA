using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ArenaEnemyWeaponViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly ArenaEnemyWeaponViewerViewModel _vm = new();

        public string ViewTitle => "Arena Enemy Weapon";
        public bool IsLoaded => _vm.IsLoaded;

        public ArenaEnemyWeaponViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadArenaEnemyWeaponList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadArenaEnemyWeapon(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponIdLabel.Text = $"0x{_vm.WeaponId:X02} ({_vm.WeaponId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
