// SPDX-License-Identifier: GPL-3.0-or-later
// ImageGenericEnemyPortraitView code-behind — Image Import/Export parity (#907).
//
// Adds a read-only preview render of the selected 32x32 4bpp portrait plus
// Export PNG / Import Image buttons matching WF ImageGenericEnemyPortraitForm
// (ExportButton_Click / ImportButton_Click).
//
//   * Export: render the selected entry -> GbaImageControl.ExportPng (the #904
//     export seam). Read-only; never mutates the ROM.
//   * Import (async): file dialog -> ImageImportService.LoadAndRemapFromFile
//     against the entry's EXISTING palette (raw, like PortraitViewer; #906
//     un-premultiply handled in the loader) -> 32x32 indexed pixels + raw
//     palette -> GenericEnemyPortraitImportCore.ImportPortrait under one
//     UndoService scope, repointing BOTH the image (+0) and palette (+0x20)
//     slots. Rollback + ShowError on failure; Commit + re-render on success.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageGenericEnemyPortraitView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageGenericEnemyPortraitViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Generic Enemy Portraits";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageGenericEnemyPortraitView()
        {
            InitializeComponent();
            // The Export/Import buttons bind IsEnabled to IsLoaded on the VM.
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageGenericEnemyPortraitView.LoadList failed: {0}", ex.Message);
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
                RefreshPreview();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageGenericEnemyPortraitView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImagePtrLabel.Text = $"0x{_vm.ImagePointer:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        /// <summary>
        /// Render the selected portrait into the read-only preview control.
        /// On any failure the preview is cleared (blank). Never throws.
        /// </summary>
        void RefreshPreview()
        {
            try
            {
                IImage img = _vm.RenderPreview();
                PreviewImage.SetImage(img);
            }
            catch (Exception ex)
            {
                PreviewImage.SetImage(null);
                Log.ErrorF("ImageGenericEnemyPortraitView.RefreshPreview failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Export — read-only PNG of the selected portrait (#904 seam).
        // -----------------------------------------------------------------

        async void ExportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            try
            {
                // Make sure the preview holds the current entry's image, then
                // export it via the shared GbaImageControl save-file dialog.
                RefreshPreview();
                await PreviewImage.ExportPng(this, "generic_enemy_portrait");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageGenericEnemyPortraitView.ExportButton failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Import — write BOTH image (+0) and palette (+0x20) slots.
        // -----------------------------------------------------------------

        async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            string? filePath = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.OpenImageFile(this);
            if (string.IsNullOrEmpty(filePath)) return;
            RunPortraitImport(filePath);
        }

        // #1380 Part B — FE-Repo button: same as Import, sourced from the
        // FE-Repo "Item Icons/Special - Generic Minimugs" folder. Routes through
        // the SAME RunPortraitImport path; a non-32x32 asset fails gracefully
        // with the existing strict-size error.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            string? path = await FERepoPickHelper.PickForEditor(this,
                FERepoResourceBrowser.FERepoEditorKind.GenericEnemyPortrait);
            if (string.IsNullOrEmpty(path)) return;
            RunPortraitImport(path);
        }

        void RunPortraitImport(string filePath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return;

            uint entryAddr = _vm.CurrentAddr;
            if (entryAddr == 0) return;

            // Remap the imported PNG against the entry's EXISTING palette so the
            // colors stay close (import preserves the 16-color palette; #906
            // un-premultiply is handled inside the loader). Raw palette, like
            // PortraitViewer.
            byte[]? existingPalette = _vm.ReadActivePaletteBytes();
            if (existingPalette == null)
            {
                CoreState.Services.ShowError(
                    "Generic Enemy Portrait Import: could not read the active palette.");
                return;
            }

            var loadResult = ImageImportService.LoadAndRemapFromFile(
                filePath, 32, 32, existingPalette, 16, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? "Unknown error";
                CoreState.Services.ShowError($"Generic Enemy Portrait Import failed: {err}");
                return;
            }

            _undoService.Begin("Generic Enemy Portrait Import");
            string writeError;
            try
            {
                writeError = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom,
                    loadResult.IndexedPixels,
                    loadResult.GBAPalette,
                    entryAddr + 0,
                    entryAddr + ImageGenericEnemyPortraitViewModel.PALETTE_SLOT_OFFSET);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                _vm.LoadEntry(entryAddr);
                UpdateUI();
                RefreshPreview();
                CoreState.Services.ShowError($"Generic Enemy Portrait Import failed: {ex.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(writeError))
            {
                _undoService.Rollback();
                _vm.LoadEntry(entryAddr);
                UpdateUI();
                RefreshPreview();
                CoreState.Services.ShowError($"Generic Enemy Portrait Import failed: {writeError}");
                return;
            }

            _undoService.Commit();
            _vm.MarkClean();
            // Pointers changed -> reload + re-render.
            _vm.LoadEntry(entryAddr);
            UpdateUI();
            RefreshPreview();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
