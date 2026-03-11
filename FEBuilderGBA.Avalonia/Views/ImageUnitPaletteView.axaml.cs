using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitPaletteView : Window, IEditorView
    {
        readonly ImageUnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Unit Palette Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageUnitPaletteView()
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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            IdentNameLabel.Text = _vm.IdentifierName;
            Id0Box.Value = _vm.Id0;
            Id1Box.Value = _vm.Id1;
            Id2Box.Value = _vm.Id2;
            Id3Box.Value = _vm.Id3;
            Id4Box.Value = _vm.Id4;
            Id5Box.Value = _vm.Id5;
            Id6Box.Value = _vm.Id6;
            Id7Box.Value = _vm.Id7;
            Id8Box.Value = _vm.Id8;
            Id9Box.Value = _vm.Id9;
            Id10Box.Value = _vm.Id10;
            Id11Box.Value = _vm.Id11;
            PalettePointerBox.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Unit Palette");
            try
            {
                _vm.Id0 = (uint)(Id0Box.Value ?? 0);
                _vm.Id1 = (uint)(Id1Box.Value ?? 0);
                _vm.Id2 = (uint)(Id2Box.Value ?? 0);
                _vm.Id3 = (uint)(Id3Box.Value ?? 0);
                _vm.Id4 = (uint)(Id4Box.Value ?? 0);
                _vm.Id5 = (uint)(Id5Box.Value ?? 0);
                _vm.Id6 = (uint)(Id6Box.Value ?? 0);
                _vm.Id7 = (uint)(Id7Box.Value ?? 0);
                _vm.Id8 = (uint)(Id8Box.Value ?? 0);
                _vm.Id9 = (uint)(Id9Box.Value ?? 0);
                _vm.Id10 = (uint)(Id10Box.Value ?? 0);
                _vm.Id11 = (uint)(Id11Box.Value ?? 0);
                _vm.PalettePointer = ParseHexText(PalettePointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ImageUnitPaletteView.Write: {0}", ex.Message); }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
