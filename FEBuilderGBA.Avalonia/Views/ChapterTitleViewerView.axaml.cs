using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ChapterTitleViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly ChapterTitleViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

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
            _vm.IsLoading = true;
            try { var items = _vm.LoadChapterTitleList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("ChapterTitleViewerView.LoadList: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadChapterTitle(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("ChapterTitleViewerView.OnSelected: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
            _undoService.Begin("Edit Chapter Title");
            try
            {
                _vm.SaveImagePointer = ParseHexText(SaveImgBox.Text);
                _vm.ChapterImagePointer = ParseHexText(ChapterImgBox.Text);
                _vm.TitleImagePointer = ParseHexText(TitleImgBox.Text);
                _vm.WriteChapterTitle();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Chapter Title data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ChapterTitleViewerView.Write: {0}", ex.Message); }
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

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint palAddr = rom.RomInfo.image_chapter_title_palette;
                if (palAddr == 0 || !U.isSafetyOffset(palAddr)) { CoreState.Services.ShowError("No palette address"); return; }
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 16);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "chapter_title_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(pal, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Chapter title images use a shared palette at rom.RomInfo.image_chapter_title_palette
                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint paletteAddr = rom.RomInfo.image_chapter_title_palette;
                if (paletteAddr == 0 || !U.isSafetyOffset(paletteAddr))
                {
                    CoreState.Services.ShowError("Could not locate shared chapter title palette");
                    return;
                }
                byte[] existingPalette = ImageUtilCore.GetPalette(paletteAddr, 16);
                if (existingPalette == null) { CoreState.Services.ShowError("Failed to read palette"); return; }

                var loadResult = await ImageImportService.LoadAndRemapToExistingPalette(this, 256, 0, existingPalette, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                _undoService.Begin("Import Chapter Title Image");
                try
                {
                    uint addr = _vm.CurrentAddr;
                    byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                    if (tileData == null) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to encode tile data"); return; }

                    uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                    if (writeAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write compressed tile data (no free space)"); return; }

                    _undoService.Commit();
                    _vm.LoadChapterTitle(addr);
                    UpdateUI();
                    LoadImage();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Chapter title image imported successfully.");
                }
                catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
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
