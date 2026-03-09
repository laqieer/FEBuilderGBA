using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageCGFE7UView : Window, IEditorView
    {
        readonly ImageCGFE7UViewModel _vm = new();

        public string ViewTitle => "CG Editor (FE7U)";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageCGFE7UView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageCGFE7UView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageCGFE7UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            string typeDesc = _vm.ImageType == 0 ? "0 (Single)" : _vm.ImageType == 1 ? "1 (10-Split)" : $"0x{_vm.ImageType:X02}";
            ImageTypeLabel.Text = typeDesc;
            ReservedLabel.Text = $"0x{_vm.Reserved1:X02} 0x{_vm.Reserved2:X02} 0x{_vm.Reserved3:X02}";
            SplitImagePtrLabel.Text = $"0x{_vm.SplitImagePtr:X08}";
            TSAPtrLabel.Text = $"0x{_vm.TSAPtr:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePtr:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
