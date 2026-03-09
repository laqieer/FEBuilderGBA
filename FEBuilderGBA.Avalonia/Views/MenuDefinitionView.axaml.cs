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
            D4Box.Text = $"0x{_vm.D4:X08}";
            HandlerPtrBox.Text = $"0x{_vm.HandlerPtr:X08}";
            P12Box.Text = $"0x{_vm.P12:X08}";
            P16Box.Text = $"0x{_vm.P16:X08}";
            P20Box.Text = $"0x{_vm.P20:X08}";
            P24Box.Text = $"0x{_vm.P24:X08}";
            P28Box.Text = $"0x{_vm.P28:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.D4 = ParseHexText(D4Box.Text);
            _vm.HandlerPtr = ParseHexText(HandlerPtrBox.Text);
            _vm.P12 = ParseHexText(P12Box.Text);
            _vm.P16 = ParseHexText(P16Box.Text);
            _vm.P20 = ParseHexText(P20Box.Text);
            _vm.P24 = ParseHexText(P24Box.Text);
            _vm.P28 = ParseHexText(P28Box.Text);
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
