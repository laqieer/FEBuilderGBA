using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageSystemAreaView : Window, IEditorView
    {
        readonly ImageSystemAreaViewModel _vm = new();

        public string ViewTitle => "System Area Graphics";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageSystemAreaView()
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
                Log.Error("ImageSystemAreaView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageSystemAreaView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            GBAColorBox.Value = _vm.GBAColor;

            // Decompose GBA BGR555 to R/G/B (each 0-248, step 8)
            uint c = _vm.GBAColor;
            uint r5 = c & 0x1F;
            uint g5 = (c >> 5) & 0x1F;
            uint b5 = (c >> 10) & 0x1F;
            ColorR.Value = r5 * 8;
            ColorG.Value = g5 * 8;
            ColorB.Value = b5 * 8;

            // Color preview
            ColorPreview.Background = new SolidColorBrush(
                Color.FromRgb((byte)(r5 * 8), (byte)(g5 * 8), (byte)(b5 * 8)));
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.GBAColor = (uint)(GBAColorBox.Value ?? 0);
            _vm.Write();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
