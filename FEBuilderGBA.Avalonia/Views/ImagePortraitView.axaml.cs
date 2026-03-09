using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitView : Window, IEditorView
    {
        readonly ImagePortraitViewModel _vm = new();

        public string ViewTitle => "Portrait Image Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitView()
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
                Log.Error("ImagePortraitView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImagePortraitView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PortraitImagePtrLabel.Text = $"0x{_vm.PortraitImagePtr:X08}";
            MiniPortraitPtrLabel.Text = $"0x{_vm.MiniPortraitPtr:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePtr:X08}";
            MouthFramesPtrLabel.Text = $"0x{_vm.MouthFramesPtr:X08}";
            ClassCardPtrLabel.Text = $"0x{_vm.ClassCardPtr:X08}";
            MouthXLabel.Text = _vm.MouthX.ToString();
            MouthYLabel.Text = _vm.MouthY.ToString();
            EyeXLabel.Text = _vm.EyeX.ToString();
            EyeYLabel.Text = _vm.EyeY.ToString();
            StatusLabel.Text = $"0x{_vm.Status:X02}";
            Unused25Label.Text = $"0x{_vm.Unused25:X02}";
            Unused26Label.Text = $"0x{_vm.Unused26:X02}";
            Unused27Label.Text = $"0x{_vm.Unused27:X02}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
