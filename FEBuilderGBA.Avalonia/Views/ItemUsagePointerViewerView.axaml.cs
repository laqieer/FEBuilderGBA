using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemUsagePointerViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemUsagePointerViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Item Usage Pointer";
        public bool IsLoaded => _vm.CanWrite;

        public ItemUsagePointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemUsagePointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemUsagePointerViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemUsagePointer(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemUsagePointerViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UsabilityPointerBox.Text = $"0x{_vm.UsabilityPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UsabilityPointer = ParseHexText(UsabilityPointerBox.Text);

            _undoService.Begin("Edit Item Usage Pointer");
            try
            {
                _vm.WriteItemUsagePointer();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Usage Pointer data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
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
