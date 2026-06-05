using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
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
    public partial class MapTileAnimation1View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapTileAnimation1ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressFilterChange;

        public string ViewTitle => "Map Tile Animation Type 1";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTileAnimation1View()
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
                Log.Error("MapTileAnimation1View.LoadList failed: {0}", ex.Message);
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
                Log.Error("MapTileAnimation1View.OnSelected failed: {0}", ex.Message);
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
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapTileAnimation1View.Write: {0}", ex.Message); }
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
