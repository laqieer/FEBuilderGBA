// SPDX-License-Identifier: GPL-3.0-or-later
// World Map Image (FE7) editor view (#1184) — port of WF WorldMapImageFE7Form.
// Two GbaImageControl previews (12-split big field map + event image), each with
// PNG import (file dialog -> remap -> Core import under one UndoService scope) and
// PNG export (GbaImageControl.ExportPng), plus a Write-Pointers button and three
// read-only pointer labels. Mirrors the base WorldMapImageView's preview/import/
// export/undo idioms. All Log.Error calls pass a single interpolated string (Core
// Log.Error is params string[] — no {0}/{1} composite substitution).
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapImageFE7View : TranslatedWindow, IEditorView
    {
        readonly WorldMapImageFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Image (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapImageFE7View()
        {
            InitializeComponent();
            Opened += (_, _) => Load();
        }

        void Load()
        {
            try
            {
                _vm.LoadEntry(0);
                UpdateUI();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.Load failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            ImagePtrLabel.Text = string.Format("0x{0:X08}", _vm.BigImagePointer);
            PalettePtrLabel.Text = string.Format("0x{0:X08}", _vm.BigPalettePointer);
            TsaPtrLabel.Text = string.Format("0x{0:X08}", _vm.BigTsaPointer);
            BigImportButton.IsEnabled = _vm.CanImport;
            EventImportButton.IsEnabled = _vm.CanImport;
        }

        // ===================================================================
        // Previews (READ-ONLY) — render into the GbaImageControls. Null-safe;
        // a bad/missing/corrupt pointer (or a non-FE7 ROM) clears the surface.
        // ===================================================================

        void RefreshPreviews()
        {
            RenderInto(BigPreviewImage, _vm.TryRenderBigFieldMap, "BigFieldMap");
            RenderInto(EventPreviewImage, _vm.TryRenderEvent, "Event");
        }

        void RenderInto(GbaImageControl target, Func<IImage?> render, string label)
        {
            try
            {
                IImage? img = render();
                target.SetImage(img);
            }
            catch (Exception ex)
            {
                target.SetImage(null);
                Log.Error("WorldMapImageFE7View.Render" + label + " failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Big field map import (1024x688) / export.
        // ===================================================================

        async void BigImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoBigImport(path);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.BigImport_Click failed: " + ex.ToString());
            }
        }

        /// <summary>Injectable big-field-map import driver (testable without UI).
        /// Loads the PNG, validates 1024x688, then imports RGBA under one undo
        /// scope (the Core import derives the 4-bank palette + per-tile banks).</summary>
        public async System.Threading.Tasks.Task DoBigImport(string imagePath)
        {
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                if (img.Width != WorldMapImageFE7ViewModel.BigWidth
                    || img.Height != WorldMapImageFE7ViewModel.BigHeight)
                {
                    CoreState.Services?.ShowError(R.Error(
                        "The world map big field map must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                        WorldMapImageFE7ViewModel.BigWidth, WorldMapImageFE7ViewModel.BigHeight,
                        img.Width, img.Height));
                    return;
                }
                byte[] rgba = img.GetPixelData();
                if (rgba == null
                    || rgba.Length < WorldMapImageFE7ViewModel.BigWidth * WorldMapImageFE7ViewModel.BigHeight * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                _undoService.Begin("Import World Map Big Field Map (FE7)");
                try
                {
                    string err = _vm.ImportBigFieldMap(rgba, img.Width, img.Height);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("WorldMapImageFE7View.DoBigImport write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                img.Dispose();
            }
            UpdateUI();
            RefreshPreviews();
        }

        async void BigExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await BigPreviewImage.ExportPng(this, "worldmap_fe7_bigfieldmap");
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.BigExport_Click failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Event image import (240x160) / export.
        // ===================================================================

        async void EventImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoEventImport(path);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.EventImport_Click failed: " + ex.ToString());
            }
        }

        /// <summary>Injectable event-image import driver (testable without UI).
        /// Loads the PNG, validates 240x160, then imports RGBA under one undo
        /// scope (the Core import reduces to the 256x160 4-bank canvas).</summary>
        public async System.Threading.Tasks.Task DoEventImport(string imagePath)
        {
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                if (img.Width != WorldMapImageFE7ViewModel.EventWidth
                    || img.Height != WorldMapImageFE7ViewModel.EventHeight)
                {
                    CoreState.Services?.ShowError(R.Error(
                        "The world map event image must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                        WorldMapImageFE7ViewModel.EventWidth, WorldMapImageFE7ViewModel.EventHeight,
                        img.Width, img.Height));
                    return;
                }
                byte[] rgba = img.GetPixelData();
                if (rgba == null
                    || rgba.Length < WorldMapImageFE7ViewModel.EventWidth * WorldMapImageFE7ViewModel.EventHeight * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                _undoService.Begin("Import World Map Event Image (FE7)");
                try
                {
                    string err = _vm.ImportEvent(rgba, img.Width, img.Height);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("WorldMapImageFE7View.DoEventImport write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                img.Dispose();
            }
            UpdateUI();
            RefreshPreviews();
        }

        async void EventExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await EventPreviewImage.ExportPng(this, "worldmap_fe7_event");
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.EventExport_Click failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Write pointers (WF WriteButton_Click + WMEvent.WritePointer) / Undo.
        // ===================================================================

        void WriteAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _undoService.Begin("Write World Map Pointers (FE7)");
                try
                {
                    _vm.WritePointers();
                    _undoService.Commit();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("WorldMapImageFE7View.WriteAll write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Write failed: {0}", ex.Message));
                    return;
                }
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.WriteAll_Click failed: " + ex.ToString());
            }
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                _vm.LoadEntry(0);
                UpdateUI();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE7View.Undo_Click failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() => Load();
    }
}
