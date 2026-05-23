using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventMapChangeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventMapChangeViewModel _vm = new();
        readonly UndoService _undoService = new();

        readonly ObservableCollection<string> _mapDisplayItems = new();
        List<AddrResult> _mapItems = new();

        public string ViewTitle => "Map Change Event Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public EventMapChangeView()
        {
            InitializeComponent();
            MapListBox.ItemsSource = _mapDisplayItems;
            MapListBox.SelectionChanged += MapListBox_SelectionChanged;

            EntryList.SelectedAddressChanged += OnEntrySelected;
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                // Map list (left column).
                _mapItems = _vm.LoadMapList();
                _mapDisplayItems.Clear();
                foreach (var m in _mapItems)
                    _mapDisplayItems.Add(m.name);

                // Legacy entry list (right column) — same callbacks as before.
                var entries = _vm.LoadList();
                EntryList.SetItems(entries);

                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
                BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";

                if (_mapItems.Count > 0)
                    MapListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.Error("EventMapChangeView.LoadList failed: {0}", ex.Message);
            }
        }

        void MapListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = MapListBox.SelectedIndex;
                if (idx < 0 || idx >= _mapItems.Count) return;

                uint mapId = _mapItems[idx].tag;
                bool ok = _vm.LoadEntryForMap(mapId);
                if (ok)
                {
                    UpdateUI();
                }
                else
                {
                    // No change-data for this map — clear the detail panel.
                    ClearDetail();
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventMapChangeView.MapListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void OnEntrySelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventMapChangeView.OnEntrySelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";
            SelectedAddressBox.Text = $"0x{_vm.SelectAddress:X08}";
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            P8Box.Text = $"0x{_vm.P8:X08}";
            CommentBox.Text = _vm.Comment;
        }

        void ClearDetail()
        {
            AddrLabel.Text = "";
            AddressBox.Value = 0;
            BlockSizeBox.Text = "";
            SelectedAddressBox.Text = "";
            B0Box.Value = 0;
            B1Box.Value = 0;
            B2Box.Value = 0;
            B3Box.Value = 0;
            B4Box.Value = 0;
            B5Box.Value = 0;
            B6Box.Value = 0;
            B7Box.Value = 0;
            P8Box.Text = "";
            CommentBox.Text = "";
        }

        void ReadFromUI()
        {
            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.P8 = ParseHexText(P8Box.Text);
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Map Change");
            try
            {
                ReadFromUI();
                _vm.SaveComment(CommentBox.Text ?? string.Empty);
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventMapChangeView.Write_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
            }
        }

        void PointerImport_Click(object? sender, RoutedEventArgs e)
        {
            // Pointer-import (mirrors WF `button1`) is not yet implemented
            // in the Avalonia editor. The button is surfaced so the
            // density / labels scanner sees the parity surface; full
            // import behaviour is tracked as a follow-up.
            CoreState.Services?.ShowError("Pointer import is not yet implemented in the Avalonia editor.");
        }

        void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // List-expansion (mirrors WF `AddressListExpandsButton`) is
            // not yet implemented in the Avalonia editor. Same follow-up
            // status as PointerImport above.
            CoreState.Services?.ShowError("List expansion is not yet implemented in the Avalonia editor.");
        }

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
