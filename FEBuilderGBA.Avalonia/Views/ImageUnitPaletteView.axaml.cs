using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitPaletteView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageUnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Per-swatch control references for fast UI updates.
        NumericUpDown[] _rBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _gBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _bBoxes = Array.Empty<NumericUpDown>();
        Border[] _swatches = Array.Empty<Border>();

        public string ViewTitle => "Unit Palette Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageUnitPaletteView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) =>
            {
                CacheSwatchControls();
                LoadList();
            };
        }

        void CacheSwatchControls()
        {
            _rBoxes = new NumericUpDown[16];
            _gBoxes = new NumericUpDown[16];
            _bBoxes = new NumericUpDown[16];
            _swatches = new Border[16];
            for (int i = 0; i < 16; i++)
            {
                int idx = i + 1;
                _rBoxes[i] = this.FindControl<NumericUpDown>($"R{idx}Box")!;
                _gBoxes[i] = this.FindControl<NumericUpDown>($"G{idx}Box")!;
                _bBoxes[i] = this.FindControl<NumericUpDown>($"B{idx}Box")!;
                _swatches[i] = this.FindControl<Border>($"Swatch{idx}")!;
                int captureIdx = i;
                if (_rBoxes[i] != null) _rBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
                if (_gBoxes[i] != null) _gBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
                if (_bBoxes[i] != null) _bBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                ReadStartAddressBox.Text = _vm.LoadListBaseAddress();
                // The VM appends a trailing "Unit Palette Editor" sentinel row
                // (addr=0) at the end; exclude it from the displayed count so
                // the count matches the actual table-row scan (Copilot bot #585
                // off-by-one ask).
                int realCount = items.Count;
                if (realCount > 0 && items[realCount - 1].addr == 0)
                    realCount--;
                ReadCountBox.Text = realCount.ToString();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageUnitPaletteView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SelectedAddressBox.Text = $"0x{_vm.CurrentAddr:X08}";
            IdentNameLabel.Text = _vm.IdentifierName;
            Id0Box.Value = _vm.Id0;
            Id1Box.Value = _vm.Id1;
            Id2Box.Value = _vm.Id2;
            Id3Box.Value = _vm.Id3;
            Id4Box.Value = _vm.Id4;
            Id5Box.Value = _vm.Id5;
            Id6Box.Value = _vm.Id6;
            Id7Box.Value = _vm.Id7;
            Id8Box.Value = _vm.Id8;
            Id9Box.Value = _vm.Id9;
            Id10Box.Value = _vm.Id10;
            Id11Box.Value = _vm.Id11;
            PalettePointerBox.Text = $"0x{_vm.PalettePointer:X08}";
            PaletteAddressBox.Text = $"0x{_vm.PalettePointer:X08}";

            // Push RGB channels from VM to the 16 swatch inputs.
            for (int i = 0; i < 16; i++)
            {
                if (_rBoxes.Length > i && _rBoxes[i] != null) _rBoxes[i].Value = _vm.RChannel[i];
                if (_gBoxes.Length > i && _gBoxes[i] != null) _gBoxes[i].Value = _vm.GChannel[i];
                if (_bBoxes.Length > i && _bBoxes[i] != null) _bBoxes[i].Value = _vm.BChannel[i];
                RefreshSwatch(i);
            }
        }

        /// <summary>Repaint a swatch from the current (R, G, B) NumericUpDown values.</summary>
        void RefreshSwatch(int i)
        {
            if (_swatches.Length <= i || _swatches[i] == null) return;
            uint r = (uint)(_rBoxes[i]?.Value ?? 0);
            uint g = (uint)(_gBoxes[i]?.Value ?? 0);
            uint b = (uint)(_bBoxes[i]?.Value ?? 0);
            // RGB555 -> RGB888 expand-to-8bit (replicate top 3 bits in low nibble)
            byte r8 = (byte)((r << 3) | (r >> 2));
            byte g8 = (byte)((g << 3) | (g >> 2));
            byte b8 = (byte)((b << 3) | (b >> 2));
            _swatches[i].Background = new SolidColorBrush(Color.FromRgb(r8, g8, b8));
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Unit Palette");
            try
            {
                _vm.Id0 = (uint)(Id0Box.Value ?? 0);
                _vm.Id1 = (uint)(Id1Box.Value ?? 0);
                _vm.Id2 = (uint)(Id2Box.Value ?? 0);
                _vm.Id3 = (uint)(Id3Box.Value ?? 0);
                _vm.Id4 = (uint)(Id4Box.Value ?? 0);
                _vm.Id5 = (uint)(Id5Box.Value ?? 0);
                _vm.Id6 = (uint)(Id6Box.Value ?? 0);
                _vm.Id7 = (uint)(Id7Box.Value ?? 0);
                _vm.Id8 = (uint)(Id8Box.Value ?? 0);
                _vm.Id9 = (uint)(Id9Box.Value ?? 0);
                _vm.Id10 = (uint)(Id10Box.Value ?? 0);
                _vm.Id11 = (uint)(Id11Box.Value ?? 0);
                _vm.PalettePointer = ParseHexText(PalettePointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ImageUnitPaletteView.Write: {0}", ex.Message); }
        }

        /// <summary>
        /// Functional palette write-back using <see cref="UnitPaletteWriteCore"/>.
        /// Reads the 16 RGB NumericUpDowns, compresses the new palette via LZ77,
        /// writes in-place when it fits or reallocates at ROM end and patches
        /// the P12 slot under the same undo scope.
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0)
            {
                Log.Error("ImageUnitPaletteView.PaletteWrite: no selected entry.");
                return;
            }
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                r[i] = (uint)(_rBoxes[i]?.Value ?? 0);
                g[i] = (uint)(_gBoxes[i]?.Value ?? 0);
                b[i] = (uint)(_bBoxes[i]?.Value ?? 0);
            }
            int paletteIndex = PaletteTypeCombo.SelectedIndex;
            if (paletteIndex < 0) paletteIndex = 0;
            bool isOverrideAll = PaletteOverrideAllCheck.IsChecked ?? false;
            _undoService.Begin("Write Unit Palette");
            try
            {
                // `undo: null` — the ambient scope opened by `_undoService.Begin`
                // takes care of all ROM writes. Passing the active UndoData
                // through the explicit (addr, value, undo) overloads would
                // double-record every entry (Copilot bot #585 caught this).
                uint newP12 = UnitPaletteWriteCore.WritePalette(
                    CoreState.ROM,
                    _vm.CurrentAddr + 12,
                    r, g, b,
                    paletteIndex,
                    isOverrideAll,
                    undo: null);
                if (newP12 == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    Log.Error("ImageUnitPaletteView.PaletteWrite: WritePalette returned NOT_FOUND (invalid pointer or LZ77 stream).");
                    return;
                }
                _vm.PalettePointer = newP12;
                PalettePointerBox.Text = $"0x{newP12:X08}";
                PaletteAddressBox.Text = $"0x{newP12:X08}";
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImageUnitPaletteView.PaletteWrite: {0}", ex.Message);
            }
        }

        void Reload_Click(object? sender, RoutedEventArgs e) => LoadList();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
