using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapPointerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Map Pointer Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapPointerView()
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
                var items = _vm.LoadMapPointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMapPointer(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            MapDataPointerBox.Text = $"0x{_vm.MapDataPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Pointer");
            try
            {
                _vm.MapDataPointer = ParseHexText(MapDataPointerBox.Text);
                _vm.WriteMapPointer();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Pointer data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapPointerView.Write: {0}", ex.Message); }
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
