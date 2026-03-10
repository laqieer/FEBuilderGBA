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
            SaveImgBox.Text = $"0x{_vm.P0:X08}";
        }

        void Write_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.P0 = ParseHexText(SaveImgBox.Text);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("Chapter title FE7 data written.");
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
            await ImageDisplay.ExportPng(this, "chapter_title_fe7.png");
        }

        async void ImportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                // FE7 chapter title: LZ77 compressed 4bpp tiles, variable size
                var loadResult = await ImageImportService.LoadAndQuantize(this, 256, 0, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                // FE7 chapter title has a single pointer at addr+0 (P0 = SaveImagePointer)
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { CoreState.Services.ShowError("Failed to encode tile data"); return; }

                uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr);
                if (writeAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write compressed tile data (no free space)"); return; }

                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Chapter title FE7 image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
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
