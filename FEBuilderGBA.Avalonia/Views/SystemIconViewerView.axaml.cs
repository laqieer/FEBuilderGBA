using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SystemIconViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SystemIconViewerViewModel _vm = new();

        public string ViewTitle => "System Icon Viewer";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public SystemIconViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadSystemIconList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("SystemIconViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                // addr here is the icon index (stored in AddrResult.addr)
                _vm.LoadSystemIconByIndex(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("SystemIconViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            IconIndexLabel.Text = $"0x{_vm.IconIndex:X02} ({_vm.IconIndex})";
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrLabel.Text = $"0x{_vm.ImageGbaPointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PaletteGbaPointer:X08}";
            TileOffsetLabel.Text = $"0x{_vm.TileOffset:X04}";
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "system_icon.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
