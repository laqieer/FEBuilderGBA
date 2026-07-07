using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleBGViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly BattleBGViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Battle Background Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Battle Background Editor", 853, 441, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public BattleBGViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try { var items = _vm.LoadBattleBGList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.ErrorF("BattleBGViewerView.LoadList: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.ErrorF("BattleBGViewerView.OnSelected: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            TsaPtrBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Battle BG");
            try
            {
                _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
                _vm.TSAPointer = ParseHexText(TsaPtrBox.Text);
                _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
                _vm.WriteBattleBG();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Battle Background data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            string? filePath = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
            if (string.IsNullOrEmpty(filePath)) return; // cancelled
            ImportImageFromFile(filePath);
        }

        // #1397 — the FE-Repo button routes through this SAME FromFile import
        // path. strictSize=true (used by FE-Repo) rejects a non-240x160 asset
        // with the existing size error rather than silently cropping/quantizing
        // it; the file-picker keeps the lenient default (strictSize=false).
        void ImportImageFromFile(string filePath, bool strictSize = false)
        {
            try
            {
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 240, 160, 16, strictSize: strictSize);
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Battle BG Image");
                // BattleBG stores all 3 components LZ77-compressed (including palette)
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8, compressPalette: true);

                if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                _undoService.Commit();
                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        // #1397 — FE-Repo button: pick a 240x160 battle background from the
        // FE-Repo "Battle Frames & Backgrounds" folder and route it through the
        // SAME FromFile import path (strictSize so a non-240x160 asset is
        // rejected, not silently cropped).
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await FERepoPickHelper.PickForEditor(TopLevel.GetTopLevel(this) as Window,
                FERepoResourceBrowser.FERepoEditorKind.BattleBackground);
            if (string.IsNullOrEmpty(path)) return;
            ImportImageFromFile(path, strictSize: true);
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(TopLevel.GetTopLevel(this) as Window, "battle_bg.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint palPtr = _vm.PalettePointer;
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                // BattleBG palette is LZ77-compressed
                byte[] pal = LZ77.decompress(rom.Data, palAddr);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                await FileDialogHelper.SavePaletteFileVia(TopLevel.GetTopLevel(this) as Window, "battle_bg_palette.pal", p =>
                {
                    // #1639: write via the SAF bridge so Android content:// targets work.
                    PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(p));
                    File.WriteAllBytes(p, PaletteFormatConverter.ExportToFormat(pal, fmt));
                });
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                string path = await FileDialogHelper.OpenPaletteFile(TopLevel.GetTopLevel(this) as Window);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Battle BG Palette");
                // BattleBG palette is LZ77-compressed
                uint palAddr = ImageImportCore.WriteCompressedToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write palette"); return; }
                _undoService.Commit();
                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
