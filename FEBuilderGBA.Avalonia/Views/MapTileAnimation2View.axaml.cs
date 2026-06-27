using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Platform.Storage;
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

        // Window title shown in the editor dock (matches the AXAML Title
        // attribute so screenshot / UIAutomation tools can locate the view).
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
                else
                {
                    // No PLIST entries - clear the entry list and read-config
                    // bar so a Reload after switching ROMs (or a legitimately
                    // empty result) doesn't leave the old list visible
                    // (Copilot bot review on PR #535).
                    EntryList.SetItemsWithIcons(new List<AddrResult>(), _ => null);
                    // #743: route through the unified EditorTopBarWithInputs (TopBar).
                    TopBar.ReadStartAddress = 0;
                    TopBar.ReadCount = 0;
                    ClearDetailPanel();
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTileAnimation2View.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        /// <summary>
        /// Reset the right-hand detail panel and palette sub-list state.
        /// Used by LoadList (zero PLIST entries) and SelectPlist (broken
        /// PLIST). Mirrors the WF behavior of clearing the entire panel4
        /// when no entry is selectable (Copilot bot review on PR #535).
        /// </summary>
        void ClearDetailPanel()
        {
            _vm.IsLoaded = false;
            _vm.CurrentAddr = 0;
            _vm.SelectedAddress = 0;
            _vm.PaletteDataPointer = 0;
            _vm.AnimInterval = 0;
            _vm.DataCount = 0;
            _vm.StartPaletteIndex = 0;
            _vm.Unknown7 = 0;
            _vm.PaletteRows = new List<MapTileAnimation2Core.PaletteRow>();
            _vm.SelectedPaletteRowIndex = -1;
            _vm.NReadStartAddress = 0;
            _vm.NReadCount = 0;
            _vm.PaletteR = 0;
            _vm.PaletteG = 0;
            _vm.PaletteB = 0;
            _vm.PaletteGba = 0;
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        static List<string> MakeFilterItems(List<MapTileAnimation2Core.PlistRow> rows)
        {
            var items = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                // Display strings are constructed in MapTileAnimation2Core
                // via string.Format so they could be localized in a future
                // pass; for now they ship in English (the AXAML literal is
                // covered by the L10n scanner via the en.txt reverse map).
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

        void SelectPlist(MapTileAnimation2Core.PlistRow row)
        {
            // Filter selection is a navigation event, NOT a user edit.
            // Suppress dirty marking via _vm.IsLoading so changing the
            // filter combo does not mark the editor as dirty (Copilot CLI
            // inline review on PR #534).
            _vm.IsLoading = true;
            try
            {
                _vm.SelectedPlist = row.Plist;
                if (row.IsBroken)
                {
                    EntryList.SetItemsWithIcons(new List<AddrResult>(), _ => null);
                    // #743: route through the unified EditorTopBarWithInputs (TopBar).
                    TopBar.ReadStartAddress = 0;
                    TopBar.ReadCount = 0;
                    // Reset the right-hand detail panel - including the
                    // main entry fields (PaletteDataPointer / AnimInterval
                    // / DataCount / StartPaletteIndex / Unknown7) - so a
                    // broken PLIST doesn't display the previously-selected
                    // entry's data (Copilot bot review on PR #535).
                    ClearDetailPanel();
                    UpdateUI();
                    return;
                }
                var items = _vm.BuildList(row.Addr);
                // ColorSwatchLoader renders a small palette swatch beside
                // each entry, mirroring WF ListBoxEx.DrawColorAndText. The
                // existing ListIconWiringTests.View_UsesColorSwatchLoader
                // test asserts this wiring stays in place.
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ColorSwatchLoader(items, i));
                // #743: route through the unified EditorTopBarWithInputs (TopBar).
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
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
                Log.ErrorF("MapTileAnimation2View.OnSelected failed: {0}", ex.Message);
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
                        row.Index, row.Gba, row.R, row.G, row.B));
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
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapTileAnimation2View.Write: {0}", ex.Message); }
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
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapTileAnimation2View.NWrite: {0}", ex.Message); }
        }

        // -----------------------------------------------------------------
        // #524 Core-extracted affordances: BulkImport / BulkExport /
        // ExpandEntryList / ExpandPaletteRowList. Each handler wraps in
        // _undoService.Begin/Commit/Rollback so ROM mutations record into
        // the ambient undo scope (Core helpers use the no-undo
        // RecycleAddress ambient overloads to avoid double-recording).
        // -----------------------------------------------------------------

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.ReadStartAddress == 0) return;
            uint slot = _vm.GetPlistTableSlot();
            if (slot == 0)
            {
                CoreState.Services?.ShowError("Cannot resolve PLIST table slot.");
                return;
            }
            _undoService.Begin("Expand Tile Animation Type 2 Entry List");
            try
            {
                uint newBase = _vm.ExpandEntryListByOne(slot);
                if (newBase == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Failed to expand entry list.");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                // Reload to surface the new row.
                LoadList();
                CoreState.Services?.ShowInfo($"Entry list expanded. New base: 0x{newBase:X08}");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MapTileAnimation2View.ListExpand_Click: {0}", ex.Message);
            }
        }

        void NListExpand_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            if (_vm.PaletteDataPointer == 0)
            {
                CoreState.Services?.ShowError("No palette data pointer set on this entry.");
                return;
            }
            uint reloadAddr = _vm.CurrentAddr;
            _undoService.Begin("Expand Tile Animation Type 2 Palette Sub-List");
            try
            {
                uint newBase = _vm.ExpandPaletteRowListByOne();
                if (newBase == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Failed to expand palette sub-list.");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                // Reload the entry so the palette sub-list reflects the new
                // base pointer + grown row count.
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr);
                    UpdateUI();
                }
                finally { _vm.IsLoading = false; _vm.MarkClean(); }
                CoreState.Services?.ShowInfo($"Palette sub-list expanded. New base: 0x{newBase:X08}");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MapTileAnimation2View.NListExpand_Click: {0}", ex.Message);
            }
        }

        async void BulkImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.ReadStartAddress == 0) return;
            uint slot = _vm.GetPlistTableSlot();
            if (slot == 0)
            {
                CoreState.Services?.ShowError("Cannot resolve PLIST table slot.");
                return;
            }
            try
            {
                var txtType = new FilePickerFileType("Map Tile Animation 2 Files")
                { Patterns = new[] { "*.mapanime2.txt", "*.txt" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Map Tile Animation Type 2",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { txtType, allType },
                });
                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                _undoService.Begin("Bulk Import Tile Animation Type 2");
                try
                {
                    string err = _vm.BulkImport(path, slot);
                    if (err != "")
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    // Reload so the filter combo / entry list / palette sub-
                    // list reflect the newly-imported data.
                    LoadList();
                    CoreState.Services?.ShowInfo($"Imported from {path}.");
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("MapTileAnimation2View.BulkImport: {0}", inner.Message);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTileAnimation2View.BulkImport_Click: {0}", ex.Message);
            }
        }

        async void BulkExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.ReadStartAddress == 0) return;
            try
            {
                var txtType = new FilePickerFileType("Map Tile Animation 2 Files")
                { Patterns = new[] { "*.mapanime2.txt", "*.txt" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Map Tile Animation Type 2",
                    SuggestedFileName =
                        $"maptileanim2_plist{_vm.SelectedPlist:X2}.mapanime2.txt",
                    FileTypeChoices = new[] { txtType, allType },
                });
                string? path = file?.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                string err = _vm.BulkExport(path);
                if (err != "")
                {
                    CoreState.Services?.ShowError(err);
                    return;
                }
                CoreState.Services?.ShowInfo($"Exported to {path}.");
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTileAnimation2View.BulkExport_Click: {0}", ex.Message);
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
