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
    public partial class ImageBGView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageBGViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Background Image Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Background Image Editor", 1024, 560, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ImageBGView()
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

        /// <summary>
        /// Apply all pre-import safety gates that the import paths share:
        ///   1. **Entry-loaded guard** (Copilot bot review on PR #517):
        ///      refuse if no entry is selected / loaded yet. Writing to
        ///      `CurrentAddr == 0` would corrupt the ROM header.
        ///   2. Reserve-BG confirmation (system black / random slots).
        ///
        /// BG255/BG224 entries are no longer refused here (#799): when the
        /// BG256Color patch is installed AND the entry's P4 is &lt;= 1 (the
        /// 255/224 mode flag), the import is routed to the 8bpp 255/224 path
        /// (<see cref="IsBG256ColorEntry"/> + <see cref="RunBG256Import"/>)
        /// instead of the 16-color header-TSA path. Both import handlers
        /// branch on <see cref="IsBG256ColorEntry"/> after this gate passes.
        /// </summary>
        /// <returns>true if import should proceed; false if cancelled
        ///   or refused.</returns>
        bool PreImportGate()
        {
            // Capture rom up front and bail cleanly when null — avoids NRE
            // in `U.isSafetyOffset` / `rom.Data.Length` (Copilot bot
            // review on PR #517 — "PreImportGate can throw NullReferenceException
            // if CoreState.ROM is null").
            var rom = CoreState.ROM;
            if (rom == null)
            {
                CoreState.Services?.ShowError("No ROM is loaded.");
                return false;
            }

            // Gate 0: entry-loaded guard.
            if (!_vm.IsLoaded || !U.isSafetyOffset(_vm.CurrentAddr, rom) || _vm.CurrentAddr + 12 > (uint)rom.Data.Length)
            {
                CoreState.Services?.ShowError("No BG entry is selected. Select an entry in the list before importing.");
                return false;
            }

            // Gate 1: reserve-BG confirmation.
            if (FEBuilderGBA.ImageBGCore.IsReserveBgId(rom, (uint)_vm.CurrentIndex))
            {
                bool proceed = CoreState.Services?.ShowYesNo(
                    "Warning: This BG slot is reserved by the system. Overwriting it may cause unexpected behavior. Continue?") ?? false;
                if (!proceed) return false;
            }

            return true;
        }

        /// <summary>
        /// True when the current entry is a 255/224-color cutscene background:
        /// the BG256Color patch is installed AND P4 is the raw mode flag
        /// (0 = 255-color, 1 = 224-color), not a TSA pointer. Such entries use
        /// the 8bpp <see cref="RunBG256Import"/> path instead of the 16-color
        /// header-TSA path (#799). The 255-vs-224 mode is preserved from the
        /// entry's current P4 (no popup — mirrors WF defaulting to the
        /// existing mode).
        /// </summary>
        bool IsBG256ColorEntry() => _vm.IsBG256Patched && _vm.P4 <= 1;

        /// <summary>
        /// Import an 8bpp 255/224-color background (already quantized) into the
        /// current entry, under the shared <see cref="UndoService"/> scope.
        /// Mirrors WF <c>ImageBGForm.ImportButton255</c>: P0 = LZ77 8bpp tiles,
        /// P4 = raw mode flag, P8 = raw 512-byte palette. The 255-vs-224 mode
        /// is taken from the entry's current P4 (preserve).
        /// </summary>
        void RunBG256Import(ImageImportService.LoadResult loadResult, string sourcePath, string undoLabel)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            bool is224 = _vm.P4 == 1;
            uint addr = _vm.CurrentAddr;
            _undoService.Begin(undoLabel);
            try
            {
                // Begin() already opened ROM.BeginUndoScope(undo); pass the
                // same UndoData so Import255ColorBG reuses the ambient scope
                // (no double-snapshot) and all P0/P4/P8 writes are captured.
                var undo = _undoService.GetActiveUndoData();
                var importResult = FEBuilderGBA.ImageBG256ColorCore.Import255ColorBG(
                    rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height,
                    addr + 0, addr + 4, addr + 8, is224,
                    CoreState.ImageService, undo);

                if (!importResult.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(importResult.Error);
                    return;
                }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                if (!string.IsNullOrEmpty(sourcePath)) _vm.RecordSourceFile(sourcePath);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Import failed: {ex.Message}");
            }
        }

        void ImportImageFromFile(string filePath)
        {
            try
            {
                if (!PreImportGate()) return;

                // BG255/BG224 entries take the 8bpp 255/224 path (#799).
                if (IsBG256ColorEntry())
                {
                    int maxColors = _vm.P4 == 1 ? 224 : 256;
                    var bg256Load = ImageImportService.LoadAndQuantizeFromFile(filePath, 256, 160, maxColors, strictSize: true);
                    if (bg256Load == null) return;
                    if (!bg256Load.Success) { CoreState.Services.ShowError(bg256Load.Error); return; }
                    RunBG256Import(bg256Load, filePath, "Import BG255 Image (Drop)");
                    return;
                }

                // strictSize=true mirrors WF's 256x160 expectation (see
                // ImportPng_Click comment for rationale).
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 256, 160, 16, strictSize: true);
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
                LoadImage(); // refresh preview after drop import (Copilot bot review on PR #517).
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        // #1380 Part B — FE-Repo button: same as Import, sourced from the
        // FE-Repo "CG Images" folder. Routes the chosen path through the SAME
        // ImportImageFromFile pipeline as the drag-drop import; a wrong-size
        // asset fails gracefully with the existing strict-size error.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await FERepoPickHelper.PickForEditor(TopLevel.GetTopLevel(this) as Window,
                FERepoResourceBrowser.FERepoEditorKind.BackgroundImage);
            if (string.IsNullOrEmpty(path)) return;
            ImportImageFromFile(path);
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.BGThumbnailLoader(items, i));
                // #649: ReadStart/ReadCount display via the unified EditorTopBar.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();
                BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBGView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("ImageBGView.OnSelected failed: {0}", ex.Message);
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

            // X_REF list — populated from the Core event-script BG
            // cross-reference finder via ImageBGViewModel.RefreshXrefs (#990).
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

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

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
                Log.ErrorF("ImageBGView.ListExpands_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Expand failed: {ex.Message}");
            }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // The reserve-BG confirmation + entry-loaded guards live in
                // `PreImportGate` so the drag-drop import path applies them
                // too. Copilot CLI v3 review (#517) flagged the drop-import
                // bypassing the gate — sharing it eliminates that.
                if (!PreImportGate()) return;

                // BG255/BG224 entries take the 8bpp 255/224 path (#799):
                // quantize to 256 (255-color) or 224 (224-color) colors and
                // write P0 = LZ77 8bpp tiles, P4 = raw mode flag, P8 = raw
                // 512-byte palette. The mode is preserved from the entry's P4.
                if (IsBG256ColorEntry())
                {
                    int maxColors = _vm.P4 == 1 ? 224 : 256;
                    var bg256Load = await ImageImportService.LoadAndQuantize(TopLevel.GetTopLevel(this) as Window, 256, 160, maxColors, strictSize: true);
                    if (bg256Load == null) return;
                    if (!bg256Load.Success) { CoreState.Services.ShowError(bg256Load.Error); return; }
                    RunBG256Import(bg256Load, bg256Load.SourcePath, "Import BG255 Image");
                    return;
                }

                // Single-sub-palette quantization. WF uses palette_count=8
                // for normal BG (8 × 16-color sub-palettes, addressed by
                // TSA upper-nibble bits 12-15). The Avalonia port today
                // quantizes to one 16-color sub-palette and writes 32
                // bytes — sufficient for ROMs that only use sub-palette
                // 0 (the common case for vanilla FE6/7/8 backgrounds),
                // but multi-palette BG slots will lose sub-palettes 1-7.
                // This limitation is called out in the PR Known
                // Limitations section (#429 / Copilot CLI v3 review).
                //
                // strictSize=true mirrors WF's 256x160 (32×20 tile)
                // expectation. Loading 240x160 would produce
                // incorrect header-TSA packing under the default
                // tsa_width_margin=2 (Copilot bot review on PR #517).
                var loadResult = await ImageImportService.LoadAndQuantize(TopLevel.GetTopLevel(this) as Window, 256, 160, 16, strictSize: true);
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
            await ImageDisplay.ExportPng(TopLevel.GetTopLevel(this) as Window, $"background_{_vm.CurrentIndex:X02}.png");
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

        /// <summary>
        /// Number of 16-color sub-palettes a normal BG entry occupies
        /// in ROM. Matches WF `ImageBGForm.GraphicsToolButton_Click`'s
        /// `paletteCount = 8` for normal-BG mode (and 16 for the
        /// BG256-patched cutscene modes). The palette byte length is
        /// <c>SubPalettes × 16 colors × 2 bytes = SubPalettes × 32</c>.
        /// </summary>
        const int NormalBgSubPalettes = 8;
        const int BG256SubPalettes = 16;

        /// <summary>
        /// Decide how many 16-color sub-palettes the current entry's P8
        /// palette pointer should reference, based on the mode (BG256
        /// vs vanilla) detected at LoadEntry time. Used by both
        /// ExportPal and ImportPal to keep the byte counts honest.
        /// </summary>
        int GetExpectedSubPaletteCount()
        {
            // BG256-patched + P4 <= 1 ⇒ cutscene mode uses 16 sub-palettes.
            if (_vm.IsBG256Patched && _vm.P4 <= 1) return BG256SubPalettes;
            return NormalBgSubPalettes;
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
                // Read multi-row palette bytes (raw — not LZ77). Mirrors
                // WF: normal BG = 8×16 = 128 colors (256 bytes);
                // BG256 cutscene = 16×16 = 256 colors (512 bytes).
                // Copilot bot review on PR #517 flagged the previous
                // 32-byte-only export as incomplete for multi-palette
                // entries.
                int subPalettes = GetExpectedSubPaletteCount();
                int colorCount = subPalettes * 16;
                // Validate we have enough ROM bytes for the requested count.
                uint maxAvailable = (uint)rom.Data.Length - palAddr;
                int safeColorCount = (int)Math.Min((uint)colorCount, maxAvailable / 2);
                if (safeColorCount < 16) { CoreState.Services.ShowError("Palette pointer is too close to end of ROM"); return; }
                byte[] pal = ImageUtilCore.GetPalette(palAddr, safeColorCount);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                await FileDialogHelper.SavePaletteFileVia(TopLevel.GetTopLevel(this) as Window, "bg_palette.pal", p =>
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
                // Entry-loaded guard (Copilot bot review on PR #517):
                // refuse writes when no entry is selected.
                if (!_vm.IsLoaded || !U.isSafetyOffset(_vm.CurrentAddr) || _vm.CurrentAddr + 12 > (uint)rom.Data.Length)
                {
                    CoreState.Services?.ShowError("No BG entry is selected. Select an entry in the list before importing.");
                    return;
                }
                string path = await FileDialogHelper.OpenPaletteFile(TopLevel.GetTopLevel(this) as Window);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                if (palData.Length % 32 != 0) { CoreState.Services.ShowError("Palette length must be a multiple of 32 bytes (16 colors × 2 bytes)"); return; }
                // Validate size against the expected mode. Mismatched
                // sizes get a clear error rather than a silent truncation
                // (Copilot bot review on PR #517).
                int subPalettes = GetExpectedSubPaletteCount();
                int expectedBytes = subPalettes * 32;
                if (palData.Length != expectedBytes)
                {
                    bool proceed = CoreState.Services?.ShowYesNo(
                        $"Palette size mismatch: this BG entry expects {expectedBytes} bytes ({subPalettes} sub-palettes); the file has {palData.Length} bytes. Continue with size mismatch?") ?? false;
                    if (!proceed) return;
                }
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
                Log.ErrorF("ImageBGView.OpenSource_Click: {0}", ex.Message);
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
                Log.ErrorF("ImageBGView.SelectSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source folder: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
