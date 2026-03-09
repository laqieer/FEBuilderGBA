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
            SaveImgBox.Text = $"0x{_vm.SaveImagePointer:X08}";
            ChapterImgBox.Text = $"0x{_vm.ChapterImagePointer:X08}";
            TitleImgBox.Text = $"0x{_vm.TitleImagePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SaveImagePointer = ParseHexText(SaveImgBox.Text);
            _vm.ChapterImagePointer = ParseHexText(ChapterImgBox.Text);
            _vm.TitleImagePointer = ParseHexText(TitleImgBox.Text);
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

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "chapter_title.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        private static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
    }
}
