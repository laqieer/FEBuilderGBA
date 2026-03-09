using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PortraitViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly PortraitViewerViewModel _vm = new();

        public string ViewTitle => "Portrait Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public PortraitViewerView()
        {
            InitializeComponent();
            PortraitList.SelectedAddressChanged += OnPortraitSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadPortraitList();
                PortraitList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnPortraitSelected(uint addr)
        {
            try
            {
                _vm.LoadPortrait(addr);
                UpdateUI();
                TryShowPortraitImage();
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.OnPortraitSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            PortraitList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            MapPtrBox.Text = $"0x{_vm.MapPointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
            D12Box.Text = $"0x{_vm.MouthPointer:X08}";
            D16Box.Text = $"0x{_vm.ClassCardPointer:X08}";
            B20Box.Value = _vm.MouthX;
            B21Box.Value = _vm.MouthY;
            B22Box.Value = _vm.EyeX;
            B23Box.Value = _vm.EyeY;
            B24Box.Value = _vm.State;
            B25Box.Value = _vm.Padding25;
            B26Box.Value = _vm.Padding26;
            B27Box.Value = _vm.Padding27;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.MapPointer = ParseHexText(MapPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.MouthPointer = ParseHexText(D12Box.Text);
            _vm.ClassCardPointer = ParseHexText(D16Box.Text);
            _vm.MouthX = (uint)(B20Box.Value ?? 0);
            _vm.MouthY = (uint)(B21Box.Value ?? 0);
            _vm.EyeX = (uint)(B22Box.Value ?? 0);
            _vm.EyeY = (uint)(B23Box.Value ?? 0);
            _vm.State = (uint)(B24Box.Value ?? 0);
            _vm.Padding25 = (uint)(B25Box.Value ?? 0);
            _vm.Padding26 = (uint)(B26Box.Value ?? 0);
            _vm.Padding27 = (uint)(B27Box.Value ?? 0);
            _vm.WritePortrait();
            CoreState.Services.ShowInfo("Portrait data written.");
        }

        void TryShowPortraitImage()
        {
            try
            {
                MainPortraitImage.SetImage(_vm.TryLoadMainPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage main failed: {0}", ex.Message);
                MainPortraitImage.SetImage(null);
            }

            try
            {
                MapPortraitImage.SetImage(_vm.TryLoadMapPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage map failed: {0}", ex.Message);
                MapPortraitImage.SetImage(null);
            }

            try
            {
                ClassPortraitImage.SetImage(_vm.TryLoadClassPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage class failed: {0}", ex.Message);
                ClassPortraitImage.SetImage(null);
            }
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await MainPortraitImage.ExportPng(this, "portrait.png");
        }

        public void SelectFirstItem()
        {
            PortraitList.SelectFirst();
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
