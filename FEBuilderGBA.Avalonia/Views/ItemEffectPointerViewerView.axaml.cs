using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectPointerViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemEffectPointerViewerViewModel _vm = new();

        public string ViewTitle => "Item Effect Pointer";
        public bool IsLoaded => _vm.CanWrite;

        public ItemEffectPointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemEffectPointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectPointerViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemEffectPointer(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectPointerViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EffectPointerBox.Text = $"0x{_vm.EffectPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.EffectPointer = ParseHexText(EffectPointerBox.Text);
            _vm.WriteItemEffectPointer();
            CoreState.Services.ShowInfo("Item Effect Pointer data written.");
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
