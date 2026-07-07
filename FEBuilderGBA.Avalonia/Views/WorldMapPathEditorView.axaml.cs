using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// World Map Road (Path) editor (#1185) — the FE8 interactive road painter.
    /// Left-click/drag paints the selected chip onto the world map; right-click
    /// is the eyedropper; the 5th palette column erases. FE8-only.
    /// </summary>
    public partial class WorldMapPathEditorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly WorldMapPathEditorViewModel _vm = new();
        bool _hasLoadedList;
        readonly UndoService _undo = new();

        // The 8x8-snapped grid the road format works in.
        const int GRID = 8;

        public string ViewTitle => "Path Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Path Editor", 1103, 710, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public WorldMapPathEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            WriteButton.Click += OnWrite;
            // #1458: .road.bin file Save/Load (WF SaveAS/Load parity).
            SaveBinButton.Click += OnSaveBin;
            LoadBinButton.Click += OnLoadBin;
            // TUNNEL (Copilot PR #1228 review #3): GbaImageControl starts a
            // left-drag PAN at zoom>1 and marks the bubbling pointer event
            // Handled, so a bubbling subscription would stop painting once
            // zoomed in. Tunnelling lets the editor see + consume the pointer
            // BEFORE GbaImageControl's drag-pan does.
            MapImageControl.AddHandler(PointerPressedEvent, OnMapPointerPressed,
                RoutingStrategies.Tunnel);
            MapImageControl.AddHandler(PointerMovedEvent, OnMapPointerMoved,
                RoutingStrategies.Tunnel);
            ChipPaletteImage.PointerPressed += OnPalettePointerPressed;

            UpdateSelectedChipLabel();
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

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                WriteButton.IsEnabled = _vm.CanEdit;
                // #1458: Save/Load .road.bin are FE8-only (same gate as Write).
                SaveBinButton.IsEnabled = _vm.CanEdit;
                LoadBinButton.IsEnabled = _vm.CanEdit;
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
        // The chip palette is 5 columns (0..3 flip variants + 4 erase) x 15 rows
        // (the road-strip tile rows). Clamp clicks so an edge click can't select
        // an out-of-grid cell.
        const int PaletteCols = 5;
        const int PaletteRows = 15;

        /// <summary>
        /// Pure mapping of a palette pointer position (in the Stretch=Fill,
        /// PaletteScale-scaled <c>ChipPaletteImage</c>'s DIPs) to a (col,row)
        /// cell, or false when outside the 5x15 grid. Extracted so the
        /// coordinate math is unit-testable without a real PointerEventArgs
        /// (Copilot PR #1228 review — the Stretch fix).
        /// </summary>
        internal static bool TryPaletteCell(double posX, double posY, out int col, out int row)
        {
            col = (int)(posX / PaletteScale) / GRID;
            row = (int)(posY / PaletteScale) / GRID;
            if (posX < 0 || posY < 0 || col < 0 || row < 0
                || col >= PaletteCols || row >= PaletteRows)
            {
                col = 0; row = 0;
                return false;
            }
            return true;
        }

        void OnPalettePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var pos = e.GetPosition(ChipPaletteImage);
                // Stretch=Fill scales the bitmap to the box, so pos is in scaled
                // DIPs; /PaletteScale gives native px, /GRID gives the cell.
                if (!TryPaletteCell(pos.X, pos.Y, out int col, out int row)) return;
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
                    // Eyedropper. Handle the tunnelled event so GbaImageControl
                    // does not also act on it.
                    if (MapPointerToWorld(e, out int wx, out int wy) && _vm.PickChipAt(wx, wy))
                        UpdateSelectedChipLabel();
                    e.Handled = true;
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

        // Paint the selected chip. Marks the tunnelled pointer event Handled when
        // a paint lands so GbaImageControl's left-drag PAN does not also fire at
        // zoom>1 (Copilot PR #1228 review #3).
        void PaintAt(PointerEventArgs e)
        {
            if (!MapPointerToWorld(e, out int wx, out int wy)) return;
            if (_vm.PutPathChip(wx, wy))
                RenderComposite();
            e.Handled = true;
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

        // ===================================================================
        // #1458: .road.bin file Save / Load (WF WorldMapPathEditorForm
        // SaveASbutton_Click / LoadButton_Click parity).
        //
        // Save  — export the RAW packed road stream from ROM at the selected
        //         path's CurrentAddr (getBinaryData(addr, CalcPathDataLength)),
        //         so a non-canonical-but-loadable stream round-trips byte-for-
        //         byte. NOT a re-pack of the live buffer.
        // Load  — decode the file bytes into a NEW chip buffer + re-render +
        //         mark dirty so the user explicitly Writes it (the existing
        //         undo-tracked ROM commit). Load mutates NO ROM — there is no
        //         UndoService scope here (WF Load is a buffer replace too).
        // ===================================================================

        async void OnSaveBin(object? sender, RoutedEventArgs e)
        {
            try
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

                byte[]? bin = _vm.ExportPathBin(out string err);
                if (bin == null)
                {
                    CoreState.Services?.ShowError(string.IsNullOrEmpty(err)
                        ? R._("No road data to export.") : err);
                    return;
                }

                // #1639: single-file BIN export → SAF bridge.
                string? written = await FileDialogHelper.SaveFileVia(
                    TopLevel.GetTopLevel(this) as Window, R._("Save Road Data"), R._("Road.BIN"), "*.road.bin", "road.road.bin",
                    p => File.WriteAllBytes(p, bin));
                if (written == null) return; // cancelled
                StatusLabel.Text = R._("Saved.");
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnSaveBin failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Save failed: {0}", ex.Message));
            }
        }

        async void OnLoadBin(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.CanEdit)
                {
                    CoreState.Services?.ShowInfo(R._("World map roads are FE8-only."));
                    return;
                }

                string? path = await FileDialogHelper.OpenFile(
                    TopLevel.GetTopLevel(this) as Window, R._("Load Road Data"), "*.road.bin");
                if (path == null) return; // cancelled

                byte[] bin = File.ReadAllBytes(path);
                string err = _vm.ImportPathBin(bin);
                if (!string.IsNullOrEmpty(err))
                {
                    CoreState.Services?.ShowError(err);
                    return;
                }

                RenderComposite();
                StatusLabel.Text = R._("Loaded (Write to apply).");
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathEditorView.OnLoadBin failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Load failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
