using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorView : TranslatedWindow, IEditorView
    {
        readonly MapEditorViewModel _vm = new();
        readonly UndoService _undo = new();
        int _zoom = 1;
        byte[] _lastRgba; // cached for refresh

        public string ViewTitle => "Visual Map Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public MapEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            ZoomInBtn.Click += OnZoomIn;
            ZoomOutBtn.Click += OnZoomOut;
            MapImageControl.PointerPressed += OnMapImageClick;
            WriteTileBtn.Click += OnWriteTile;
            RefreshMapBtn.Click += OnRefreshMap;
            ExportCsvButton.Click += ExportCsv_Click;
            ImportCsvButton.Click += ImportCsv_Click;
            ExportTmxButton.Click += ExportTmx_Click;
            ImportTmxButton.Click += ImportTmx_Click;
            // Paint Mode defaults to OFF (no regression to existing select behaviour).
            PaintModeCheck.IsChecked = false;
            // Hit-test the outer Border (Background=Transparent) only — clicks on the
            // inner Image bubble up here via Avalonia's routed PointerPressed event.
            // Wiring both would double-fire the handler. The handler converts pointer
            // coords to image-pixel coords via e.GetPosition(TilePaletteImage).
            TilePaletteHitArea.PointerPressed += OnTilePaletteClick;
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                // Find the map ID from the address list
                uint mapId = 0;
                var items = _vm.LoadList();
                foreach (var item in items)
                {
                    if (item.addr == addr)
                    {
                        mapId = item.tag;
                        break;
                    }
                }

                byte[] rgba = _vm.LoadMapImage(addr, mapId);
                _lastRgba = rgba;
                UpdateUI(rgba);
                UpdateTileUI();
                UpdateTilePalette();
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.OnSelected failed: {0}", ex.Message);
                MapInfoLabel.Text = "Error: " + ex.Message;
            }
        }

        void UpdateUI(byte[] rgba)
        {
            MapInfoLabel.Text = _vm.MapInfo;
            ZoomLabel.Text = _zoom + "x";

            if (rgba != null && _vm.MapWidth > 0 && _vm.MapHeight > 0)
            {
                int pixelW = _vm.MapWidth * 16;
                int pixelH = _vm.MapHeight * 16;
                MapImageControl.Zoom = _zoom;
                MapImageControl.SetRgbaData(rgba, pixelW, pixelH);
            }
            else
            {
                MapImageControl.SetImage(null);
            }
        }

        /// <summary>Re-render the chipset palette for the currently-loaded map.</summary>
        void UpdateTilePalette()
        {
            try
            {
                byte[] paletteRgba = _vm.RenderChipsetPalette(out int pw, out int ph);
                if (paletteRgba == null || pw <= 0 || ph <= 0)
                {
                    TilePaletteImage.Source = null;
                    TilePaletteImage.Width = double.NaN;
                    TilePaletteImage.Height = double.NaN;
                    // Clear the chipset info too — otherwise a stale label from a
                    // previously-loaded map would remain visible after the palette
                    // is cleared.
                    ChipsetInfoLabel.Text = R._("No chipset selected");
                    return;
                }
                var bmp = IconBitmapBuilder.FromRgba(paletteRgba, pw, ph);
                if (bmp == null)
                {
                    TilePaletteImage.Source = null;
                    ChipsetInfoLabel.Text = R._("No chipset selected");
                    return;
                }
                TilePaletteImage.Source = bmp;
                TilePaletteImage.Width = pw;
                TilePaletteImage.Height = ph;
                if (_vm.HasChipsetSelected)
                    ChipsetInfoLabel.Text = _vm.ChipsetInfo;
                else
                    ChipsetInfoLabel.Text = R._("No chipset selected");
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.UpdateTilePalette failed: {0}", ex.Message);
            }
        }

        void OnZoomIn(object? sender, RoutedEventArgs e)
        {
            if (_zoom < 4) _zoom++;
            MapImageControl.Zoom = _zoom;
            ZoomLabel.Text = _zoom + "x";
        }

        void OnZoomOut(object? sender, RoutedEventArgs e)
        {
            if (_zoom > 1) _zoom--;
            MapImageControl.Zoom = _zoom;
            ZoomLabel.Text = _zoom + "x";
        }

        /// <summary>
        /// Convert a pointer position on the map image control into tile (x, y) coordinates.
        /// </summary>
        bool MapPointerToTile(PointerPressedEventArgs e, out int tileX, out int tileY)
        {
            tileX = 0;
            tileY = 0;
            if (_vm.MapWidth <= 0 || _vm.MapHeight <= 0) return false;
            // Use the inner ImageDisplay's source-pixel coords (#658). Calling
            // e.GetPosition(MapImageControl) on the outer UserControl gave coords
            // that didn't account for ScrollViewer offset / zoom, so the click
            // landed at the wrong tile.
            if (!MapImageControl.TryGetSourcePixel(e, out int srcX, out int srcY)) return false;
            tileX = srcX / 16;
            tileY = srcY / 16;
            if (tileX < 0 || tileY < 0 || tileX >= _vm.MapWidth || tileY >= _vm.MapHeight) return false;
            return true;
        }

        void OnMapImageClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (!MapPointerToTile(e, out int tileX, out int tileY)) return;

                bool paintMode = PaintModeCheck.IsChecked == true;
                if (paintMode)
                {
                    if (_vm.HasChipsetSelected)
                    {
                        // #1148: click-to-paint commits the raw .mar map layout to the
                        // build-preview ROM (PaintTileAt → ApplyMapEdit → LZ77 write +
                        // repoint). In decomp mode that's a source-tree asset — block it
                        // and surface the export-only notice (the eyedropper path below,
                        // and select-only mode, are read-only and stay available).
                        if (DecompMapAssetGuard.BlockIfDecomp(R._("map tile layout")))
                            return;

                        // Wrap the paint commit in an undo scope so ROM writes are reversible.
                        _undo.Begin("MapEditor.PaintTile");
                        bool ok;
                        try
                        {
                            ok = _vm.PaintTileAt(tileX, tileY);
                        }
                        catch (Exception)
                        {
                            _undo.Rollback();
                            throw;
                        }
                        if (ok)
                        {
                            _undo.Commit();
                            // Patch the affected 16x16 region in the cached RGBA buffer so the
                            // map preview reflects the paint without a full re-decompress.
                            PatchPaintedTileIntoCache(tileX, tileY);
                            // Only update the tile-id textbox when a paint succeeded —
                            // PaintTileAt updated SelectedTileId to the newly-painted MAR.
                            TileIdTextBox.Text = _vm.SelectedTileId.ToString("X4");
                        }
                        else
                        {
                            _undo.Rollback();
                        }
                        UpdateTileUI();
                    }
                    else
                    {
                        // Eyedropper: in paint mode without a selected chipset, picking
                        // a tile sets the chipset from its MAR so the next click paints.
                        if (_vm.SelectTile(tileX, tileY))
                            TileIdTextBox.Text = _vm.SelectedTileId.ToString("X4");
                        _vm.EyedropperAt(tileX, tileY);
                        if (_vm.HasChipsetSelected)
                            ChipsetInfoLabel.Text = _vm.ChipsetInfo;
                        UpdateTileUI();
                    }
                }
                else
                {
                    // Existing select-only behaviour (Paint Mode OFF).
                    if (_vm.SelectTile(tileX, tileY))
                        TileIdTextBox.Text = _vm.SelectedTileId.ToString("X4");
                    UpdateTileUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.OnMapImageClick failed: {0}", ex.Message);
            }
        }

        void OnTilePaletteClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                // Pointer position is taken against the Image control (Stretch=None,
                // explicit Width/Height set to bitmap pixel size, so the position is
                // 1:1 source-pixel coordinates).
                var pos = e.GetPosition(TilePaletteImage);
                int px = (int)pos.X;
                int py = (int)pos.Y;
                if (_vm.SelectChipsetFromPaletteClick(px, py))
                {
                    ChipsetInfoLabel.Text = _vm.ChipsetInfo;
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.OnTilePaletteClick failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// After a successful paint, re-render the affected 16x16 region into
        /// <see cref="_lastRgba"/> and push back to the map image control. This
        /// avoids a full LZ77 decompress + re-render for every paint click.
        /// </summary>
        void PatchPaintedTileIntoCache(int tileX, int tileY)
        {
            try
            {
                if (_lastRgba == null) return;
                int pixelW = _vm.MapWidth * 16;
                int pixelH = _vm.MapHeight * 16;
                if (_lastRgba.Length < pixelW * pixelH * 4) return;

                // Re-render full map via the VM is too expensive for every click;
                // instead we delegate to the VM to render JUST the affected tile.
                if (!_vm.RenderTileInto(_lastRgba, pixelW, tileX, tileY))
                {
                    // Fallback: full refresh
                    OnRefreshMap(this, new RoutedEventArgs());
                    return;
                }
                MapImageControl.SetRgbaData(_lastRgba, pixelW, pixelH);
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.PatchPaintedTileIntoCache failed: {0}", ex.Message);
            }
        }

        void OnWriteTile(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasTileSelected) return;

                // #1148: the map tile layout is a raw source-tree asset in decomp mode —
                // export it to source rather than mutating the build-preview ROM.
                if (DecompMapAssetGuard.BlockIfDecomp(R._("map tile layout")))
                    return;

                // Parse hex tile ID from text box
                string text = TileIdTextBox.Text?.Trim() ?? "";
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    text = text.Substring(2);

                if (!int.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int tileId))
                {
                    TileInfoLabel.Text = "ERROR: Invalid hex tile ID";
                    return;
                }

                _vm.SelectedTileId = tileId;

                _undo.Begin("MapEditor.WriteTile");
                bool ok = _vm.WriteTile();
                if (ok)
                {
                    _undo.Commit();
                    // Re-render the map to show the change
                    OnRefreshMap(sender, e);
                }
                else
                {
                    _undo.Rollback();
                }
                UpdateTileUI();
            }
            catch (Exception ex)
            {
                _undo.Rollback();
                Log.Error("MapEditorView.OnWriteTile failed: {0}", ex.Message);
                TileInfoLabel.Text = "ERROR: " + ex.Message;
            }
        }

        void OnRefreshMap(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentAddr == 0) return;
                byte[] rgba = _vm.LoadMapImage(_vm.CurrentAddr, _vm.MapId);
                _lastRgba = rgba;
                UpdateUI(rgba);
                UpdateTilePalette();
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.OnRefreshMap failed: {0}", ex.Message);
            }
        }

        void UpdateTileUI()
        {
            TileInfoLabel.Text = string.IsNullOrEmpty(_vm.TileInfo) ? "No tile selected" : _vm.TileInfo;
            TileEditPanel.IsVisible = _vm.HasTileSelected;
        }

        /// <summary>
        /// Export the current map's tile data (width/height header + row-major
        /// u16 MAR values) to a CSV file via the platform file picker. See
        /// <see cref="MapExportCsv.Serialize"/> for the format. Read-only — does
        /// not touch the ROM. (#658 slice B)
        /// </summary>
        async void ExportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] cachedMap = _vm.GetMapDataSnapshot();
                if (cachedMap == null || cachedMap.Length < 2)
                {
                    CoreState.Services?.ShowError(R._("No map data loaded — select a map first."));
                    return;
                }
                if (StorageProvider == null) return;
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export Map (CSV)"),
                    DefaultExtension = "csv",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });
                if (file == null) return;
                string csv = MapExportCsv.Serialize(cachedMap);
                if (string.IsNullOrEmpty(csv))
                {
                    CoreState.Services?.ShowError(R._("Map data is invalid or too small."));
                    return;
                }
                string path = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    CoreState.Services?.ShowError(R._("Could not resolve a local file path for export."));
                    return;
                }
                File.WriteAllText(path, csv);
                CoreState.Services?.ShowInfo(string.Format(R._("Exported map to {0} ({1} chars)."), file.Name, csv.Length));
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.ExportCsv_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(string.Format(R._("Export failed: {0}"), ex.Message));
            }
        }

        /// <summary>
        /// Import a CSV file (produced by <see cref="ExportCsv_Click"/>) and apply the
        /// full grid of MAR values to the currently-loaded map under a single undo scope.
        /// Requires an exact W×H match with the selected map; resize is not supported.
        /// (#1382)
        /// </summary>
        async void ImportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] cachedMap = _vm.GetMapDataSnapshot();
                if (cachedMap == null || cachedMap.Length < 2)
                {
                    CoreState.Services?.ShowError(R._("No map data loaded — select a map first."));
                    return;
                }

                // Map tile layout is a source asset in decomp mode — block writes.
                if (DecompMapAssetGuard.BlockIfDecomp(R._("map tile layout")))
                    return;

                if (StorageProvider == null) return;

                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import Map (CSV)"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });

                if (files == null || files.Count == 0) return;
                var file = files[0];

                // Read via the storage stream API so import works on providers that
                // don't expose a local path (Android SAF, sandboxed environments).
                string csv;
                using (var stream = await file.OpenReadAsync())
                using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    csv = await reader.ReadToEndAsync();
                }

                if (!MapExportCsv.Parse(csv, out int w, out int h, out ushort[] mars, out string parseErr))
                {
                    CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), parseErr));
                    return;
                }

                bool ok;
                string applyErr;
                uint addr;
                _undo.Begin("MapEditor.ImportCsv");
                try
                {
                    ok = _vm.ApplyMapGrid(mars, w, h, out applyErr, out addr);
                }
                catch (Exception)
                {
                    _undo.Rollback();
                    throw;
                }

                if (ok)
                {
                    _undo.Commit();
                    OnRefreshMap(this, new RoutedEventArgs());
                    UpdateTilePalette();
                    CoreState.Services?.ShowInfo(string.Format(R._("Imported map from {0} ({1}x{2} tiles)."), file.Name, w, h));
                }
                else
                {
                    _undo.Rollback();
                    CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), applyErr));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.ImportCsv_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), ex.Message));
            }
        }

        /// <summary>
        /// Export the current map as a Tiled project: a <c>.tmx</c> (default
        /// <c>&lt;tile gid&gt;</c> XML layer), a matching <c>.tsx</c> tileset, and the
        /// chipset PNG so Tiled renders the canvas faithfully. The three files are
        /// written as siblings (same base name) from a single save dialog. Read-only —
        /// does not touch the ROM. See <see cref="MapTmxCore"/> for the GID↔MAR
        /// convention. (#1387)
        /// </summary>
        async void ExportTmx_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] cachedMap = _vm.GetMapDataSnapshot();
                if (cachedMap == null || cachedMap.Length < 2)
                {
                    CoreState.Services?.ShowError(R._("No map data loaded — select a map first."));
                    return;
                }
                if (StorageProvider == null) return;

                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export Map (Tiled .tmx)"),
                    DefaultExtension = "tmx",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Tiled map files") { Patterns = new[] { "*.tmx" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });
                if (file == null) return;

                // The three-file (.tmx + .tsx + .png) export needs a local sibling
                // directory; resolve it via the picker's local path (same constraint
                // as the CSV export). On providers without one (sandboxed SAF), fail
                // cleanly without writing anything.
                string tmxPath = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(tmxPath))
                {
                    CoreState.Services?.ShowError(R._("Tiled export requires a local file path (not supported on this storage provider)."));
                    return;
                }

                string dir = Path.GetDirectoryName(tmxPath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(tmxPath);
                if (string.IsNullOrEmpty(baseName)) baseName = "map";
                string tsxName = baseName + ".tsx";
                string pngName = baseName + ".png";
                string tsxPath = Path.Combine(dir, tsxName);
                string pngPath = Path.Combine(dir, pngName);

                // Chipset PNG (32-column grid, 16x16 chipsets) — same renderer as the
                // live palette, so Tiled matches the in-game render.
                byte[] paletteRgba = _vm.RenderChipsetPalette(out int pw, out int ph);
                if (paletteRgba == null || pw <= 0 || ph <= 0)
                {
                    CoreState.Services?.ShowError(R._("Could not render the chipset image for the Tiled tileset."));
                    return;
                }
                var bmp = IconBitmapBuilder.FromRgba(paletteRgba, pw, ph);
                if (bmp == null || !ImageExportService.SavePngToFile(bmp, pngPath))
                {
                    CoreState.Services?.ShowError(R._("Failed to write the chipset PNG."));
                    return;
                }

                int tileCount = (pw / MapTmxCore.TILE_PIXELS) * (ph / MapTmxCore.TILE_PIXELS);
                string tsx = MapTmxCore.SerializeTsx(pngName, pw, ph, tileCount);
                string tmx = MapTmxCore.SerializeTmx(cachedMap, tsxName);
                if (string.IsNullOrEmpty(tmx))
                {
                    CoreState.Services?.ShowError(R._("Map data is invalid or too small."));
                    return;
                }

                File.WriteAllText(tsxPath, tsx);
                File.WriteAllText(tmxPath, tmx);
                CoreState.Services?.ShowInfo(string.Format(
                    R._("Exported Tiled map to {0} (+ {1}, {2})."), file.Name, tsxName, pngName));
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.ExportTmx_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(string.Format(R._("Export failed: {0}"), ex.Message));
            }
        }

        /// <summary>
        /// Import a Tiled <c>.tmx</c> file (any common encoding: CSV / default XML /
        /// Base64 / Base64+gzip / Base64+zlib) and apply its tile layer to the
        /// currently-loaded map. Reuses the exact CSV import path:
        /// <see cref="MapTmxCore.ParseTmx"/> → <see cref="MapEditorViewModel.ApplyMapGrid"/>
        /// under one undo scope. Requires an exact W×H match; resize is not supported.
        /// Blocked in decomp mode (map tile layout is a source asset). (#1387)
        /// </summary>
        async void ImportTmx_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] cachedMap = _vm.GetMapDataSnapshot();
                if (cachedMap == null || cachedMap.Length < 2)
                {
                    CoreState.Services?.ShowError(R._("No map data loaded — select a map first."));
                    return;
                }

                // Map tile layout is a source asset in decomp mode — block writes.
                if (DecompMapAssetGuard.BlockIfDecomp(R._("map tile layout")))
                    return;

                if (StorageProvider == null) return;

                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import Map (Tiled .tmx)"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Tiled map files") { Patterns = new[] { "*.tmx" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });

                if (files == null || files.Count == 0) return;
                var file = files[0];

                // Read via the storage stream API so import works on providers that
                // don't expose a local path (Android SAF, sandboxed environments).
                string xml;
                using (var stream = await file.OpenReadAsync())
                using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    xml = await reader.ReadToEndAsync();
                }

                if (!MapTmxCore.ParseTmx(xml, out int w, out int h, out ushort[] mars, out string parseErr))
                {
                    CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), parseErr));
                    return;
                }

                bool ok;
                string applyErr;
                uint addr;
                _undo.Begin("MapEditor.ImportTmx");
                try
                {
                    ok = _vm.ApplyMapGrid(mars, w, h, out applyErr, out addr);
                }
                catch (Exception)
                {
                    _undo.Rollback();
                    throw;
                }

                if (ok)
                {
                    _undo.Commit();
                    OnRefreshMap(this, new RoutedEventArgs());
                    UpdateTilePalette();
                    CoreState.Services?.ShowInfo(string.Format(R._("Imported map from {0} ({1}x{2} tiles)."), file.Name, w, h));
                }
                else
                {
                    _undo.Rollback();
                    CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), applyErr));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.ImportTmx_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(string.Format(R._("Import failed: {0}"), ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
