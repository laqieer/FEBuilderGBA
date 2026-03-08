using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPPrologueViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPPrologueViewerViewModel _vm = new();

        public string ViewTitle => "OP Prologue Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPPrologueViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadOPPrologueList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadOPPrologue(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Value = _vm.ImagePointer;
            TsaPtrBox.Value = _vm.TSAPointer;
            PalAddrLabel.Text = $"0x{_vm.PaletteColorPointer:X08}";
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
            _vm.TSAPointer = (uint)(TsaPtrBox.Value ?? 0);
            _vm.WriteOPPrologue();
            CoreState.Services?.ShowInfo("OP Prologue data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
