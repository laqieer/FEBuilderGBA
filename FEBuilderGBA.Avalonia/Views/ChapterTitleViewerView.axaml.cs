using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ChapterTitleViewerView : Window, IEditorView
    {
        readonly ChapterTitleViewerViewModel _vm = new();

        public string ViewTitle => "Chapter Title Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        public ChapterTitleViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadChapterTitleList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("ChapterTitleViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadChapterTitle(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("ChapterTitleViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SaveImgLabel.Text = $"0x{_vm.SaveImagePointer:X08}";
            ChapterImgLabel.Text = $"0x{_vm.ChapterImagePointer:X08}";
            TitleImgLabel.Text = $"0x{_vm.TitleImagePointer:X08}";
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
