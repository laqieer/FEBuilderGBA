using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassFontViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassFontViewerViewModel _vm = new();

        public string ViewTitle => "OP Class Font Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassFontViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadOPClassFontList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPClassFontViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadOPClassFont(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("OPClassFontViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Value = _vm.ImagePointer;
        }

        void LoadImage()
        {
            try
            {
                byte[] rgba = _vm.TryLoadImage(out int w, out int h);
                if (rgba != null && w > 0 && h > 0)
                    ImageDisplay.SetRgbaData(rgba, w, h);
                else
                    ImageDisplay.SetImage(null);
            }
            catch { ImageDisplay.SetImage(null); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ImagePointer = (uint)(ImgPtrBox.Value ?? 0);
            _vm.WriteOPClassFont();
            CoreState.Services?.ShowInfo("OP Class Font data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
