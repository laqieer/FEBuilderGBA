using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuCommandView : Window, IEditorView, IDataVerifiableView
    {
        readonly MenuCommandViewModel _vm = new();

        public string ViewTitle => "Menu Command";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MenuCommandView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMenuCommandList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMenuCommand(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            P0Box.Text = $"0x{_vm.P0:X08}";
            W4Box.Value = _vm.W4;
            W6Box.Value = _vm.W6;
            D8Box.Text = $"0x{_vm.D8:X08}";
            P12Box.Text = $"0x{_vm.P12:X08}";
            P16Box.Text = $"0x{_vm.P16:X08}";
            P20Box.Text = $"0x{_vm.P20:X08}";
            P24Box.Text = $"0x{_vm.P24:X08}";
            P28Box.Text = $"0x{_vm.P28:X08}";
            P32Box.Text = $"0x{_vm.P32:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.P0 = ParseHexText(P0Box.Text);
            _vm.W4 = (uint)(W4Box.Value ?? 0);
            _vm.W6 = (uint)(W6Box.Value ?? 0);
            _vm.D8 = ParseHexText(D8Box.Text);
            _vm.P12 = ParseHexText(P12Box.Text);
            _vm.P16 = ParseHexText(P16Box.Text);
            _vm.P20 = ParseHexText(P20Box.Text);
            _vm.P24 = ParseHexText(P24Box.Text);
            _vm.P28 = ParseHexText(P28Box.Text);
            _vm.P32 = ParseHexText(P32Box.Text);
            _vm.WriteMenuCommand();
            CoreState.Services?.ShowInfo("Menu command data written.");
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
