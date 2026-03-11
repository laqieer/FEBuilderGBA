using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTileAnimationView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapTileAnimationViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Map Tile Animation Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapTileAnimationView()
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
                var items = _vm.LoadMapTileAnimationList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimationView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMapTileAnimation(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimationView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AnimIntervalBox.Value = _vm.AnimInterval;
            DataCountBox.Value = _vm.DataCount;
            AnimPointerBox.Text = $"0x{_vm.AnimPointer:X08}";
            RawBytesLabel.Text = _vm.RawBytes;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Tile Animation");
            try
            {
                _vm.AnimInterval = (uint)(AnimIntervalBox.Value ?? 0);
                _vm.DataCount = (uint)(DataCountBox.Value ?? 0);
                _vm.AnimPointer = ParseHexText(AnimPointerBox.Text);
                _vm.WriteMapTileAnimation();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Tile Animation data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapTileAnimationView.Write: {0}", ex.Message); }
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

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
