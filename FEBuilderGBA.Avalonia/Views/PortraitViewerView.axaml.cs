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
            D12Box.Text = $"0x{_vm.D12:X08}";
            D16Box.Text = $"0x{_vm.D16:X08}";
            B20Box.Value = _vm.B20;
            B21Box.Value = _vm.B21;
            B22Box.Value = _vm.B22;
            B23Box.Value = _vm.B23;
            B24Box.Value = _vm.B24;
            B25Box.Value = _vm.B25;
            B26Box.Value = _vm.B26;
            B27Box.Value = _vm.B27;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.MapPointer = ParseHexText(MapPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.D12 = ParseHexText(D12Box.Text);
            _vm.D16 = ParseHexText(D16Box.Text);
            _vm.B20 = (uint)(B20Box.Value ?? 0);
            _vm.B21 = (uint)(B21Box.Value ?? 0);
            _vm.B22 = (uint)(B22Box.Value ?? 0);
            _vm.B23 = (uint)(B23Box.Value ?? 0);
            _vm.B24 = (uint)(B24Box.Value ?? 0);
            _vm.B25 = (uint)(B25Box.Value ?? 0);
            _vm.B26 = (uint)(B26Box.Value ?? 0);
            _vm.B27 = (uint)(B27Box.Value ?? 0);
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
