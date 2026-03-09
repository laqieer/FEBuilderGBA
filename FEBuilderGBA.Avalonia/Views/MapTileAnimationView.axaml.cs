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
            try
            {
                var items = _vm.LoadMapTileAnimationList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimationView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMapTileAnimation(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapTileAnimationView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            W0Box.Value = _vm.W0;
            W2Box.Value = _vm.W2;
            AnimPointerBox.Text = $"0x{_vm.AnimPointer:X08}";
            RawBytesLabel.Text = _vm.RawBytes;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.W0 = (uint)(W0Box.Value ?? 0);
            _vm.W2 = (uint)(W2Box.Value ?? 0);
            _vm.AnimPointer = ParseHexText(AnimPointerBox.Text);
            _vm.WriteMapTileAnimation();
            CoreState.Services?.ShowInfo("Map Tile Animation data written.");
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
