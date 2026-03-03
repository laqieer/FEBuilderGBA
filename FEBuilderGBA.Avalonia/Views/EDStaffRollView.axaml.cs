using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDStaffRollView : Window, IEditorView
    {
        readonly EDStaffRollViewModel _vm = new();

        public string ViewTitle => "Staff Roll";
        public bool IsLoaded => _vm.IsLoaded;

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
            DataPtrLabel.Text = $"0x{_vm.DataPointer:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
