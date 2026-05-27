// SPDX-License-Identifier: GPL-3.0-or-later
// Portrait Import Wizard — first meaningful slice (#657) + FE-Repo browser
// and drag-and-drop integration (#664).
//
// Pipeline: file dialog / drag-drop / FE-Repo -> LoadImageFromPath ->
// ImageImportService.LoadAndQuantizeFromFile (16-color quantization) ->
// preview via PortraitImportHelper.BuildPreviewImage -> on Import, call
// PortraitImportHelper.ImportSimple / ImportSheet under an UndoService
// scope. All ROM-write logic lives in PortraitImportHelper so both this
// wizard and ImagePortraitView share a single source of truth.
//
// Out of scope for #664 (tracked under follow-ups):
//   - Batch import (#661)
//   - Advanced palette options + Fuchidori (#662)
//   - Eye/mouth block configuration + per-frame preview (#663)
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitImporterView : TranslatedWindow, IEditorView
    {
        readonly ImagePortraitImporterViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Portrait Import Wizard";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitImporterView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // #664: enable drag-and-drop for image files. Mirrors the
            // pattern from ImagePortraitView (ctor lines 43-45).
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = _vm.CurrentAddr == 0
                ? "(no slot selected)"
                : string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        // #664: drag-over handler — accept the drag if any file has a
        // .png or .bmp extension. Mirrors ImagePortraitView.OnDragOver.
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

        // #664: drop handler — load the first .png/.bmp file via the shared
        // LoadImageFromPath helper so drag-drop, Pick PNG/BMP, and FE-Repo
        // all share the same load/preview path. The wizard does NOT
        // auto-import on drop; the user must still hit "Import to selected
        // slot" to commit the write (matches the Pick PNG/BMP flow).
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
                    LoadImageFromPath(path);
                    return;
                }
            }
        }

        async void PickFile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string filePath = await FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(filePath))
                {
                    return; // user cancelled
                }

                LoadImageFromPath(filePath);
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.PickFile_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Pick file failed: {ex.Message}");
            }
        }

        // #664: FE-Repo browser button — open the FE-Repo resource browser
        // window, await the user's selection, and load the returned file
        // path through the shared LoadImageFromPath helper. Mirrors
        // ImagePortraitView.FERepoButton_Click (line 676). The browser's
        // ShowDialog<string>(owner) returns the selected file path or
        // null/empty when cancelled.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var browser = new FERepoResourceBrowserWindow();
                string result = await browser.ShowDialog<string>(this);
                if (!string.IsNullOrEmpty(result))
                {
                    LoadImageFromPath(result);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.FERepo_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"FE-Repo browser failed: {ex.Message}");
            }
        }

        // Shared image-load + preview pipeline. Pick PNG/BMP, FE-Repo, and
        // drag-and-drop all funnel through here so the preview behavior is
        // identical across entry points (Copilot CLI plan #664 review #2).
        void LoadImageFromPath(string filePath)
        {
            try
            {
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 0, 0, 16);
                if (loadResult == null)
                {
                    CoreState.Services.ShowError("Failed to load image (no result).");
                    return;
                }
                if (!loadResult.Success)
                {
                    CoreState.Services.ShowError(loadResult.Error ?? "Failed to load image.");
                    return;
                }

                _vm.LoadedImage = loadResult;
                SourceFileLabel.Text = filePath;
                ImageSizeLabel.Text = $"Quantized to 16 colors — {loadResult.Width} x {loadResult.Height}";
                bool isSheet = loadResult.Width == 128 && loadResult.Height == 112;
                SheetModeLabel.Text = isSheet
                    ? "128 x 112 composite sheet — will write face, mini, mouth, palette (FE7/FE8 only)"
                    : "Simple image — will write sheet (D0) + palette (D8) only";

                // Preview — `BuildPreviewImage` returns an `IImage` (IDisposable);
                // `SetImage` extracts pixel data immediately via
                // `IconBitmapBuilder.FromImage`, so we dispose the source
                // right after to avoid leaking native bitmap resources when
                // the user picks multiple files (Copilot bot PR #684 inline
                // review).
                using (IImage preview = PortraitImportHelper.BuildPreviewImage(loadResult))
                {
                    PreviewImage.SetImage(preview);
                }

                StatusLabel.Text = string.Empty;
                StatusLabel.Foreground = global::Avalonia.Media.Brushes.Gray;
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.LoadImageFromPath failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Load image failed: {ex.Message}");
            }
        }

        void Import_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) { CoreState.Services.ShowError("No ROM loaded."); return; }

                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("Select a portrait slot from the list first."); return; }

                var loadResult = _vm.LoadedImage;
                if (loadResult == null || !loadResult.Success)
                { CoreState.Services.ShowError("Pick a source image first."); return; }

                // Single source of truth: PortraitImportHelper. Same path used
                // by ImagePortraitView (drag-drop + Import PNG button).
                ImportOutcome outcome;
                if (loadResult.Width == 128 && loadResult.Height == 112)
                {
                    outcome = PortraitImportHelper.ImportSheet(rom, addr, loadResult, _undoService,
                        "Import Portrait Sheet (Wizard)");
                }
                else
                {
                    outcome = PortraitImportHelper.ImportSimple(rom, addr, loadResult, _undoService,
                        "Import Portrait Image (Wizard)");
                }

                if (!outcome.Success)
                {
                    StatusLabel.Text = outcome.Error;
                    StatusLabel.Foreground = global::Avalonia.Media.Brushes.Firebrick;
                    CoreState.Services.ShowError(outcome.Error);
                    return;
                }

                // Record source file so the ImagePortraitView Open/Select
                // Source buttons light up for this slot.
                int idx = EntryList.SelectedOriginalIndex;
                PortraitImportHelper.RecordSourceFile(idx, loadResult.SourcePath);

                _vm.MarkClean();
                StatusLabel.Text = $"Imported into 0x{addr:X08}";
                StatusLabel.Foreground = global::Avalonia.Media.Brushes.DarkGreen;
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.Import_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Import failed: {ex.Message}");
            }
        }

        void RefreshImportButtonState()
        {
            bool hasImage = _vm.LoadedImage != null && _vm.LoadedImage.Success;
            bool hasSlot = _vm.CurrentAddr != 0;
            ImportButton.IsEnabled = hasImage && hasSlot;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
