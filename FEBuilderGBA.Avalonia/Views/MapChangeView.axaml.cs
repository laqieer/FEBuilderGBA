using System;
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

        public string ViewTitle => "Map Change Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapChangeView()
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

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Change");
            try
            {
                _vm.ChangePointer = ParseHexText(ChangePointerBox.Text);
                _vm.WriteMapChange();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Change data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MapChangeView.Write: {0}", ex.Message); }
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
