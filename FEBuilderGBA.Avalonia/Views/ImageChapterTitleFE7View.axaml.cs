using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageChapterTitleFE7View : Window, IEditorView, IDataVerifiableView
    {
        readonly ImageChapterTitleFE7ViewModel _vm = new();

        public string ViewTitle => "Chapter Title FE7 Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public ImageChapterTitleFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("ImageChapterTitleFE7View.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("ImageChapterTitleFE7View.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SaveImgBox.Value = _vm.P0;
        }

        void Write_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.P0 = (uint)(SaveImgBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("Chapter title FE7 data written.");
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
