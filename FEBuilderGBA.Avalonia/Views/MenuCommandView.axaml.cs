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
            JpNamePtrBox.Text = $"0x{_vm.JpNamePointer:X08}";
            NameTextIdBox.Value = _vm.NameTextId;
            HelpTextIdBox.Value = _vm.HelpTextId;
            ColorIdBox.Text = $"0x{_vm.ColorAndIdDword:X08}";
            UsabilityBox.Text = $"0x{_vm.UsabilityRoutine:X08}";
            DrawBox.Text = $"0x{_vm.DrawRoutine:X08}";
            EffectBox.Text = $"0x{_vm.EffectRoutine:X08}";
            PerTurnBox.Text = $"0x{_vm.PerTurnCallback:X08}";
            CursorSelBox.Text = $"0x{_vm.CursorSelectAction:X08}";
            CancelBox.Text = $"0x{_vm.CancelAction:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.JpNamePointer = ParseHexText(JpNamePtrBox.Text);
            _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
            _vm.HelpTextId = (uint)(HelpTextIdBox.Value ?? 0);
            _vm.ColorAndIdDword = ParseHexText(ColorIdBox.Text);
            _vm.UsabilityRoutine = ParseHexText(UsabilityBox.Text);
            _vm.DrawRoutine = ParseHexText(DrawBox.Text);
            _vm.EffectRoutine = ParseHexText(EffectBox.Text);
            _vm.PerTurnCallback = ParseHexText(PerTurnBox.Text);
            _vm.CursorSelectAction = ParseHexText(CursorSelBox.Text);
            _vm.CancelAction = ParseHexText(CancelBox.Text);
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
