using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusOptionView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusOptionViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Status Screen Options";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public StatusOptionView()
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
                Log.Error("StatusOptionView.LoadList failed: {0}", ex.Message);
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
                Log.Error("StatusOptionView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            IdTextIdBox.Value = _vm.IdTextId;
            NameTextIdBox.Value = _vm.NameTextId;
            HelpTextIdBox.Value = _vm.HelpTextId;
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            Sel1Box.Value = _vm.SelectionText1;
            Sel2Box.Value = _vm.SelectionText2;
            ColumnsBox.Value = _vm.Columns;
            RowsBox.Value = _vm.Rows;
            DefaultTextIdBox.Value = _vm.DefaultTextId;
            YesTextIdBox.Value = _vm.YesTextId;
            MinValueBox.Value = _vm.MinValue;
            MaxValueBox.Value = _vm.MaxValue;
            OnOff1Box.Value = _vm.OnOffText1;
            OnOff2Box.Value = _vm.OnOffText2;
            DefaultValueBox.Value = _vm.DefaultValue;
            OptionTypeBox.Value = _vm.OptionType;
            IconIdBox.Value = _vm.IconId;
            AsmPointerBox.Text = $"0x{_vm.AsmPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Status Option");
            try
            {
                _vm.IdTextId = (uint)(IdTextIdBox.Value ?? 0);
                _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
                _vm.HelpTextId = (uint)(HelpTextIdBox.Value ?? 0);
                _vm.PosX = (uint)(PosXBox.Value ?? 0);
                _vm.PosY = (uint)(PosYBox.Value ?? 0);
                _vm.SelectionText1 = (uint)(Sel1Box.Value ?? 0);
                _vm.SelectionText2 = (uint)(Sel2Box.Value ?? 0);
                _vm.Columns = (uint)(ColumnsBox.Value ?? 0);
                _vm.Rows = (uint)(RowsBox.Value ?? 0);
                _vm.DefaultTextId = (uint)(DefaultTextIdBox.Value ?? 0);
                _vm.YesTextId = (uint)(YesTextIdBox.Value ?? 0);
                _vm.MinValue = (uint)(MinValueBox.Value ?? 0);
                _vm.MaxValue = (uint)(MaxValueBox.Value ?? 0);
                _vm.OnOffText1 = (uint)(OnOff1Box.Value ?? 0);
                _vm.OnOffText2 = (uint)(OnOff2Box.Value ?? 0);
                _vm.DefaultValue = (uint)(DefaultValueBox.Value ?? 0);
                _vm.OptionType = (uint)(OptionTypeBox.Value ?? 0);
                _vm.IconId = (uint)(IconIdBox.Value ?? 0);
                _vm.AsmPointer = ParseHexText(AsmPointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status option data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("StatusOptionView.Write: {0}", ex.Message); }
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
