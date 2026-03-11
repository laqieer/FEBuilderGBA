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
        readonly UndoService _undoService = new();

        public string ViewTitle => "Arena Enemy Weapon";
        public bool IsLoaded => _vm.CanWrite;

        public ArenaEnemyWeaponViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadArenaEnemyWeaponList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadArenaEnemyWeapon(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponIdBox.Value = _vm.WeaponId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Arena Enemy Weapon");
            try
            {
                _vm.WeaponId = (uint)(WeaponIdBox.Value ?? 0);
                _vm.WriteArenaEnemyWeapon();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Arena Enemy Weapon data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ArenaEnemyWeaponViewerView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
