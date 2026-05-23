using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBGView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageBGViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Background Image Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBGView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // Wire Comment lost-focus → save (mirrors WF
            // InputFormRef.OnComment_TextChanged save path).
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

            // Enable drag-and-drop for image files
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        void OnDragOver(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files)) { e.DragEffects = DragDropEffects.None; return; }
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f.Path.LocalPath).ToLowerInvariant();
                    if (ext == ".png" || ext == ".bmp") { e.DragEffects = DragDropEffects.Copy; return; }
                }
            }
            e.DragEffects = DragDropEffects.None;
        }

        void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files == null) return;

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".bmp")
                {
                    ImportImageFromFile(path);
                    return;
                }
            }
        }

        void ImportImageFromFile(string filePath)
        {
            try
            {
                // Reserve-BG confirmation gate.
                if (FEBuilderGBA.ImageBGCore.IsReserveBgId(CoreState.ROM, (uint)_vm.CurrentIndex))
                {
                    bool proceed = CoreState.Services?.ShowYesNo(
                        "Warning: This BG slot is reserved by the system. Overwriting it may cause unexpected behavior. Continue?") ?? false;
                    if (!proceed) return;
                }

                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 256, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import BG Image (Drop)");
                // ImageBG uses RAW header-packed TSA + RAW palette
                // (Import3PointerHeaderTSA), not LZ77.
                var importResult = FEBuilderGBA.ImageImportCore.Import3PointerHeaderTSA(
                    rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height,
                    addr + 0, addr + 4, addr + 8);

                if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                _vm.RecordSourceFile(filePath);
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.BGThumbnailLoader(items, i));
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
                BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";
            }
            catch (Exception ex)
            {
                Log.Error("ImageBGView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBGView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            SelectedAddressBox.Text = $"0x{_vm.SelectAddress:X08}";
            ImagePointerBox.Text = $"0x{_vm.P0:X08}";
            TSAPointerBox.Text = $"0x{_vm.P4:X08}";
            PalettePointerBox.Text = $"0x{_vm.P8:X08}";
            CommentBox.Text = _vm.Comment;
            BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";

            // X_REF list (scaffold — empty for now; see VM comment).
            XRefList.ItemsSource = _vm.XRefEntries
                .Select(x => x.name).ToList();

            // Source-file affordance visibility.
            SourceFilePanel.IsVisible = _vm.IsSourceFileAvailable;

            // Warning banner.
            WarningMessageText.Text = _vm.WarningMessage;
            WarningPanel.IsVisible = !string.IsNullOrEmpty(_vm.WarningMessage);
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit BG");
            try
            {
                _vm.P0 = ParseHexText(ImagePointerBox.Text);
                _vm.P4 = ParseHexText(TSAPointerBox.Text);
                _vm.P8 = ParseHexText(PalettePointerBox.Text);
                _vm.SaveComment(CommentBox.Text ?? string.Empty);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                LoadImage();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // Increment by 1 each click — matches the existing
            // `Expand List (+1 slot)` semantics. Capped at 255 by Core.
            uint newCount = (uint)Math.Min(255, _vm.ReadCount + 1);
            if (newCount <= _vm.ReadCount) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            _undoService.Begin("Expand BG List");
            try
            {
                uint result = _vm.ExpandList(newCount);
                if (result == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Failed to expand BG list. Check ROM free space.");
                    return;
                }
                _undoService.Commit();
                LoadList();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo($"BG list expanded to {newCount} entries.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImageBGView.ListExpands_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Expand failed: {ex.Message}");
            }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Reserve-BG confirmation gate.
                if (FEBuilderGBA.ImageBGCore.IsReserveBgId(CoreState.ROM, (uint)_vm.CurrentIndex))
                {
                    bool proceed = CoreState.Services?.ShowYesNo(
                        "Warning: This BG slot is reserved by the system. Overwriting it may cause unexpected behavior. Continue?") ?? false;
                    if (!proceed) return;
                }

                // BG256 popup: pick 16-color / BG255 / BG224 mode.
                // For BG255 / BG224 we explicitly refuse — see plan v3.
                // The existing `ImageBGSelectPopupView` is a generic
                // picker stub today; treat any non-trivial selection
                // as "use the WinForms editor for now."
                if (_vm.IsBG256Patched)
                {
                    var popup = new ImageBGSelectPopupView();
                    object? result = await popup.ShowDialog<object?>(this);
                    if (result == null) return; // user cancelled
                    // Treat selected items containing "224" or "255" as
                    // explicit BG255/BG224 picks — refuse cleanly.
                    string? sel = result?.ToString();
                    if (!string.IsNullOrEmpty(sel) &&
                        (sel.Contains("224") || sel.Contains("255")))
                    {
                        CoreState.Services.ShowError(
                            "BG255/BG224 import is not yet supported in the Avalonia editor. Use the WinForms editor for 255/224-color cutscene backgrounds.");
                        return;
                    }
                    // Else: fall through to 16-color VanillaTSA path.
                }

                var loadResult = await ImageImportService.LoadAndQuantize(this, 256, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import BG Image");
                // ImageBG uses RAW header-packed TSA + RAW palette
                // (Import3PointerHeaderTSA), not LZ77.
                var importResult = FEBuilderGBA.ImageImportCore.Import3PointerHeaderTSA(
                    rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height,
                    addr + 0, addr + 4, addr + 8);

                if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                if (!string.IsNullOrEmpty(loadResult.SourcePath))
                {
                    _vm.RecordSourceFile(loadResult.SourcePath);
                }
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, $"background_{_vm.CurrentIndex:X02}.png");
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                // ImageBG palette is at P8 (addr+8), RAW (not LZ77).
                uint palPtr = _vm.P8;
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                if (!U.isSafetyOffset(palAddr)) { CoreState.Services.ShowError("Invalid palette address"); return; }
                // Read raw palette bytes (not LZ77-decompressed). 16 colors x 2 bytes = 32 bytes.
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 16);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string path = await FileDialogHelper.SavePaletteFile(this, "bg_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(pal, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                string path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import BG Palette");
                // ImageBG palette is at P8 (addr+8), written RAW (not LZ77).
                uint palAddr = FEBuilderGBA.ImageImportCore.WritePaletteToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write palette"); return; }
                _undoService.Commit();
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        void GraphicsTool_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `GraphicsToolButton_Click`:
            //   BG256 + P4==0: imageType=3, tsaType=0, paletteCount=16  (255-color)
            //   BG256 + P4==1: imageType=4, tsaType=0, paletteCount=16  (224-color)
            //   else:          imageType=0, tsaType=3, paletteCount=8   (normal BG)
            int imageType, tsaType, paletteCount;
            if (_vm.IsBG256Patched && _vm.P4 == 0)
            {
                imageType = 3; tsaType = 0; paletteCount = 16;
            }
            else if (_vm.IsBG256Patched && _vm.P4 == 1)
            {
                imageType = 4; tsaType = 0; paletteCount = 16;
            }
            else
            {
                imageType = 0; tsaType = 3; paletteCount = 8;
            }

            var view = WindowManager.Instance.Open<GraphicsToolView>();
            view.Jump(
                width: 32 * 8,
                height: 20 * 8,
                image: _vm.P0,
                imageType: imageType,
                tsa: _vm.P4,
                tsaType: tsaType,
                palette: _vm.P8,
                paletteType: 0,
                paletteCount: paletteCount,
                image2: 0);
        }

        void DecreaseColor_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `DecreaseColorTSAToolButton_Click`:
            //   f.InitMethod(1) — method 1 = normal BG mode.
            var view = WindowManager.Instance.Open<DecreaseColorTSAToolView>();
            view.InitMethod(1);
        }

        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath) || !File.Exists(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                var psi = new ProcessStartInfo(_vm.SourceFilePath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Error("ImageBGView.OpenSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source file: {ex.Message}");
            }
        }

        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath) || !File.Exists(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("explorer.exe",
                        $"/select,\"{_vm.SourceFilePath}\"")
                        { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    string? folder = Path.GetDirectoryName(_vm.SourceFilePath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var psi = new ProcessStartInfo(folder) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageBGView.SelectSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source folder: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }

    /// <summary>
    /// Selection result from the ImageBG mode-picker popup. Mirrors
    /// <see cref="ImageBGSelectPopupForm.SelectedType"/> in WinForms.
    /// </summary>
    public enum ImageBGSelectMode
    {
        None,
        VanillaTSA,
        BG224,
        BG255,
    }
}
