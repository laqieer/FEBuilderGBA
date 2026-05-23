using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>MapTileAnimation2Form</c>.
    /// Gap-sweep fix (#426) raises the AXAML control surface from 14 to
    /// MEDIUM-verdict density (&gt;=30 of WF's 40 controls), wires the
    /// read-config / filter / selection-bar / palette sub-list /
    /// bulk-export rows, and switches the palette pointer field to
    /// <c>P0</c> (pointer-aware) so writes round-trip through
    /// <c>rom.write_p32</c>. Real Bulk Import / Bulk Export / List
    /// Expansion remain WF-coupled until the Core extraction follow-up
    /// (#524) lands; those buttons render but are <c>IsEnabled=False</c>
    /// with a tooltip referencing the follow-up.
    /// </summary>
    public partial class MapTileAnimation2View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapTileAnimation2ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressRgbChange;
        bool _suppressFilterChange;
        bool _suppressNListChange;

        public string ViewTitle => "Map Tile Animation Type 2 (Palette)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTileAnimation2View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                // Populate the filter combo with the PLIST list mirroring WF
                // MakeTileAnimation2.
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
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimation2View.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        static List<string> MakeFilterItems(List<MapTileAnimation2Core.PlistRow> rows)
        {
            var items = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                items.Add(row.display);
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

        void SelectPlist(MapTileAnimation2Core.PlistRow row)
        {
            _vm.SelectedPlist = row.plist;
            if (row.isBroken)
            {
                EntryList.SetItemsWithIcons(new List<AddrResult>(), _ => null);
                ReadStartAddressBox.Value = 0;
                ReadCountBox.Value = 0;
                return;
            }
            var items = _vm.BuildList(row.addr);
            // ColorSwatchLoader renders a small palette swatch beside each
            // entry, mirroring WF ListBoxEx.DrawColorAndText. The
            // existing ListIconWiringTests.View_UsesColorSwatchLoader test
            // asserts this wiring stays in place.
            EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ColorSwatchLoader(items, i));
            ReadStartAddressBox.Value = _vm.ReadStartAddress;
            ReadCountBox.Value = _vm.ReadCount;
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
                Log.Error("MapTileAnimation2View.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            PaletteDataPointerBox.Text = string.Format("0x{0:X08}", _vm.PaletteDataPointer);
            AnimIntervalBox.Value = _vm.AnimInterval;
            DataCountBox.Value = _vm.DataCount;
            StartPaletteIndexBox.Value = _vm.StartPaletteIndex;
            Unknown7Box.Value = _vm.Unknown7;
            NAddressBox.Value = _vm.NReadStartAddress;
            NBlockSizeBox.Value = _vm.NBlockSize;

            UpdatePaletteSubList();
        }

        void UpdatePaletteSubList()
        {
            _suppressNListChange = true;
            try
            {
                var items = new List<string>(_vm.PaletteRows.Count);
                foreach (var row in _vm.PaletteRows)
                {
                    items.Add(string.Format("0x{0:X2}  GBA=0x{1:X4}  R={2:X2} G={3:X2} B={4:X2}",
                        row.index, row.gba, row.r, row.g, row.b));
                }
                NPaletteListBox.ItemsSource = items;
                int idx = _vm.SelectedPaletteRowIndex;
                NPaletteListBox.SelectedIndex = idx >= 0 && idx < items.Count ? idx : -1;
            }
            finally { _suppressNListChange = false; }

            if (_vm.SelectedPaletteRowIndex >= 0 && _vm.SelectedPaletteRowIndex < _vm.PaletteRows.Count)
            {
                NSelectedAddressLabel.Content = string.Format("0x{0:X08}",
                    _vm.NReadStartAddress + 2 * _vm.SelectedPaletteRowIndex);
            }
            else
            {
                NSelectedAddressLabel.Content = "";
            }

            SyncRgbFromVm();
        }

        void SyncRgbFromVm()
        {
            _suppressRgbChange = true;
            try
            {
                NRBox.Value = _vm.PaletteR;
                NGBox.Value = _vm.PaletteG;
                NBBox.Value = _vm.PaletteB;
                NGbaBox.Value = _vm.PaletteGba;
            }
            finally { _suppressRgbChange = false; }
            ColorPreview.Background = new SolidColorBrush(Color.FromRgb(
                (byte)_vm.PaletteR, (byte)_vm.PaletteG, (byte)_vm.PaletteB));
        }

        void NPaletteList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressNListChange) return;
            int idx = NPaletteListBox.SelectedIndex;
            if (idx < 0 || idx >= _vm.PaletteRows.Count) return;
            _vm.LoadPaletteRow(idx);
            NSelectedAddressLabel.Content = string.Format("0x{0:X08}",
                _vm.NReadStartAddress + 2 * idx);
            SyncRgbFromVm();
        }

        void NRgb_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressRgbChange) return;
            _vm.PaletteR = (uint)(NRBox.Value ?? 0);
            _vm.PaletteG = (uint)(NGBox.Value ?? 0);
            _vm.PaletteB = (uint)(NBBox.Value ?? 0);
            _vm.RecomputeGbaColor();
            _suppressRgbChange = true;
            try { NGbaBox.Value = _vm.PaletteGba; }
            finally { _suppressRgbChange = false; }
            ColorPreview.Background = new SolidColorBrush(Color.FromRgb(
                (byte)_vm.PaletteR, (byte)_vm.PaletteG, (byte)_vm.PaletteB));
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the VM
            // hasn't loaded an entry yet (matches the #506 pattern).
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            uint reloadAddr = _vm.CurrentAddr;
            _undoService.Begin("Edit Tile Animation Type 2");
            try
            {
                _vm.PaletteDataPointer = ParseHexText(PaletteDataPointerBox.Text);
                _vm.AnimInterval = (uint)(AnimIntervalBox.Value ?? 0);
                _vm.DataCount = (uint)(DataCountBox.Value ?? 0);
                _vm.StartPaletteIndex = (uint)(StartPaletteIndexBox.Value ?? 0);
                _vm.Unknown7 = (uint)(Unknown7Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                // Reload the entry so the palette sub-list, N-address bar
                // (NReadStartAddress / NReadCount), color preview, and
                // SelectedAddress label all reflect the new
                // PaletteDataPointer / DataCount instead of the stale
                // pre-write state (Copilot CLI inline review on PR #534).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr);
                    UpdateUI();
                }
                finally { _vm.IsLoading = false; _vm.MarkClean(); }
                CoreState.Services?.ShowInfo("Tile Animation Type 2 data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapTileAnimation2View.Write: {0}", ex.Message); }
        }

        void NWrite_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard mirrors Write_Click so we don't push empty undo
            // entries when no palette row is selected.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            if (_vm.SelectedPaletteRowIndex < 0) return;
            _undoService.Begin("Edit Palette Row");
            try
            {
                _vm.PaletteR = (uint)(NRBox.Value ?? 0);
                _vm.PaletteG = (uint)(NGBox.Value ?? 0);
                _vm.PaletteB = (uint)(NBBox.Value ?? 0);
                if (!_vm.WritePaletteRow())
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                UpdatePaletteSubList();
                CoreState.Services?.ShowInfo("Palette row written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapTileAnimation2View.NWrite: {0}", ex.Message); }
        }

        // Deferred affordances - logged no-ops until follow-up #524 lands.
        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("MapTileAnimation2View.ListExpand_Click invoked - disabled until #524 lands");
        }

        void NListExpand_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("MapTileAnimation2View.NListExpand_Click invoked - disabled until #524 lands");
        }

        void BulkImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("MapTileAnimation2View.BulkImport_Click invoked - disabled until #524 lands");
        }

        void BulkExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("MapTileAnimation2View.BulkExport_Click invoked - disabled until #524 lands");
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
