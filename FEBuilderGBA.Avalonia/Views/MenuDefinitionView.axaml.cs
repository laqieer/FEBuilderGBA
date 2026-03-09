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
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            WidthBox.Value = _vm.Width;
            HeightBox.Value = _vm.Height;
            StyleDataBox.Text = $"0x{_vm.StyleData:X08}";
            MenuCommandPtrBox.Text = $"0x{_vm.MenuCommandPtr:X08}";
            OnInitBox.Text = $"0x{_vm.OnInitRoutine:X08}";
            OnEndBox.Text = $"0x{_vm.OnEndRoutine:X08}";
            UnknownBox.Text = $"0x{_vm.UnknownRoutine:X08}";
            OnBPressBox.Text = $"0x{_vm.OnBPressRoutine:X08}";
            OnRPressBox.Text = $"0x{_vm.OnRPressRoutine:X08}";
            OnHelpBoxBox.Text = $"0x{_vm.OnHelpBoxRoutine:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.PosX = (uint)(PosXBox.Value ?? 0);
            _vm.PosY = (uint)(PosYBox.Value ?? 0);
            _vm.Width = (uint)(WidthBox.Value ?? 0);
            _vm.Height = (uint)(HeightBox.Value ?? 0);
            _vm.StyleData = ParseHexText(StyleDataBox.Text);
            _vm.MenuCommandPtr = ParseHexText(MenuCommandPtrBox.Text);
            _vm.OnInitRoutine = ParseHexText(OnInitBox.Text);
            _vm.OnEndRoutine = ParseHexText(OnEndBox.Text);
            _vm.UnknownRoutine = ParseHexText(UnknownBox.Text);
            _vm.OnBPressRoutine = ParseHexText(OnBPressBox.Text);
            _vm.OnRPressRoutine = ParseHexText(OnRPressBox.Text);
            _vm.OnHelpBoxRoutine = ParseHexText(OnHelpBoxBox.Text);
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
