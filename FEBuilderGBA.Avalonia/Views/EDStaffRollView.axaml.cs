using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDStaffRollView : Window, IEditorView, IDataVerifiableView
    {
        readonly EDStaffRollViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Staff Roll Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public EDStaffRollView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadEDStaffRollList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EDStaffRollView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEDStaffRoll(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EDStaffRollView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImagePtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            TSAPtrBox.Text = $"0x{_vm.TSAPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.ImagePointer = ParseHexText(ImagePtrBox.Text);
            _vm.TSAPointer = ParseHexText(TSAPtrBox.Text);
            _undoService.Begin("Edit Staff Roll");
            try
            {
                _vm.WriteEDStaffRoll();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Staff roll data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EDStaffRollView.Write failed: {0}", ex.Message);
            }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
