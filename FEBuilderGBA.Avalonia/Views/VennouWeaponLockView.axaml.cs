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

        public string ViewTitle => "Weapon Lock (Vennou) Editor";
        public bool IsLoaded => _vm.CanWrite;

        public VennouWeaponLockView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
            }
            catch (Exception ex) { Log.Error("VennouWeaponLockView.NavigateTo: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            LockTypeBox.Value = _vm.LockTypeOrId;
            LinkedNameLabel.Text = _vm.LinkedName;
            ExplanationBox.Text = _vm.Explanation;
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
                CoreState.Services?.ShowInfo("Weapon lock data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("VennouWeaponLockView.Write: {0}", ex.Message); }
        }
    }
}
