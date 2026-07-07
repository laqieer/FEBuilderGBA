using global::Avalonia;
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
    public partial class ImageBattleBGView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageBattleBGViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Battle Background Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Battle Background Editor", 1024, 560, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ImageBattleBGView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Wire Comment lost-focus → save (mirrors WF
            // InputFormRef.OnComment_TextChanged save path).
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

            // Enable drag-and-drop for image files
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
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

        // #1393: the FE-Repo button routes through this SAME FromFile import
        // path. strictSize=true (used by FE-Repo) rejects a non-240x160 asset
        // with the existing size error rather than silently cropping/quantizing
        // it; drag-and-drop keeps the lenient default (strictSize=false).
        void ImportImageFromFile(string filePath, bool strictSize = false)
        {
            try
            {
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 240, 160, 16, strictSize: strictSize);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Battle BG Image (Drop)");
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8);

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
                EntryList.SetItems(items);
                // Reflect read-config bar values now that the list is loaded.
                // #649: ReadStart/ReadCount now display via the unified
                // EditorTopBar's read-only TextBlock slots.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();
                BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleBGView.LoadList failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleBGView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            SelectedAddressBox.Text = $"0x{_vm.SelectAddress:X08}";
            ImagePointerBox.Text = $"0x{_vm.ImagePointer:X08}";
            TSAPointerBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalettePointerBox.Text = $"0x{_vm.PalettePointer:X08}";
            CommentBox.Text = _vm.Comment;
            BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";

            // Refresh X_REF list display (mirror WF X_REF ListBoxEx).
            XRefList.ItemsSource = _vm.XRefEntries
                .Select(x => x.name).ToList();

            // Refresh source-file affordance visibility.
            SourceFilePanel.IsVisible = _vm.IsSourceFileAvailable;

            // Refresh BG preview image.
            try
            {
                var img = _vm.TryLoadImage();
                BgPreviewImage.SetImage(img);
            }
            catch { BgPreviewImage.SetImage(null); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Battle BG");
            try
            {
                _vm.ImagePointer = ParseHexText(ImagePointerBox.Text);
                _vm.TSAPointer = ParseHexText(TSAPointerBox.Text);
                _vm.PalettePointer = ParseHexText(PalettePointerBox.Text);
                _vm.SaveComment(CommentBox.Text ?? string.Empty);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var loadResult = await ImageImportService.LoadAndQuantize(TopLevel.GetTopLevel(this) as Window, 240, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Battle BG Image");
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8);

                if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                // Record the source file path so the "Open Source File/Folder"
                // affordance works whether the user imported via the button
                // OR via drag-and-drop (Copilot bot review on PR #513 —
                // mirrors WinForms `ImageBattleBGForm.ImportButton_Click`).
                if (!string.IsNullOrEmpty(loadResult.SourcePath))
                {
                    _vm.RecordSourceFile(loadResult.SourcePath);
                }
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        // #1393 — FE-Repo button: pick a 240x160 battle background from the
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
            try
            {
                if (CoreState.ROM == null) return;
                var img = _vm.TryLoadImage();
                if (img == null) { CoreState.Services.ShowError("No image to export"); return; }
                // #1639: write via the SAF bridge so Android content:// targets work.
                await FileDialogHelper.SaveImageFileVia(TopLevel.GetTopLevel(this), $"battle_bg_{_vm.CurrentIndex:X02}.png", p => img.Save(p));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export image failed: {ex.Message}"); }
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
                uint palPtr = _vm.PalettePointer;
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                // BattleBG palette is LZ77-compressed
                byte[] pal = LZ77.decompress(rom.Data, palAddr);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                await FileDialogHelper.SavePaletteFileVia(TopLevel.GetTopLevel(this), "battle_bg_palette.pal", p =>
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
                string path = await FileDialogHelper.OpenPaletteFile(TopLevel.GetTopLevel(this));
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
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        // -----------------------------------------------------------------
        // New gap-fix handlers (#434)
        // -----------------------------------------------------------------

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // Increment by 1 each click — matches the existing
            // `Expand List (+1 slot)` semantics used by the
            // ItemEffectivenessViewerView. Capped at 255 by the Core helper.
            uint newCount = (uint)Math.Min(255, _vm.ReadCount + 1);
            if (newCount <= _vm.ReadCount) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Single source of truth for undo tracking: open the ambient
            // ROM undo scope and call the parameterless VM/Core overload.
            // Every ROM write inside the scope appends to exactly one
            // UndoData list — no double-snapshot (Copilot CLI re-review
            // on PR #513).
            _undoService.Begin("Expand Battle BG List");
            try
            {
                uint result = _vm.ExpandList(newCount);
                if (result == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Failed to expand Battle BG list. Check ROM free space.");
                    return;
                }
                _undoService.Commit();
                LoadList();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo($"Battle BG list expanded to {newCount} entries.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageBattleBGView.ListExpands_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Expand failed: {ex.Message}");
            }
        }

        void GraphicsTool_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `GraphicsToolButton_Click`:
            //   f.Jump(30*8, 20*8, image, 0, tsa, 1, palette, 1, 8, 0)
            // Battle BG is 30x20 tiles = 240x160 px. All three streams
            // (image, tsa, palette) are LZ77-compressed in the ROM —
            // imageType=0 in WF maps to "LZ77 compressed" (see
            // GraphicsToolForm.Draw line 309); the tsaType/paletteType=1
            // is the WF compressed-combo index for those streams.
            var view = WindowManager.Instance.Open<GraphicsToolView>();
            view.Jump(
                width: 30 * 8,
                height: 20 * 8,
                image: _vm.ImagePointer,
                imageType: 0,
                tsa: _vm.TSAPointer,
                tsaType: 1,
                palette: _vm.PalettePointer,
                paletteType: 1,
                paletteCount: 8,
                image2: 0);
        }

        void DecreaseColor_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `DecreaseColorTSAToolButton_Click`:
            //   f.InitMethod(2) — method 2 = Battle BG mode.
            var view = WindowManager.Instance.Open<DecreaseColorTSAToolView>();
            view.InitMethod(2);
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
                Log.ErrorF("ImageBattleBGView.OpenSource_Click: {0}", ex.Message);
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
                // Open Explorer with the file selected (Windows-only "/select,"
                // verb; on other platforms fall back to opening the parent
                // directory).
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
                Log.ErrorF("ImageBattleBGView.SelectSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source folder: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
