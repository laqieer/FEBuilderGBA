using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuDefinitionView : Window, IEditorView, IDataVerifiableView
    {
        readonly MenuDefinitionViewModel _vm = new();

        public string ViewTitle => "Menu Definition";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MenuDefinitionView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMenuDefinitionList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuDefinitionView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMenuDefinition(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuDefinitionView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            D4Box.Value = _vm.D4;
            HandlerPtrBox.Text = $"0x{_vm.HandlerPtr:X08}";
            P12Box.Value = _vm.P12;
            P16Box.Value = _vm.P16;
            P20Box.Value = _vm.P20;
            P24Box.Value = _vm.P24;
            P28Box.Value = _vm.P28;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.D4 = (uint)(D4Box.Value ?? 0);
            _vm.HandlerPtr = ParseHexText(HandlerPtrBox.Text);
            _vm.P12 = (uint)(P12Box.Value ?? 0);
            _vm.P16 = (uint)(P16Box.Value ?? 0);
            _vm.P20 = (uint)(P20Box.Value ?? 0);
            _vm.P24 = (uint)(P24Box.Value ?? 0);
            _vm.P28 = (uint)(P28Box.Value ?? 0);
            _vm.WriteMenuDefinition();
            CoreState.Services?.ShowInfo("Menu definition data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
