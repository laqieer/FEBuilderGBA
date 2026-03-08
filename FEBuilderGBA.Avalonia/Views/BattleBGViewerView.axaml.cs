using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleBGViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BattleBGViewerViewModel _vm = new();

        public string ViewTitle => "Battle Background Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BattleBGViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBattleBGList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BattleBGViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BattleBGViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Value = _vm.ImagePointer;
            TsaPtrBox.Value = _vm.TSAPointer;
            PalPtrBox.Value = _vm.PalettePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = (uint)(ImgPtrBox.Value ?? 0);
            _vm.TSAPointer = (uint)(TsaPtrBox.Value ?? 0);
            _vm.PalettePointer = (uint)(PalPtrBox.Value ?? 0);
            _vm.WriteBattleBG();
            CoreState.Services.ShowInfo("Battle Background data written.");
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
