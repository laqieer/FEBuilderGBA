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
            ImgPtrBox.Value = _vm.ImagePointer;
            MapPtrBox.Value = _vm.MapPointer;
            PalPtrBox.Value = _vm.PalettePointer;
            D12Box.Value = _vm.D12;
            D16Box.Value = _vm.D16;
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
            _vm.ImagePointer = (uint)(ImgPtrBox.Value ?? 0);
            _vm.MapPointer = (uint)(MapPtrBox.Value ?? 0);
            _vm.PalettePointer = (uint)(PalPtrBox.Value ?? 0);
            _vm.D12 = (uint)(D12Box.Value ?? 0);
            _vm.D16 = (uint)(D16Box.Value ?? 0);
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
                var image = _vm.TryLoadPortraitImage();
                PortraitImage.SetImage(image);
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.TryShowPortraitImage failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
        }

        public void SelectFirstItem()
        {
            PortraitList.SelectFirst();
        }
    }
}
