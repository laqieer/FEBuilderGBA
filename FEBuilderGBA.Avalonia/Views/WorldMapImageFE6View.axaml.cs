// SPDX-License-Identifier: GPL-3.0-or-later
// World Map Image (FE6) editor view (#1183) — port of WF WorldMapImageFE6Form.
// Five GbaImageControl previews (full + 4 quadrants NW/NE/SW/SE), each with a
// per-zoom PNG Import (file dialog -> validate 240x160 -> Core 256-linear import
// under one UndoService scope -> repoint image+palette slots) and a PNG Export
// (GbaImageControl.ExportPng), plus the five editable image + five editable
// palette pointer fields (bound TwoWay to the VM) and a Write button (the WF
// "AllWriteButton"). Mirrors the FE7 view's preview/import/export/undo idioms.
// All Log.Error calls pass a single interpolated string (Core Log.Error is
// params string[] — no {0}/{1} composite substitution).
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapImageFE6View : TranslatedUserControl, IEmbeddableEditor
    {
        readonly WorldMapImageFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "World Map Image (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("World Map Image (FE6)", 900, 720, SizeToContent: global::Avalonia.Controls.SizeToContent.Width);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public WorldMapImageFE6View()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                Load();
            }
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
                Log.Error("WorldMapImageFE6View.Load failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            FullImportButton.IsEnabled = _vm.CanImportFull;
            NWImportButton.IsEnabled = _vm.CanImportNW;
            NEImportButton.IsEnabled = _vm.CanImportNE;
            SWImportButton.IsEnabled = _vm.CanImportSW;
            SEImportButton.IsEnabled = _vm.CanImportSE;
        }

        // ===================================================================
        // Previews (READ-ONLY) — render into the GbaImageControls. Null-safe;
        // a bad/missing/corrupt pointer (or a non-FE6 ROM) clears the surface.
        // ===================================================================

        void RefreshPreviews()
        {
            RenderInto(FullPreviewImage, _vm.TryRenderZoomOut, "Full");
            RenderInto(NWPreviewImage, _vm.TryRenderZoomNW, "NW");
            RenderInto(NEPreviewImage, _vm.TryRenderZoomNE, "NE");
            RenderInto(SWPreviewImage, _vm.TryRenderZoomSW, "SW");
            RenderInto(SEPreviewImage, _vm.TryRenderZoomSE, "SE");
        }

        void RenderInto(GbaImageControl target, Func<IImage?> render, string label)
        {
            try
            {
                // GbaImageControl.SetImage COPIES the pixels into its own Avalonia
                // bitmap (it does NOT take ownership), so the Core-rendered IImage
                // must be disposed after SetImage returns or every RefreshPreviews
                // leaks the backing bitmap (Copilot PR #1223 re-review #1). IImage
                // is IDisposable.
                using IImage? img = render();
                target.SetImage(img);
            }
            catch (Exception ex)
            {
                target.SetImage(null);
                Log.Error("WorldMapImageFE6View.Render" + label + " failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Per-zoom import (240x160 256-linear LZ77 image + palette).
        // ===================================================================

        async void FullImport_Click(object? sender, RoutedEventArgs e) => await Import(WorldMapImageFE6ViewModel.SlotFull, FullPreviewImage, _vm.TryRenderZoomOut, "Full");
        async void NWImport_Click(object? sender, RoutedEventArgs e) => await Import(WorldMapImageFE6ViewModel.SlotNW, NWPreviewImage, _vm.TryRenderZoomNW, "NW");
        async void NEImport_Click(object? sender, RoutedEventArgs e) => await Import(WorldMapImageFE6ViewModel.SlotNE, NEPreviewImage, _vm.TryRenderZoomNE, "NE");
        async void SWImport_Click(object? sender, RoutedEventArgs e) => await Import(WorldMapImageFE6ViewModel.SlotSW, SWPreviewImage, _vm.TryRenderZoomSW, "SW");
        async void SEImport_Click(object? sender, RoutedEventArgs e) => await Import(WorldMapImageFE6ViewModel.SlotSE, SEPreviewImage, _vm.TryRenderZoomSE, "SE");

        async System.Threading.Tasks.Task Import(uint slotByteOffset, GbaImageControl target,
            Func<IImage?> render, string label)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
                if (path == null) return;
                await DoImport(slotByteOffset, path, target, render, label);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE6View." + label + "Import_Click failed: " + ex.ToString());
            }
        }

        /// <summary>Injectable per-zoom import driver (testable without UI). Loads
        /// the PNG, validates 240x160, then imports RGBA under one undo scope (the
        /// Core import builds the 256-color palette + repoints image+palette).</summary>
        public async System.Threading.Tasks.Task DoImport(uint slotByteOffset, string imagePath,
            GbaImageControl target, Func<IImage?> render, string label)
        {
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                if (img.Width != WorldMapImageFE6ViewModel.ZoomWidth
                    || img.Height != WorldMapImageFE6ViewModel.ZoomHeight)
                {
                    CoreState.Services?.ShowError(R.Error(
                        "The world map big field map must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                        WorldMapImageFE6ViewModel.ZoomWidth, WorldMapImageFE6ViewModel.ZoomHeight,
                        img.Width, img.Height));
                    return;
                }
                byte[] rgba = img.GetPixelData();
                if (rgba == null
                    || rgba.Length < WorldMapImageFE6ViewModel.ZoomWidth * WorldMapImageFE6ViewModel.ZoomHeight * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                _undoService.Begin("Import World Map Image (FE6)");
                try
                {
                    string err = _vm.ImportZoom(slotByteOffset, rgba, img.Width, img.Height);
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
                    Log.Error("WorldMapImageFE6View.DoImport" + label + " write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                img.Dispose();
            }
            // The import repoints the slot, so re-seed the editable pointer fields.
            _vm.LoadEntry(0);
            UpdateUI();
            RenderInto(target, render, label);
        }

        // ===================================================================
        // Per-zoom export (the 240x160 rendered preview -> PNG).
        // ===================================================================

        async void FullExport_Click(object? sender, RoutedEventArgs e) => await Export(FullPreviewImage, "worldmap_fe6_full");
        async void NWExport_Click(object? sender, RoutedEventArgs e) => await Export(NWPreviewImage, "worldmap_fe6_nw");
        async void NEExport_Click(object? sender, RoutedEventArgs e) => await Export(NEPreviewImage, "worldmap_fe6_ne");
        async void SWExport_Click(object? sender, RoutedEventArgs e) => await Export(SWPreviewImage, "worldmap_fe6_sw");
        async void SEExport_Click(object? sender, RoutedEventArgs e) => await Export(SEPreviewImage, "worldmap_fe6_se");

        async System.Threading.Tasks.Task Export(GbaImageControl source, string suggestedName)
        {
            try
            {
                await source.ExportPng(TopLevel.GetTopLevel(this) as Window, suggestedName);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE6View.Export failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Write the editable image + palette pointer values back (WF AllWriteButton).
        // ===================================================================

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _undoService.Begin("Write World Map Image Pointers (FE6)");
                try
                {
                    _vm.WritePointers(_undoService.GetActiveUndoData());
                    _undoService.Commit();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("WorldMapImageFE6View.Write_Click write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Write failed: {0}", ex.Message));
                    return;
                }
                _vm.LoadEntry(0);
                UpdateUI();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageFE6View.Write_Click failed: " + ex.ToString());
            }
        }

        // ===================================================================
        // Undo (reverts the most recent import / write).
        // ===================================================================

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
                Log.Error("WorldMapImageFE6View.Undo_Click failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() => Load();
    }
}
