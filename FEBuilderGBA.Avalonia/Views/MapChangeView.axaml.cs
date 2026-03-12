using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapChangeView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapChangeViewModel _vm = new();
        readonly UndoService _undoService = new();
        readonly ObservableCollection<string> _recordDisplayItems = new();
        List<ChangeRecord> _records = new();

        public string ViewTitle => "Map Change Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapChangeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            RecordList.ItemsSource = _recordDisplayItems;
            RecordList.SelectionChanged += OnRecordSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMapChangeList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapChangeView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMapChange(addr);
                UpdateUI();
                LoadRecords();
            }
            catch (Exception ex)
            {
                Log.Error("MapChangeView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ChangePointerBox.Text = $"0x{_vm.ChangePointer:X08}";
        }

        void LoadRecords()
        {
            _recordDisplayItems.Clear();
            _records = _vm.LoadChangeRecords();
            foreach (var rec in _records)
            {
                _recordDisplayItems.Add(rec.DisplayName);
            }
            ClearRecordFields();
        }

        void ClearRecordFields()
        {
            RecAddrLabel.Text = "";
            RecChangeIDBox.Text = "";
            RecXBox.Text = "";
            RecYBox.Text = "";
            RecWidthBox.Text = "";
            RecHeightBox.Text = "";
            RecTileDataPtrBox.Text = "";
            _vm.CanWriteRecord = false;
        }

        void OnRecordSelected(object? sender, SelectionChangedEventArgs e)
        {
            int idx = RecordList.SelectedIndex;
            if (idx < 0 || idx >= _records.Count)
            {
                ClearRecordFields();
                return;
            }

            _vm.IsLoading = true;
            try
            {
                var record = _records[idx];
                _vm.LoadRecord(record);
                UpdateRecordUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapChangeView.OnRecordSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateRecordUI()
        {
            RecAddrLabel.Text = $"0x{_vm.SelectedRecordAddr:X08}";
            RecChangeIDBox.Text = $"0x{_vm.RecChangeID:X02}";
            RecXBox.Text = _vm.RecX.ToString();
            RecYBox.Text = _vm.RecY.ToString();
            RecWidthBox.Text = _vm.RecWidth.ToString();
            RecHeightBox.Text = _vm.RecHeight.ToString();
            RecTileDataPtrBox.Text = $"0x{_vm.RecTileDataPtr:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Change Pointer");
            try
            {
                _vm.ChangePointer = ParseHexText(ChangePointerBox.Text);
                _vm.WriteMapChange();
                _undoService.Commit();
                _vm.MarkClean();
                LoadRecords();
                CoreState.Services?.ShowInfo("Map Change pointer written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapChangeView.Write: {0}", ex.Message); }
        }

        void WriteRecord_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWriteRecord) return;
            _undoService.Begin("Edit Map Change Record");
            try
            {
                _vm.RecChangeID = ParseHexByte(RecChangeIDBox.Text);
                _vm.RecX = ParseDecimalByte(RecXBox.Text);
                _vm.RecY = ParseDecimalByte(RecYBox.Text);
                _vm.RecWidth = ParseDecimalByte(RecWidthBox.Text);
                _vm.RecHeight = ParseDecimalByte(RecHeightBox.Text);
                _vm.RecTileDataPtr = ParseHexText(RecTileDataPtrBox.Text);
                _vm.WriteChangeRecord();
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh the record list to show updated display names
                int selectedIdx = RecordList.SelectedIndex;
                LoadRecords();
                if (selectedIdx >= 0 && selectedIdx < _records.Count)
                    RecordList.SelectedIndex = selectedIdx;

                CoreState.Services?.ShowInfo("Change record written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapChangeView.WriteRecord: {0}", ex.Message); }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        static byte ParseHexByte(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte v) ? v : (byte)0;
        }

        static byte ParseDecimalByte(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            // Allow hex prefix for convenience
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
                return byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte hv) ? hv : (byte)0;
            }
            return byte.TryParse(text, out byte v) ? v : (byte)0;
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
