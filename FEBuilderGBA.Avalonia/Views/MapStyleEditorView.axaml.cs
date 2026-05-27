using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        List<AddrResult> _styleList = new();

        public string ViewTitle => "Map Style Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public MapStyleEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Re-load the palette when either the palette index or the
            // fog flag changes — this keeps the 16-row RGB grid in sync
            // with the user's selection (mirrors WF behavior).
            PaletteCombo.SelectionChanged += (_, _) => ReloadPalette();
            PaletteTypeCombo.SelectionChanged += (_, _) => ReloadPalette();
            MapStyleCombo.SelectionChanged += MapStyle_SelectionChanged;
            Opened += (_, _) => LoadList();
            // Wire ValueChanged handlers on the 48 editable RGB NumericUpDowns
            // so user edits sync into the VM and the swatch updates live.
            // The handler is no-op when _vm.IsLoading is true so programmatic
            // population (LoadEntry / ReloadPalette) does not feed itself.
            WireColorBoxes();
        }

        void WireColorBoxes()
        {
            for (int i = 1; i <= 16; i++)
            {
                int row = i; // capture for closure
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'R', rBox);
                if (gBox != null) gBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'G', gBox);
                if (bBox != null) bBox.ValueChanged += (_, _) => OnColorChannelChanged(row, 'B', bBox);
            }
        }

        void OnColorChannelChanged(int row, char channel, NumericUpDown box)
        {
            // Programmatic load/clear paths set _vm.IsLoading = true to
            // suppress side effects (Copilot v2 non-blocking guidance).
            if (_vm.IsLoading) return;
            // NumericUpDown.Value is decimal? — cast through int before
            // masking to 5 bits to satisfy the 0..0x1F clamp.
            int raw = (int)(box.Value ?? 0m);
            ushort v = (ushort)(raw & 0x1F);
            switch (channel)
            {
                case 'R': _vm.SetColorR(row, v); break;
                case 'G': _vm.SetColorG(row, v); break;
                case 'B': _vm.SetColorB(row, v); break;
            }
            UpdateSwatch(row);
        }

        void LoadList()
        {
            try
            {
                _styleList = _vm.LoadList();
                EntryList.SetItems(_styleList);
                // Mirror EntryList into MapStyleCombo so the top-bar
                // selector is also populated (Copilot bot inline review
                // on MapStyleCombo).
                MapStyleCombo.ItemsSource = _styleList.ConvertAll(r => r.name);
                if (MapStyleCombo.ItemsSource != null && _styleList.Count > 0)
                {
                    MapStyleCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                // Sync the top-bar MapStyleCombo to the same entry without
                // recursing — block the SelectionChanged handler while we
                // assign by toggling _vm.IsLoading.
                int idx = _styleList.FindIndex(r => r.addr == addr);
                if (idx >= 0 && MapStyleCombo.SelectedIndex != idx)
                    MapStyleCombo.SelectedIndex = idx;
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("MapStyleEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        void MapStyle_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int idx = MapStyleCombo.SelectedIndex;
            if (idx < 0 || idx >= _styleList.Count) return;
            // Forward selection to the AddressList so all other UI stays
            // in sync via the existing OnSelected path.
            EntryList.SelectAddress(_styleList[idx].addr);
        }

        void ReloadPalette()
        {
            if (_vm.IsLoading) return;
            int idx = PaletteCombo.SelectedIndex;
            if (idx < 0) idx = 0;
            bool fog = PaletteTypeCombo.SelectedIndex == 1;
            _vm.IsLoading = true;
            try
            {
                // Pass the STABLE base address (PaletteBaseAddress) not
                // the slice (PaletteAddress) -- otherwise the previous
                // slice becomes the new base and the read drifts by
                // idx*0x20 on each selection (Copilot bot v2 inline review).
                bool ok = _vm.LoadPalette(_vm.PaletteBaseAddress, idx, fog);
                if (ok)
                {
                    UpdatePaletteUI();
                    PaletteAddressLabel.Text = $"0x{_vm.PaletteAddress:X08}";
                }
                else
                {
                    // Out-of-bounds / invalid base -- clear stale RGB values
                    // AND the address label so the user doesn't see a wrong
                    // palette OR a stale address (Copilot bot v3 inline review).
                    // ALSO clear VM state so PaletteWrite cannot accidentally
                    // mutate the previous slice (Copilot PR v2 review --
                    // stale-state regression).
                    _vm.ClearPaletteState();
                    ClearPaletteUI();
                    PaletteAddressLabel.Text = "(invalid)";
                }
            }
            finally { _vm.IsLoading = false; }
        }

        void ClearPaletteUI()
        {
            for (int i = 1; i <= 16; i++)
            {
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.Value = 0;
                if (gBox != null) gBox.Value = 0;
                if (bBox != null) bBox.Value = 0;
                // Also clear the swatch so the user does not see a stale
                // preview color after a failed reload.
                var rect = this.FindControl<Rectangle>($"Color{i}_Swatch");
                if (rect != null) rect.Fill = new SolidColorBrush(Colors.Black);
            }
        }

        void UpdateUI()
        {
            // Tab 1 -- Map Style.
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ObjPtrBox.Text = $"0x{_vm.ObjPointer:X08}";
            ConfigPtrLabel.Text = $"0x{_vm.ConfigPointer:X08}";
            ObjAddressLabel.Text = $"0x{_vm.ObjAddress:X08}";
            ObjAddress2Label.Text = _vm.ObjAddress2 != 0 ? $"0x{_vm.ObjAddress2:X08}" : "(none)";
            PaletteAddressLabel.Text = $"0x{_vm.PaletteAddress:X08}";
            TilesetTypeLabel.Text = $"Tileset {_vm.ConfigNo}";

            // Tab 3 -- Chipset.
            ChipsetConfigAddressLabel.Text = $"0x{_vm.ChipsetConfigAddress:X08}";
            ConfigNoLabel.Text = _vm.ConfigNo;

            // Tab 2 -- Palette (populated by LoadEntry's LoadPalette call).
            UpdatePaletteUI();
        }

        void UpdatePaletteUI()
        {
            // Populate the 16 editable RGB rows from the VM's loaded
            // palette state (#660 first slice: NUDs are now editable
            // and the swatch column previews the current color).
            // _vm.IsLoading is set by the caller (OnSelected / ReloadPalette)
            // so the OnColorChannelChanged handler is suppressed during
            // programmatic population (Copilot v2 non-blocking guidance).
            for (int i = 1; i <= 16; i++)
            {
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.Value = _vm.GetColorR(i);
                if (gBox != null) gBox.Value = _vm.GetColorG(i);
                if (bBox != null) bBox.Value = _vm.GetColorB(i);
                UpdateSwatch(i);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ObjPointer = ParseHexText(ObjPtrBox.Text);

            _undoService.Begin("Edit Map Style");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Map style data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Write the in-memory 16-color palette back to ROM at the resolved
        /// PaletteAddress slice. Wraps the VM write in an undo scope so the
        /// 32-byte mutation is undoable atomically. If the VM refuses the
        /// write (no ROM, unresolved address, out-of-bounds), the undo scope
        /// is rolled back and no success message is shown
        /// (Copilot v2 non-blocking guidance).
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.PaletteAddress == 0)
            {
                CoreState.Services.ShowError("Palette address not resolved -- select a map style first.");
                return;
            }

            _undoService.Begin("Edit Map Palette");
            try
            {
                bool ok = _vm.WritePalette();
                if (!ok)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError("Palette write refused (invalid address or out of bounds).");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.PaletteWrite_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Repaint the swatch Rectangle for the given color row from the
        /// VM's current RGB triplet. The 5-5-5 channel value is scaled to
        /// the 0..255 range via `(v &lt;&lt; 3) | (v &gt;&gt; 2)` (a common
        /// GBA-to-PC color expansion that maps 0x1F to 0xFF exactly).
        /// </summary>
        void UpdateSwatch(int row)
        {
            var rect = this.FindControl<Rectangle>($"Color{row}_Swatch");
            if (rect == null) return;
            byte r = Expand5To8(_vm.GetColorR(row));
            byte g = Expand5To8(_vm.GetColorG(row));
            byte b = Expand5To8(_vm.GetColorB(row));
            rect.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        static byte Expand5To8(ushort v5)
        {
            ushort v = (ushort)(v5 & 0x1F);
            return (byte)((v << 3) | (v >> 2));
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
