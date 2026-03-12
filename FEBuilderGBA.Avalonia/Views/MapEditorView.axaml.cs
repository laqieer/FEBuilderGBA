using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorView : Window, IEditorView
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

        void OnMapImageClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (_vm.MapWidth <= 0 || _vm.MapHeight <= 0) return;

                // Get click position relative to the image control
                var pos = e.GetPosition(MapImageControl);

                // Convert pixel position to tile coordinates (16x16 tiles, accounting for zoom)
                int tileX = (int)(pos.X / (16 * _zoom));
                int tileY = (int)(pos.Y / (16 * _zoom));

                if (_vm.SelectTile(tileX, tileY))
                {
                    TileIdTextBox.Text = _vm.SelectedTileId.ToString("X4");
                }
                UpdateTileUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapEditorView.OnMapImageClick failed: {0}", ex.Message);
            }
        }

        void OnWriteTile(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasTileSelected) return;

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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
