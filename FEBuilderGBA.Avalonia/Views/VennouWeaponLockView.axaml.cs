using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class VennouWeaponLockView : Window, IEditorView, IDataVerifiableView
    {
        readonly VennouWeaponLockViewModel _vm = new();
        readonly UndoService _undoService = new();
        uint _baseAddr;

        public string ViewTitle => "Weapon Lock (Vennou) Editor";
        public bool IsLoaded => _vm.CanWrite;

        public VennouWeaponLockView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            LoadList();
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        void LoadList()
        {
            var items = _vm.BuildList(_baseAddr);
            EntryList.SetItems(items);
        }

        void OnSelected(uint address)
        {
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
            }
            catch (Exception ex) { Log.Error("VennouWeaponLockView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            LockTypeBox.Value = _vm.LockTypeOrId;
            LinkedNameLabel.Text = _vm.LinkedName;
            ExplanationBox.Text = _vm.Explanation;
            FieldLabelText.Text = _vm.FieldLabel + ":";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Weapon Lock");
            try
            {
                _vm.LockTypeOrId = (uint)(LockTypeBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                LoadList(); // Refresh list after write
                CoreState.Services?.ShowInfo("Weapon lock data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("VennouWeaponLockView.Write: {0}", ex.Message); }
        }
    }
}
