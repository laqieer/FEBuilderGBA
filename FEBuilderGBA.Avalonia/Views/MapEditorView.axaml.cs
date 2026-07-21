using global::Avalonia;
using System;
using System.IO;
using System.Threading;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly MapEditorViewModel _vm = new();
        bool _hasLoadedList;
        readonly UndoService _undo = new();
        int _zoom = 1;
        byte[] _lastRgba; // cached for refresh
        bool _generatingRandomMap;
        readonly RandomMapOneClickService _randomMapService;
        CancellationTokenSource? _randomMapCts;

        public string ViewTitle => "Visual Map Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Visual Map Editor", 1200, 800, MinWidth: 1170, MinHeight: 640);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapEditorView()
        {
            InitializeComponent();
            _randomMapService = new RandomMapOneClickService();
            EntryList.SelectedAddressChanged += OnSelected;
            ZoomInBtn.Click += OnZoomIn;
            ZoomOutBtn.Click += OnZoomOut;
            MapImageControl.PointerPressed += OnMapImageClick;
            WriteTileBtn.Click += OnWriteTile;
            RefreshMapBtn.Click += OnRefreshMap;
            ExportCsvButton.Click += ExportCsv_Click;
            ImportCsvButton.Click += ImportCsv_Click;
            ExportTmxButton.Click += ExportTmx_Click;
            ImportTmxButton.Click += ImportTmx_Click;
            ResizeMapButton.Click += ResizeMap_Click;
            GenerateRandomMapButton.Click += GenerateRandomMap_Click;
            CancelRandomMapButton.Click += CancelRandomMap_Click;
            RandomizeSeedButton.Click += RandomizeSeed_Click;
            MapTilesetButton.Click += MapTileset_Click;
            // #1978 Slice 3: cancel any in-flight one-click generation when the editor is
            // detached (map switched/editor closed) so a stale generation can never apply.
            DetachedFromVisualTree += (_, _) => _randomMapCts?.Cancel();
            AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            // Paint Mode defaults to OFF (no regression to existing select behaviour).
            PaintModeCheck.IsChecked = false;
            // Hit-test the outer Border (Background=Transparent) only — clicks on the
            // inner Image bubble up here via Avalonia's routed PointerPressed event.
            // Wiring both would double-fire the handler. The handler converts pointer
            // coords to image-pixel coords via e.GetPosition(TilePaletteImage).
            TilePaletteHitArea.PointerPressed += OnTilePaletteClick;
            // #1998: cap the upper controls scroller height from the right column's
            // arranged height so a compact browser viewport scrolls the controls
            // instead of squeezing/overflowing the pinned map canvas below it.
            MapEditorRightColumnGrid.SizeChanged += OnRightColumnSizeChanged;
        }

        /// <summary>
        /// #1998: minimum usable Map Canvas height guaranteed even in a compact
        /// (browser-zoomed) viewport. Mirrors MapCanvasPanel's XAML MinHeight and
        /// MapEditorButtonReadabilityTests' MinimumUsableMapHeight constant.
        /// </summary>
        internal const double MapCanvasMinHeight = 240;

        /// <summary>
        /// #1998 (review PRRT_kwDOH0Mc1M6STCQB) — MapCanvasPanel's XAML
        /// <c>Margin="8,0,8,8"</c> (top=0, bottom=8) consumes additional vertical space
        /// in the star row beyond the panel's own MinHeight. Reserving only
        /// <see cref="MapCanvasMinHeight"/> left this 8 DIP unaccounted for, letting the
        /// upper-controls cap eat into the panel's bottom margin and clip/push its
        /// bottom edge (including its horizontal scrollbar) off-screen.
        /// </summary>
        internal const double MapCanvasVerticalMargin = 8;

        /// <summary>
        /// #1998 (review PRRT_kwDOH0Mc1M6STCQB) — the panel's full reserved vertical
        /// footprint (own MinHeight plus its vertical margins). This, not
        /// <see cref="MapCanvasMinHeight"/> alone, is what the upper-controls cap must
        /// subtract from the available height to actually guarantee containment.
        /// </summary>
        internal const double MapCanvasMinFootprint = MapCanvasMinHeight + MapCanvasVerticalMargin;

        /// <summary>
        /// #1998: minimum height left for the upper controls scroller even when the
        /// available viewport is extremely short, so the toolbar/palette/tile-editor
        /// region never fully collapses to zero.
        /// </summary>
        internal const double UpperControlsMinHeight = 80;

        /// <summary>
        /// #1998 (review: gemini-3.5-flash) — guard against resize-loop jitter from
        /// floating-point layout noise. Only re-apply MaxHeight when the requested
        /// change exceeds this many device-independent pixels.
        /// </summary>
        const double MaxHeightChangeEpsilon = 0.5;

        void OnRightColumnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdateUpperControlsMaxHeight(e.NewSize.Height);
        }

        void UpdateUpperControlsMaxHeight(double availableHeight)
        {
            double newMaxHeight = ComputeUpperControlsMaxHeight(availableHeight, MapCanvasMinFootprint, UpperControlsMinHeight);
            double current = MapUpperControlsScroller.MaxHeight;
            // Guarded assignment (never a fixed Height, never an Arrange override): skip
            // the write entirely when the change is sub-pixel-jitter sized, so repeated
            // layout passes triggered by our own MaxHeight write cannot loop.
            if (double.IsNaN(current) || Math.Abs(current - newMaxHeight) > MaxHeightChangeEpsilon)
            {
                MapUpperControlsScroller.MaxHeight = newMaxHeight;
            }

            // #1998 (review PRRT_kwDOH0Mc1M6STCQa) — MaxHeight alone only CAPS the upper
            // row; it never guaranteed an actual arranged floor. The previous
            // "controls floor wins" claim only happened to hold because the controls'
            // natural content is always taller than UpperControlsMinHeight in practice,
            // not because anything enforced it. Apply a REAL MinHeight too, but only when
            // there is room for BOTH floors simultaneously — forcing it below that
            // combined threshold would starve MapCanvasPanel's own reserved footprint and
            // reproduce the exact containment bug from PRRT_kwDOH0Mc1M6STCQB, so canvas
            // containment always wins in that documented edge case instead.
            double newMinHeight = ComputeUpperControlsMinHeight(availableHeight, MapCanvasMinFootprint, UpperControlsMinHeight);
            double currentMin = MapUpperControlsScroller.MinHeight;
            if (double.IsNaN(currentMin) || Math.Abs(currentMin - newMinHeight) > MaxHeightChangeEpsilon)
            {
                MapUpperControlsScroller.MinHeight = newMinHeight;
            }
        }

        /// <summary>
        /// #1998 pure helper: compute the MaxHeight to apply to the upper controls
        /// ScrollViewer so the pinned Map Canvas below it keeps at least
        /// <paramref name="canvasMinHeight"/> px, while the controls region itself
        /// keeps at least <paramref name="controlsMinHeight"/> px even in an
        /// extremely short viewport. Returns <see cref="double.PositiveInfinity"/>
        /// (no cap — natural desktop sizing) when <paramref name="availableHeight"/>
        /// isn't a usable finite measurement yet. Exposed internal so edge cases can
        /// be unit-tested directly without a full headless layout pass.
        /// </summary>
        internal static double ComputeUpperControlsMaxHeight(double availableHeight, double canvasMinHeight, double controlsMinHeight)
        {
            if (double.IsNaN(availableHeight) || double.IsInfinity(availableHeight) || availableHeight <= 0)
                return double.PositiveInfinity;

            double budget = availableHeight - canvasMinHeight;
            return Math.Max(controlsMinHeight, budget);
        }

        /// <summary>
        /// #1998 (review PRRT_kwDOH0Mc1M6STCQa) pure helper: compute the actual MinHeight
        /// floor to apply to the upper controls ScrollViewer. Returns
        /// <paramref name="controlsMinHeight"/> only when <paramref name="availableHeight"/>
        /// has room for BOTH the controls floor AND the canvas's full reserved footprint
        /// (<paramref name="canvasMinFootprint"/>) at the same time; otherwise returns 0
        /// (no forced floor) so MapCanvasPanel's own containment always takes priority in
        /// this documented, pathologically-short edge case. Returns 0 (rather than
        /// <see cref="double.PositiveInfinity"/>) when <paramref name="availableHeight"/>
        /// isn't a usable finite measurement yet, matching Avalonia's default MinHeight.
        /// </summary>
        internal static double ComputeUpperControlsMinHeight(double availableHeight, double canvasMinFootprint, double controlsMinHeight)
        {
            if (double.IsNaN(availableHeight) || double.IsInfinity(availableHeight) || availableHeight <= 0)
                return 0;

            return availableHeight >= canvasMinFootprint + controlsMinHeight ? controlsMinHeight : 0;
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

        void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (HandleEditorKeyDown(e.Key, e.KeyModifiers, e.Source))
                e.Handled = true;
        }

        internal bool HandleEditorKeyDown(Key key, KeyModifiers modifiers)
            => IsUndoGesture(key, modifiers) && TryRunEditorUndo();

        internal bool HandleEditorKeyDown(Key key, KeyModifiers modifiers, object? source)
            => !IsTextInputSource(source) && HandleEditorKeyDown(key, modifiers);

        internal static bool IsTextInputSource(object? source)
        {
            if (source is TextBox) return true;
            return source is Control control && control.FindAncestorOfType<TextBox>() != null;
        }

        internal static bool IsUndoGesture(Key key, KeyModifiers modifiers)
        {
            if (key != Key.Z) return false;
            if ((modifiers & KeyModifiers.Shift) != 0) return false;
            return (modifiers & KeyModifiers.Control) != 0
                || (modifiers & KeyModifiers.Meta) != 0;
        }

        internal bool TryRunEditorUndo()
        {
            var undo = CoreState.Undo;
            if (undo == null || undo.Postion <= 0) return false;

            int before = undo.Postion;
            try
            {
                undo.RunUndo();
                if (undo.Postion == before) return false;
                OnEditorUndoApplied();
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapEditorView.TryRunEditorUndo failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Undo failed: {0}", ex.Message));
                return false;
            }
        }

        protected virtual void OnEditorUndoApplied()
        {
            OnRefreshMap(this, new RoutedEventArgs());
            UpdateTileUI();
            UndoService.NotifyUnsavedChanges();
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
                Log.ErrorF("MapEditorView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("MapEditorView.OnSelected failed: {0}", ex.Message);
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

        void UpdateTilePaletteCore(bool strict)
        {
            byte[] paletteRgba = _vm.RenderChipsetPalette(out int pw, out int ph);
            if (paletteRgba == null || pw <= 0 || ph <= 0)
            {
                TilePaletteImage.Source = null;
                TilePaletteImage.Width = double.NaN;
                TilePaletteImage.Height = double.NaN;
                ChipsetInfoLabel.Text = R._("No chipset selected");
                if (strict)
                    throw new InvalidOperationException(
                        R._("Failed to render the generated map chipset palette."));
                return;
            }
            var bmp = IconBitmapBuilder.FromRgba(paletteRgba, pw, ph);
            if (bmp == null)
            {
                TilePaletteImage.Source = null;
                ChipsetInfoLabel.Text = R._("No chipset selected");
                if (strict)
                    throw new InvalidOperationException(
                        R._("Failed to render the generated map chipset palette."));
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

        /// <summary>Re-render the chipset palette for the currently-loaded map.</summary>
        void UpdateTilePalette()
        {
            try
            {
                UpdateTilePaletteCore(strict: false);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapEditorView.UpdateTilePalette failed: {0}", ex.Message);
            }
        }

        internal void UpdateTilePaletteStrict()
            => UpdateTilePaletteCore(strict: true);

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
                Log.ErrorF("MapEditorView.OnMapImageClick failed: {0}", ex.Message);
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
                Log.ErrorF("MapEditorView.OnTilePaletteClick failed: {0}", ex.Message);
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
                Log.ErrorF("MapEditorView.PatchPaintedTileIntoCache failed: {0}", ex.Message);
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
                Log.ErrorF("MapEditorView.OnWriteTile failed: {0}", ex.Message);
                TileInfoLabel.Text = "ERROR: " + ex.Message;
            }
        }

        void OnRefreshMap(object? sender, RoutedEventArgs e)
        {
            try
            {
                RefreshMapFromCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapEditorView.OnRefreshMap failed: {0}", ex.Message);
            }
        }

        void RefreshMapFromCurrentSelection()
        {
            if (_vm.CurrentAddr == 0) return;
            byte[] rgba = _vm.LoadMapImage(_vm.CurrentAddr, _vm.MapId);
            _lastRgba = rgba;
            UpdateUI(rgba);
            UpdateTilePalette();
        }

        internal void RefreshMapImageFromCurrentSelectionStrict()
        {
            if (_vm.CurrentAddr == 0)
                throw new InvalidOperationException(R._("No map data loaded — select a map first."));

            byte[] rgba = _vm.LoadMapImage(_vm.CurrentAddr, _vm.MapId);
            if (rgba == null)
                throw new InvalidOperationException(R._("Failed to render the generated map."));

            _lastRgba = rgba;
            UpdateUI(rgba);
        }

        internal void RefreshMapFromCurrentSelectionStrict()
        {
            RefreshMapImageFromCurrentSelectionStrict();
            UpdateTilePaletteStrict();
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
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
                // #1639: write via the SAF bridge so Android content:// targets
                // (no local path) are written through OpenWriteAsync.
                string? written = await FileDialogHelper.WriteViaAsync(file, p => File.WriteAllText(p, csv));
                if (written == null) return;
                // Use the bridge's returned label (local filename on desktop, SAF
                // display name on Android) for a consistent, provider-safe message.
                CoreState.Services?.ShowInfo(string.Format(R._("Exported map to {0} ({1} chars)."), Path.GetFileName(written), csv.Length));
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapEditorView.ExportCsv_Click failed: {0}", ex.Message);
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

                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
        /// Export the current map as a Tiled project: a map file — <c>.tmx</c> (XML,
        /// default <c>&lt;tile gid&gt;</c> layer) or <c>.tmj</c> (JSON, GID array), chosen
        /// by the picked filename's extension (#1796) — plus a matching <c>.tsx</c> tileset
        /// and the chipset PNG so Tiled renders the canvas faithfully. The three files are
        /// written as siblings (same base name) from a single save dialog. Read-only —
        /// does not touch the ROM. See <see cref="MapTmxCore"/> for the GID↔MAR
        /// convention. (#1387, #1796)
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
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export Map (Tiled)"),
                    DefaultExtension = "tmx",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Tiled map (XML)") { Patterns = new[] { "*.tmx" } },
                        new FilePickerFileType("Tiled map (JSON)") { Patterns = new[] { "*.tmj" } },
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

                // Build AND validate the text artifacts BEFORE writing any file, so a
                // failure can't leave a stray PNG/TSX behind ("validate-all-before-write").
                int tileCount = (pw / MapTmxCore.TILE_PIXELS) * (ph / MapTmxCore.TILE_PIXELS);
                string tsx = MapTmxCore.SerializeTsx(pngName, pw, ph, tileCount);
                // Dispatch on the actual chosen filename's extension: .tmj -> JSON, else .tmx.
                bool exportJson = Path.GetExtension(tmxPath).Equals(".tmj", StringComparison.OrdinalIgnoreCase);
                string mapText = exportJson
                    ? MapTmxCore.SerializeTmj(cachedMap, tsxName)
                    : MapTmxCore.SerializeTmx(cachedMap, tsxName);
                if (string.IsNullOrEmpty(mapText))
                {
                    CoreState.Services?.ShowError(R._("Map data is invalid or too small."));
                    return;
                }

                // WriteableBitmap is IDisposable — dispose after writing the PNG so
                // repeated exports don't leak the unmanaged backing buffer.
                using (var bmp = IconBitmapBuilder.FromRgba(paletteRgba, pw, ph))
                {
                    if (bmp == null || !ImageExportService.SavePngToFile(bmp, pngPath))
                    {
                        CoreState.Services?.ShowError(R._("Failed to write the chipset PNG."));
                        return;
                    }
                }

                File.WriteAllText(tsxPath, tsx);
                File.WriteAllText(tmxPath, mapText);
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
        /// Import a Tiled map — <c>.tmx</c> (XML) or <c>.tmj</c> (JSON), auto-detected by
        /// extension with a content sniff fallback (#1796); any common tile-layer encoding
        /// (CSV / default XML / GID array / Base64 / Base64+gzip / Base64+zlib) — and apply
        /// its tile layer to the currently-loaded map. Reuses the exact CSV import path:
        /// <see cref="MapTmxCore.ParseTmx"/> / <see cref="MapTmxCore.ParseTmj"/> →
        /// <see cref="MapEditorViewModel.ApplyMapGrid"/>
        /// under one undo scope. Requires an exact W×H match; resize is not supported.
        /// Blocked in decomp mode (map tile layout is a source asset). (#1387, #1796)
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

                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import Map (Tiled)"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Tiled map files") { Patterns = new[] { "*.tmx", "*.tmj" } },
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

                // Dispatch on the chosen filename's extension, with a content sniff
                // fallback (a leading '{' after any BOM/whitespace means JSON) —
                // MapTmxCore.LooksLikeTmj encapsulates and unit-tests this decision.
                string chosenName = file.Name ?? "";
                bool importJson = MapTmxCore.LooksLikeTmj(chosenName, xml);

                bool parsed;
                int w, h;
                ushort[] mars;
                string parseErr;
                if (importJson)
                    parsed = MapTmxCore.ParseTmj(xml, out w, out h, out mars, out parseErr);
                else
                    parsed = MapTmxCore.ParseTmx(xml, out w, out h, out mars, out parseErr);
                if (!parsed)
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

        /// <summary>
        /// Resize the currently-loaded map. Opens the existing resize dialog (pre-filled
        /// with the current W×H), and on OK applies the T/L/R/B padding via
        /// <see cref="MapEditorViewModel.ApplyMapResize"/> under one undo scope — the same
        /// compress → free-space-write → repoint pipeline the CSV/TMX import uses, so a map
        /// that grows is relocated to free space instead of overwriting trailing data.
        /// Invalid sizes (below 15×10 or above the FE height-dependent width limit) are
        /// rejected with an error and leave the ROM untouched. (#1735)
        /// </summary>
        async void ResizeMap_Click(object? sender, RoutedEventArgs e)
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

                MapEditorResizeDialogView? dialog = null;
                bool confirmed = await WindowManager.Instance.OpenModal<MapEditorResizeDialogView, bool>(
                    TopLevel.GetTopLevel(this) as Window,
                    d =>
                    {
                        dialog = d;
                        d.SetPosition(0, 0, _vm.MapWidth, _vm.MapHeight);
                    });
                if (!confirmed) return;

                    if (dialog is null) return;
                    if (dialog.DataViewModel is not MapEditorResizeDialogViewModel dlgVm) return;

                    int top = dlgVm.PaddingTop, left = dlgVm.PaddingLeft;
                int right = dlgVm.PaddingRight, bottom = dlgVm.PaddingBottom;
                if (top == 0 && left == 0 && right == 0 && bottom == 0)
                    return; // nothing to do

                bool ok;
                string applyErr;
                uint addr;
                _undo.Begin("MapEditor.Resize");
                try
                {
                    ok = _vm.ApplyMapResize(top, left, right, bottom, 0, out applyErr, out addr);
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
                    CoreState.Services?.ShowInfo(string.Format(R._("Resized map to {0}x{1} tiles."), _vm.MapWidth, _vm.MapHeight));
                }
                else
                {
                    _undo.Rollback();
                    CoreState.Services?.ShowError(string.Format(R._("Resize failed: {0}"), applyErr));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.ResizeMap_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(string.Format(R._("Resize failed: {0}"), ex.Message));
            }
        }

        async void GenerateRandomMap_Click(object? sender, RoutedEventArgs e)
        {
            if (_generatingRandomMap)
                return;

            var cts = new CancellationTokenSource();
            _randomMapCts = cts;

            // #1978 Slice 3 review finding #4: every exit path below must leave a deterministic
            // Ready/Failed/Cancelled/backend status rather than the transient "Generating..."
            // text set just below, whether it exits via return, exception, or cancellation.
            void Fail(string message, string notice = "")
            {
                ShowError(string.IsNullOrWhiteSpace(notice) ? message : notice + " " + message);
                SetRandomMapBusyState(false, R._("Failed."));
            }
            void CancelStatus()
            {
                SetRandomMapBusyState(false, R._("Cancelled."));
            }

            try
            {
                _generatingRandomMap = true;
                SetRandomMapBusyState(true, R._("Generating..."));

                if (!GenerateRandomMapWorkflow.TryPrepareForGeneration(
                    _vm,
                    assetName => DecompMapAssetGuard.BlockIfDecomp(assetName),
                    msg => Fail(msg)))
                {
                    SetRandomMapBusyState(false, R._("Failed."));
                    return;
                }

                if (!_vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError))
                {
                    Fail(string.Format(R._("Generate random map failed: {0}"), identityError));
                    return;
                }

                if (!TryGetSeed(out int seed, out string seedError))
                {
                    Fail(seedError);
                    return;
                }
                RandomMapSeedTextBox.Text = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

                uint mapSettingAddr = _vm.CurrentAddr;
                int width = _vm.MapWidth;
                int height = _vm.MapHeight;

                if (!BuiltInRandomMapTilesetCore.TryResolveMapTileset(CoreState.ROM, mapSettingAddr, out MapTilesetSnapshot snapshot, out string tilesetError))
                {
                    Fail(string.Format(R._("Generate random map failed: {0}"), tilesetError));
                    return;
                }
                TilesetFingerprint expectedFingerprint = snapshot.Fingerprint;
                if (!TryBuildCurrentGrid(
                    _vm.GetMapDataSnapshot(),
                    width,
                    height,
                    out ushort[] currentGrid))
                {
                    Fail(string.Format(
                        R._("Generate random map failed: {0}"),
                        R._("The current map data could not be decoded.")));
                    return;
                }

                RandomMapOneClickResult result;
                try
                {
                    result = await _randomMapService.GenerateAsync(
                        CoreState.ROM, mapSettingAddr, width, height, currentGrid, seed, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    CancelStatus();
                    return;
                }

                if (result.Cancelled)
                {
                    // Review finding #2: a stale/invalid-mapping notice must still be visible
                    // even when the backend attempt ends in cancellation, not only on failure.
                    string mappingNotice = FormatMappingNotice(result);
                    if (!string.IsNullOrWhiteSpace(mappingNotice))
                        CoreState.Services?.ShowInfo(mappingNotice);
                    CancelStatus();
                    return;
                }

                string notice = FormatMappingNotice(result);
                if (!result.Success || result.Outcome == null)
                {
                    Fail(string.Format(R._("Generate random map failed: {0}"), result.ErrorMessage), notice);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(notice))
                    CoreState.Services?.ShowInfo(notice);

                RandomMapGenerationOutcome outcome = result.Outcome;
                if (outcome.Mars == null || outcome.Mars.Length != outcome.Width * outcome.Height)
                {
                    Fail(R._("Random map generation returned no map data."));
                    return;
                }

                // Write the directly-replayable seed back so the user can reproduce this
                // exact layout later by generating again with the same displayed value.
                RandomMapSeedTextBox.Text = outcome.EffectiveSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // Mandatory re-check immediately before the UI-thread apply (review finding #1
                // remainder): a cancellation observed after the backend call returned (e.g. this
                // view detaching/closing while marshalling back to the UI thread) must never
                // reach ApplyGeneratedMapOnUiThreadAsync's ROM mutation.
                if (cts.Token.IsCancellationRequested)
                {
                    CancelStatus();
                    return;
                }

                // The FEMapCreator shell-out / built-in search happens inside
                // RandomMapOneClickService off the UI thread. The workflow marshals every
                // ROM/undo/cache mutation and the success notification back onto the
                // Avalonia UI thread, and re-validates identity + tileset fingerprint
                // immediately before writing so a stale context never mutates the ROM.
                string? applyError =
                    await GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                        _vm,
                        _undo,
                        outcome,
                        writeIdentity,
                        expectedFingerprint,
                        cts.Token,
                        RefreshMapImageFromCurrentSelectionStrict,
                        UpdateTilePaletteStrict,
                        RefreshMapFromCurrentSelectionStrict,
                        message => CoreState.Services?.ShowInfo(message));

                if (!string.IsNullOrWhiteSpace(applyError))
                    Fail(string.Format(R._("Generate random map failed: {0}"), applyError));
                else
                    SetRandomMapBusyState(false, FormatBackendStatus(result.BackendUsed));
            }
            catch (OperationCanceledException)
            {
                CancelStatus();
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.GenerateRandomMap_Click failed: " + ex.ToString());
                Fail(string.Format(R._("Generate random map failed: {0}"), ex.Message));
            }
            finally
            {
                // Deterministic, race-safe disposal (#1978 Slice 3 re-review finding #1): only
                // clear the shared field if it still refers to THIS invocation's own source (the
                // _generatingRandomMap guard above prevents a second concurrent invocation from
                // ever replacing it), then dispose it. DetachedFromVisualTree only ever calls
                // Cancel() — never Dispose() — so it can safely race this finally without ever
                // cancelling an already-disposed source.
                if (ReferenceEquals(_randomMapCts, cts))
                    _randomMapCts = null;
                cts.Dispose();
                _generatingRandomMap = false;
                // Do not overwrite the deterministic status text already set above (Fail/
                // CancelStatus/success-backend line) with a null-text no-op; just make sure the
                // controls are re-enabled even on an unexpected early exit.
                SetRandomMapBusyState(false);
            }
        }

        internal void SetRandomMapBusyState(bool busy, string? statusText = null)
        {
            GenerateRandomMapButton.IsEnabled = !busy;
            CancelRandomMapButton.IsEnabled = busy;
            RandomizeSeedButton.IsEnabled = !busy;
            MapTilesetButton.IsEnabled = !busy;
            RandomMapSeedTextBox.IsEnabled = !busy;
            if (statusText != null)
                RandomMapStatusLabel.Text = statusText;
        }

        void CancelRandomMap_Click(object? sender, RoutedEventArgs e) => _randomMapCts?.Cancel();

        void RandomizeSeed_Click(object? sender, RoutedEventArgs e)
        {
            RandomMapSeedTextBox.Text = Random.Shared.Next().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// #1978 Slice 3 review finding #5: this button no longer owns a standalone mapping
        /// dialog. It only identifies the current map's tileset fingerprint and navigates to the
        /// FEMapCreator section of Options (Plan v4 §4/§7's intended home for discovery,
        /// discovered-tileset selection, and per-fingerprint mapping save/status/stale
        /// guidance), passing that fingerprint as context. Generation itself remains one-click
        /// and never opens this or any other dialog.
        /// </summary>
        async void MapTileset_Click(object? sender, RoutedEventArgs e)
        {
            if (!BuiltInRandomMapTilesetCore.TryResolveMapTileset(CoreState.ROM, _vm.CurrentAddr, out MapTilesetSnapshot snapshot, out string error))
            {
                ShowError(string.Format(R._("Could not identify this map's tileset: {0}"), error));
                return;
            }

            await WindowManager.Instance.OpenModal<OptionsView>(
                TopLevel.GetTopLevel(this) as Window,
                view => view.SetTilesetContext(snapshot.Fingerprint));
        }

        internal bool TryGetSeed(out int seed, out string error)
        {
            error = "";
            string? text = RandomMapSeedTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                seed = Random.Shared.Next();
                RandomMapSeedTextBox.Text = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            if (int.TryParse(text, out seed))
                return true;

            seed = 0;
            error = R._("Seed must be a valid 32-bit integer.");
            return false;
        }

        internal static string FormatMappingNotice(RandomMapOneClickResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.MappingStatus != FEMapCreatorMappingStatus.Stale
                && result.MappingStatus != FEMapCreatorMappingStatus.Invalid)
            {
                return "";
            }

            string reason = result.MappingReason switch
            {
                FEMapCreatorMappingReason.StoredEntryMissingRequiredFields =>
                    R._("The saved mapping entry is missing required fields."),
                FEMapCreatorMappingReason.ImageUnreadable =>
                    R._("The mapped image file is no longer readable."),
                FEMapCreatorMappingReason.ImageChanged =>
                    R._("The mapped image file has changed since the mapping was saved."),
                FEMapCreatorMappingReason.GenerationDataUnreadable =>
                    R._("The mapped generation-data file is no longer readable."),
                FEMapCreatorMappingReason.GenerationDataChanged =>
                    R._("The mapped generation-data file has changed since the mapping was saved."),
                FEMapCreatorMappingReason.ProfileUnavailable =>
                    R._("FEMapCreator is no longer configured or valid."),
                FEMapCreatorMappingReason.ExecutablePathChanged =>
                    R._("The configured FEMapCreator executable path has changed."),
                FEMapCreatorMappingReason.ExecutableContentChanged =>
                    R._("The configured FEMapCreator executable content has changed."),
                FEMapCreatorMappingReason.AssetsRootChanged =>
                    R._("The configured FEMapCreator assets root has changed."),
                _ => R._("The saved FEMapCreator tileset mapping is no longer valid."),
            };
            string format = result.MappingStatus == FEMapCreatorMappingStatus.Stale
                ? R._("The saved FEMapCreator tileset mapping is stale ({0}); using the built-in generator instead.")
                : R._("The saved FEMapCreator tileset mapping is invalid ({0}); using the built-in generator instead.");
            return string.Format(format, reason);
        }

        internal static string FormatBackendStatus(RandomMapBackendUsed backend)
        {
            string backendName = backend == RandomMapBackendUsed.External
                ? R._("FEMapCreator Experimental")
                : R._("Built-in Experimental");
            return string.Format(R._("Backend: {0}"), backendName);
        }

        /// <summary>
        /// Convert the currently cached map bytes into a row-major MAR grid for the built-in
        /// engine's source-identity ladder. Fails closed when the cache is unavailable,
        /// dimensionally inconsistent, or any cell cannot be read.
        /// </summary>
        internal static bool TryBuildCurrentGrid(
            byte[] cached,
            int width,
            int height,
            out ushort[] grid)
        {
            grid = Array.Empty<ushort>();
            if (cached == null || cached.Length < 2 || width <= 0 || height <= 0)
                return false;
            if (cached[0] != width || cached[1] != height)
                return false;

            var parsed = new ushort[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!MapEditorTilesetCore.TryReadMar(cached, width, height, x, y, out ushort mar))
                        return false;
                    parsed[y * width + x] = mar;
                }
            }
            grid = parsed;
            return true;
        }

        void ShowError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                CoreState.Services?.ShowError(message);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}