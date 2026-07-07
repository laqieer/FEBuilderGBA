using global::Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>MapTileAnimation1Form</c>.
    /// The anime1 PLIST filter (#955) mirrors <c>MapTileAnimation2View</c>: the
    /// top bar's Filter combo enumerates the distinct anime1 PLISTs referenced
    /// by the map settings, and selecting a PLIST drives the entry list off that
    /// PLIST's resolved data table (instead of treating the
    /// <c>map_tileanime1_pointer</c> PLIST table as a flat entry table).
    /// </summary>
    public partial class MapTileAnimation1View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapTileAnimation1ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressFilterChange;
        bool _suppressPaletteChange;

        public string ViewTitle => "Map Tile Animation Type 1";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Tile Animation Type 1", 1238, 866, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapTileAnimation1View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Sample-palette combo: 16 sub-palettes (0..15), like the WF
            // SamplePaletteComboBox. Default selection 0.
            _suppressPaletteChange = true;
            try
            {
                var palItems = new List<string>(16);
                for (int i = 0; i < 16; i++) palItems.Add(i.ToString());
                SamplePaletteComboBox.ItemsSource = palItems;
                SamplePaletteComboBox.SelectedIndex = 0;
            }
            finally { _suppressPaletteChange = false; }
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

        void SamplePalette_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressPaletteChange) return;
            _vm.SelectedPaletteIndex = Math.Max(0, SamplePaletteComboBox.SelectedIndex);
            RefreshPreview();
        }

        /// <summary>Render the current entry's image into the preview control.
        /// Null render → clear surface. Never throws.</summary>
        void RefreshPreview()
        {
            try
            {
                PreviewImage.SetImage(_vm.RenderPreview());
            }
            catch (Exception ex)
            {
                Log.Error($"MapTileAnimation1View.RefreshPreview failed: {ex.Message}");
                PreviewImage.SetImage(null);
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                // Populate the filter combo with the anime1 PLIST list
                // (mirrors WF MakeTileAnimation1).
                var plistRows = _vm.LoadPlistList();
                _suppressFilterChange = true;
                try
                {
                    FilterComboBox.ItemsSource = MakeFilterItems(plistRows);
                    FilterComboBox.SelectedIndex = plistRows.Count > 0 ? 0 : -1;
                }
                finally { _suppressFilterChange = false; }

                // Drive list population from the selected PLIST.
                if (plistRows.Count > 0)
                {
                    SelectPlist(plistRows[0]);
                }
                else
                {
                    EntryList.SetItems(new List<AddrResult>());
                    TopBar.ReadStartAddress = 0;
                    TopBar.ReadCount = 0;
                    ClearDetailPanel();
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTileAnimation1View.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        /// <summary>Reset the right-hand detail panel state. Used by LoadList
        /// (zero PLIST entries), SelectPlist (broken PLIST OR a non-broken
        /// PLIST resolving to an EMPTY entry table, #960). Delegates to the
        /// VM's ClearEntry() so the field-reset + Write-gating lives in one
        /// place.</summary>
        void ClearDetailPanel() => _vm.ClearEntry();

        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        static List<string> MakeFilterItems(List<MapTileAnimation1Core.PlistRow> rows)
        {
            var items = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                items.Add(row.Display);
            }
            return items;
        }

        void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterChange) return;
            int idx = FilterComboBox.SelectedIndex;
            if (idx < 0 || idx >= _vm.PlistRows.Count) return;
            SelectPlist(_vm.PlistRows[idx]);
        }

        void SelectPlist(MapTileAnimation1Core.PlistRow row)
        {
            // Filter selection is a navigation event, NOT a user edit.
            _vm.IsLoading = true;
            try
            {
                _vm.SelectedPlist = row.Plist;
                if (row.IsBroken)
                {
                    EntryList.SetItems(new List<AddrResult>());
                    TopBar.ReadStartAddress = 0;
                    TopBar.ReadCount = 0;
                    ClearDetailPanel();
                    UpdateUI();
                    return;
                }
                var items = _vm.BuildList(row.Addr);
                EntryList.SetItems(items);
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;

                // #960: a non-broken PLIST can still resolve to an EMPTY entry
                // table. SetItems fires no selection on an empty list, so the
                // detail panel would otherwise keep showing the PREVIOUSLY-
                // selected entry. Clear the detail VM state + refresh the UI so
                // the stale entry is gone and Write is gated (same class as the
                // #9 Map Exit stale-detail bug).
                if (items.Count == 0)
                {
                    ClearDetailPanel();
                    UpdateUI();
                }
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
                Log.ErrorF("MapTileAnimation1View.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AnimIntervalBox.Value = _vm.AnimInterval;
            DataCountBox.Value = _vm.DataCount;
            MapTileDataPointerBox.Text = string.Format("0x{0:X08}", _vm.MapTileDataPointer);
            RefreshPreview();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Tile Animation Type 1");
            try
            {
                _vm.AnimInterval = (uint)(AnimIntervalBox.Value ?? 0);
                _vm.DataCount = (uint)(DataCountBox.Value ?? 0);
                _vm.MapTileDataPointer = ParseHexText(MapTileDataPointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Tile Animation Type 1 data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapTileAnimation1View.Write: {0}", ex.Message); }
        }

        // -----------------------------------------------------------------
        // Image Import/Export (#1602). Single PNG Export/Import target the
        // current entry's +4 RAW 4bpp image (the +2 length stays authoritative
        // on import). Export All / Import All round-trip the whole PLIST via a
        // .mapanime1.txt manifest. All ROM mutations run under _undoService with
        // a byte-identical fault restore inside the Core helper.
        // -----------------------------------------------------------------

        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowError("No entry selected.");
                    return;
                }
                var img = _vm.RenderPreview();
                if (img == null)
                {
                    CoreState.Services?.ShowError("Cannot render the tile animation image for this entry.");
                    return;
                }
                var bmp = IconBitmapBuilder.FromImage(img);
                if (bmp == null)
                {
                    CoreState.Services?.ShowError("Failed to build the image bitmap.");
                    return;
                }
                // #1639: write via the SAF bridge so Android content:// targets work.
                string? written = await FileDialogHelper.SaveImageFileVia(TopLevel.GetTopLevel(this) as Window, $"maptileanim1_{_vm.CurrentAddr:X08}.png", p =>
                {
                    using var stream = File.Create(p); bmp.Save(stream);
                });
                if (written == null) return;
                CoreState.Services?.ShowInfo($"Exported to {written}.");
            }
            catch (Exception ex)
            {
                Log.Error($"MapTileAnimation1View.Export_Click failed: {ex.Message}");
                CoreState.Services?.ShowError($"Export failed: {ex.Message}");
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowError("No entry selected.");
                    return;
                }
                string? path = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
                if (string.IsNullOrEmpty(path)) return;

                var img = CoreState.ImageService?.LoadImage(path);
                if (img == null)
                {
                    CoreState.Services?.ShowError($"Failed to load image from: {path}");
                    return;
                }
                try
                {
                    int w = img.Width, h = img.Height;
                    byte[] rgba = img.GetPixelData();
                    _undoService.Begin("Import Tile Animation Type 1 Image");
                    try
                    {
                        string err = _vm.ImportImage(rgba, w, h);
                        if (err != "")
                        {
                            _undoService.Rollback();
                            CoreState.Services?.ShowError(err);
                            return;
                        }
                        _undoService.Commit();
                        _vm.MarkClean();
                        UpdateUI();
                        CoreState.Services?.ShowInfo($"Imported {path}.");
                    }
                    catch (Exception inner)
                    {
                        _undoService.Rollback();
                        Log.Error($"MapTileAnimation1View.Import write failed: {inner.Message}");
                        CoreState.Services?.ShowError($"Import failed: {inner.Message}");
                    }
                }
                finally { img.Dispose(); }
            }
            catch (Exception ex)
            {
                Log.Error($"MapTileAnimation1View.Import_Click failed: {ex.Message}");
            }
        }

        async void ExportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.ReadStartAddress == 0)
                {
                    CoreState.Services?.ShowError("No PLIST selected.");
                    return;
                }
                // Offer BOTH export formats in one dropdown (mirrors WF
                // "マップアニメ1|*.mapanime1.txt|GifAnime|*.gif"): the .mapanime1.txt
                // batch (lossless per-frame PNG round-trip) and the composited-map
                // animated .gif (#1602). The chosen format is inferred from the
                // saved extension.
                // #1639: pick the handle so we can branch by format — the
                // single-file .gif export routes through the SAF bridge, while the
                // .mapanime1.txt batch (which writes sibling PNGs) requires a real
                // local path.
                var file = await FileDialogHelper.SaveFilePick(
                    TopLevel.GetTopLevel(this) as Window, "Export Map Tile Animation Type 1",
                    new[]
                    {
                        ("Map Tile Animation 1", "*.mapanime1.txt"),
                        ("Animated GIF", "*.gif"),
                    },
                    $"maptileanim1_plist{_vm.SelectedPlist:X2}.mapanime1.txt");
                if (file == null) return;

                bool isGif = (file.Name ?? "").EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                string? localPath = file.TryGetLocalPath();
                if (!isGif && string.IsNullOrEmpty(localPath))
                {
                    CoreState.Services?.ShowError(R._("Exporting the .mapanime1.txt batch writes sibling PNG frames and requires desktop file-system access; export as GIF instead, or use a desktop device."));
                    return;
                }

                string err = "";
                string? written;
                if (isGif)
                {
                    written = await FileDialogHelper.WriteViaAsync(file, p => { err = _vm.ExportGif(p); });
                }
                else
                {
                    err = _vm.ExportBatch(localPath, SavePngFile);
                    written = localPath;
                }
                if (written == null) return;
                if (err != "")
                {
                    CoreState.Services?.ShowError(err);
                    return;
                }
                CoreState.Services?.ShowInfo($"Exported to {written}.");
            }
            catch (Exception ex)
            {
                Log.Error($"MapTileAnimation1View.ExportAll_Click failed: {ex.Message}");
                CoreState.Services?.ShowError($"Export failed: {ex.Message}");
            }
        }

        async void ImportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.SelectedPlist == null)
                {
                    CoreState.Services?.ShowError("No PLIST selected.");
                    return;
                }
                // #1639: ImportBatch resolves sibling frame PNGs from the script's
                // own directory, so require a real local path; a SAF pick (no local
                // path) cannot resolve siblings → message on Android, never silent.
                string? path = await FileDialogHelper.OpenFile(
                    TopLevel.GetTopLevel(this) as Window, "Import Map Tile Animation Type 1", "*.mapanime1.txt", requireLocalPath: true);
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Importing a tile-animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                _undoService.Begin("Import All Tile Animation Type 1");
                try
                {
                    string err = _vm.ImportBatch(path, LoadRgbaFile);
                    if (err != "")
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo($"Imported from {path}.");
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error($"MapTileAnimation1View.ImportAll write failed: {inner.Message}");
                    CoreState.Services?.ShowError($"Import failed: {inner.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"MapTileAnimation1View.ImportAll_Click failed: {ex.Message}");
            }
        }

        /// <summary>Write a Core IImage to a PNG file (batch export sink).</summary>
        static void SavePngFile(IImage img, string path)
        {
            var bmp = IconBitmapBuilder.FromImage(img);
            if (bmp == null) return;
            using var stream = File.Create(path);
            bmp.Save(stream);
        }

        /// <summary>Load a PNG/BMP file into RGBA pixels + dims (batch import
        /// source). Returns false on any failure.</summary>
        static bool LoadRgbaFile(string path, out byte[] rgba, out int width, out int height)
        {
            rgba = Array.Empty<byte>();
            width = 0;
            height = 0;
            try
            {
                var img = CoreState.ImageService?.LoadImage(path);
                if (img == null) return false;
                try
                {
                    width = img.Width;
                    height = img.Height;
                    rgba = img.GetPixelData();
                    return rgba != null && width > 0 && height > 0;
                }
                finally { img.Dispose(); }
            }
            catch
            {
                return false;
            }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
