using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// World Map Road (Path) editor (#1185) — the FE8 interactive road painter.
    /// Left-click/drag paints the selected chip onto the world map; right-click
    /// is the eyedropper; the 5th palette column erases. FE8-only.
    /// </summary>
    public partial class WorldMapPathEditorView : TranslatedWindow, IEditorView
    {
        readonly WorldMapPathEditorViewModel _vm = new();
        readonly UndoService _undo = new();

        // The 8x8-snapped grid the road format works in.
        const int GRID = 8;

        public string ViewTitle => "Path Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapPathEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            WriteButton.Click += OnWrite;
            MapImageControl.PointerPressed += OnMapPointerPressed;
            MapImageControl.PointerMoved += OnMapPointerMoved;
            ChipPaletteImage.PointerPressed += OnPalettePointerPressed;

            UpdateSelectedChipLabel();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                WriteButton.IsEnabled = _vm.CanEdit;
                if (!_vm.CanEdit)
                {
                    StatusLabel.Text = R._("World map roads are FE8-only.");
                }
                RenderChipPalette();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.LoadList failed: " + ex.ToString());
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                var item = EntryList.SelectedItem;
                if (item == null) return;
                // The path id is carried in the AddrResult tag (stable under
                // list filtering — NOT the display index).
                _vm.LoadEntry((int)item.tag);
                UpdateUI();
                RenderComposite();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = "0x" + U.ToHexString8(_vm.CurrentAddr);
            StatusLabel.Text = "";
        }

        void RenderComposite()
        {
            try
            {
                using IImage? img = _vm.RenderComposite();
                MapImageControl.SetImage(img);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.RenderComposite failed: " + ex.ToString());
            }
        }

        void RenderChipPalette()
        {
            try
            {
                using IImage? img = _vm.RenderChipPalette(out _);
                if (img == null)
                {
                    ChipPaletteImage.Source = null;
                    return;
                }
                var bmp = IconBitmapBuilder.FromImage(img);
                if (bmp == null)
                {
                    ChipPaletteImage.Source = null;
                    return;
                }
                ChipPaletteImage.Source = bmp;
                // Scale x4 so the 40x120 palette is comfortably clickable; the
                // click handler divides the display coords back by the scale.
                ChipPaletteImage.Width = bmp.PixelSize.Width * PaletteScale;
                ChipPaletteImage.Height = bmp.PixelSize.Height * PaletteScale;
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.RenderChipPalette failed: " + ex.ToString());
            }
        }

        const int PaletteScale = 4;

        void OnPalettePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var pos = e.GetPosition(ChipPaletteImage);
                int col = (int)(pos.X / PaletteScale) / GRID;
                int row = (int)(pos.Y / PaletteScale) / GRID;
                if (col < 0 || row < 0) return;
                _vm.SelectedChipCol = col;
                _vm.SelectedChipRow = row;
                UpdateSelectedChipLabel();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnPalettePointerPressed failed: " + ex.ToString());
            }
        }

        void UpdateSelectedChipLabel()
        {
            SelectedChipLabel.Text = R._("Selected: col {0}, row {1}",
                _vm.SelectedChipCol, _vm.SelectedChipRow);
        }

        void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_vm.CanEdit) return;
            try
            {
                var props = e.GetCurrentPoint(MapImageControl).Properties;
                if (props.IsRightButtonPressed)
                {
                    // Eyedropper.
                    if (MapPointerToWorld(e, out int wx, out int wy) && _vm.PickChipAt(wx, wy))
                        UpdateSelectedChipLabel();
                    return;
                }
                if (props.IsLeftButtonPressed)
                    PaintAt(e);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnMapPointerPressed failed: " + ex.ToString());
            }
        }

        void OnMapPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_vm.CanEdit) return;
            try
            {
                var props = e.GetCurrentPoint(MapImageControl).Properties;
                if (props.IsLeftButtonPressed)
                    PaintAt(e);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnMapPointerMoved failed: " + ex.ToString());
            }
        }

        void PaintAt(PointerEventArgs e)
        {
            if (!MapPointerToWorld(e, out int wx, out int wy)) return;
            if (_vm.PutPathChip(wx, wy))
                RenderComposite();
        }

        /// <summary>
        /// Convert a pointer event into 8x8-snapped world pixel coordinates,
        /// via the GbaImageControl's zoom/scroll-aware source-pixel helper (#658).
        /// </summary>
        bool MapPointerToWorld(PointerEventArgs e, out int worldX, out int worldY)
        {
            worldX = 0;
            worldY = 0;
            if (!MapImageControl.TryGetSourcePixel(e, out int srcX, out int srcY)) return false;
            worldX = (srcX / GRID) * GRID;
            worldY = (srcY / GRID) * GRID;
            return true;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanEdit)
            {
                CoreState.Services?.ShowInfo(R._("World map roads are FE8-only."));
                return;
            }
            if (_vm.CurrentPathId < 0)
            {
                CoreState.Services?.ShowInfo(R._("No path selected."));
                return;
            }

            _undo.Begin("WorldMapPath.Write");
            try
            {
                string err = _vm.WritePath();
                if (!string.IsNullOrEmpty(err))
                {
                    _undo.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undo.Commit();
                // Reload so the address label reflects the freshly-written pointer.
                int pathId = _vm.CurrentPathId;
                _vm.LoadEntry(pathId);
                UpdateUI();
                RenderComposite();
                StatusLabel.Text = R._("Saved.");
            }
            catch (Exception ex)
            {
                _undo.Rollback();
                Log.Error("WorldMapPathEditorView.OnWrite failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Write failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
