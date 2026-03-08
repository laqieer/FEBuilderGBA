using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDStaffRollView : Window, IEditorView, IDataVerifiableView
    {
        readonly EDStaffRollViewModel _vm = new();

        public string ViewTitle => "Staff Roll Editor";
        public bool IsLoaded => _vm.CanWrite;

        public EDStaffRollView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadEDStaffRollList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EDStaffRollView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEDStaffRoll(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EDStaffRollView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DataPtrBox.Value = _vm.DataPointer;
            PalettePtrBox.Value = _vm.PalettePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.DataPointer = (uint)(DataPtrBox.Value ?? 0);
            _vm.PalettePointer = (uint)(PalettePtrBox.Value ?? 0);
            _vm.WriteEDStaffRoll();
            CoreState.Services?.ShowInfo("Staff roll data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
