using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ChapterTitleViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly ChapterTitleViewerViewModel _vm = new();

        public string ViewTitle => "Chapter Title Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

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
            SaveImgBox.Value = _vm.SaveImagePointer;
            ChapterImgBox.Value = _vm.ChapterImagePointer;
            TitleImgBox.Value = _vm.TitleImagePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SaveImagePointer = (uint)(SaveImgBox.Value ?? 0);
            _vm.ChapterImagePointer = (uint)(ChapterImgBox.Value ?? 0);
            _vm.TitleImagePointer = (uint)(TitleImgBox.Value ?? 0);
            _vm.WriteChapterTitle();
            CoreState.Services.ShowInfo("Chapter Title data written.");
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
