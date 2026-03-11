using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTileAnimation2View : Window, IEditorView, IDataVerifiableView
    {
        readonly MapTileAnimation2ViewModel _vm = new();
        readonly UndoService _undoService = new();

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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimation2View.LoadList failed: {0}", ex.Message);
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
                Log.Error("MapTileAnimation2View.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            PaletteDataPointerBox.Text = string.Format("0x{0:X08}", _vm.PaletteDataPointer);
            AnimIntervalBox.Value = _vm.AnimInterval;
            DataCountBox.Value = _vm.DataCount;
            StartPaletteIndexBox.Value = _vm.StartPaletteIndex;
            Unknown7Box.Value = _vm.Unknown7;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
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
                CoreState.Services?.ShowInfo("Tile Animation Type 2 data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapTileAnimation2View.Write: {0}", ex.Message); }
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
