using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusParamView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusParamViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Status Parameters";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public StatusParamView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TableFilterCombo.SelectionChanged += TableFilter_Changed;
            Opened += (_, _) => InitFilter();
        }

        void InitFilter()
        {
            var names = _vm.GetTableNames();
            TableFilterCombo.ItemsSource = names;
            if (names.Count > 0)
                TableFilterCombo.SelectedIndex = 0;
            // Always load — SelectionChanged may not fire if Avalonia auto-selects index 0
            LoadList(Math.Max(0, TableFilterCombo.SelectedIndex));
        }

        void TableFilter_Changed(object? sender, SelectionChangedEventArgs e)
        {
            LoadList(TableFilterCombo.SelectedIndex);
        }

        void LoadList(int tableIndex = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadStatusParamList(tableIndex);
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusParamView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusParam(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusParamView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            MenuTextStructBox.Value = _vm.MenuTextStruct;
            BitmapBox.Value = _vm.Bitmap;
            ColorTypeBox.Value = _vm.ColorType;
            IndentBox.Value = _vm.Indent;
            B10Box.Value = _vm.B10;
            B11Box.Value = _vm.B11;
            StringPointerBox.Text = $"0x{_vm.StringPointer:X08}";
            StringLabel.Text = _vm.StringText;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status Parameter");
            try
            {
                _vm.MenuTextStruct = (uint)(MenuTextStructBox.Value ?? 0);
                _vm.Bitmap = (uint)(BitmapBox.Value ?? 0);
                _vm.ColorType = (uint)(ColorTypeBox.Value ?? 0);
                _vm.Indent = (uint)(IndentBox.Value ?? 0);
                _vm.B10 = (uint)(B10Box.Value ?? 0);
                _vm.B11 = (uint)(B11Box.Value ?? 0);
                _vm.StringPointer = ParseHexText(StringPointerBox.Text);
                _vm.WriteStatusParam();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status parameter data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("StatusParamView.Write: {0}", ex.Message); }
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
