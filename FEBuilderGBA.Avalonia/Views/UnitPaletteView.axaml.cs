using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitPaletteView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly UnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();
        readonly (TextBox Input, TextBlock HexLabel, string DisplayName)[] _classFields;
        bool _hasLoadedList;

        public string ViewTitle => "Unit Palette Assignment";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unit Palette Assignment", 1443, 857, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public UnitPaletteView()
        {
            InitializeComponent();
            _classFields = new[]
            {
                (TraineeClassBox, TraineeClassHexLabel, "Trainee Class"),
                (BaseClass1Box, BaseClass1HexLabel, "Base Class 1"),
                (BaseClass2Box, BaseClass2HexLabel, "Base Class 2"),
                (AdvancedClass1Box, AdvancedClass1HexLabel, "Promoted Class 1"),
                (AdvancedClass2Box, AdvancedClass2HexLabel, "Promoted Class 2"),
                (AdvancedClass3Box, AdvancedClass3HexLabel, "Promoted Class 3"),
                (AdvancedClass4Box, AdvancedClass4HexLabel, "Promoted Class 4"),
            };
            foreach (var field in _classFields)
            {
                field.Input.PropertyChanged += OnClassValuePropertyChanged;
            }
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitPaletteView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("UnitPaletteView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SetClassValue(_classFields[0], _vm.TraineeClass);
            SetClassValue(_classFields[1], _vm.BaseClass1);
            SetClassValue(_classFields[2], _vm.BaseClass2);
            SetClassValue(_classFields[3], _vm.AdvancedClass1);
            SetClassValue(_classFields[4], _vm.AdvancedClass2);
            SetClassValue(_classFields[5], _vm.AdvancedClass3);
            SetClassValue(_classFields[6], _vm.AdvancedClass4);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            if (!TryReadClassValues(out uint[] values)) return;

            _vm.TraineeClass = values[0];
            _vm.BaseClass1 = values[1];
            _vm.BaseClass2 = values[2];
            _vm.AdvancedClass1 = values[3];
            _vm.AdvancedClass2 = values[4];
            _vm.AdvancedClass3 = values[5];
            _vm.AdvancedClass4 = values[6];

            _undoService.Begin("Edit Unit Palette");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Unit palette data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        void OnClassValuePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != TextBox.TextProperty) return;

            foreach (var field in _classFields)
            {
                if (ReferenceEquals(field.Input, sender))
                {
                    UpdateHexLabel(field);
                    return;
                }
            }
        }

        static void SetClassValue(
            (TextBox Input, TextBlock HexLabel, string DisplayName) field,
            uint value)
        {
            field.Input.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            field.HexLabel.Text = NumericInputParser.FormatHexByte(value);
        }

        static void UpdateHexLabel(
            (TextBox Input, TextBlock HexLabel, string DisplayName) field)
        {
            field.HexLabel.Text = NumericInputParser.TryParseUInt32(
                field.Input.Text,
                0,
                byte.MaxValue,
                out uint value)
                ? NumericInputParser.FormatHexByte(value)
                : R._("Invalid");
        }

        bool TryReadClassValues(out uint[] values)
        {
            values = new uint[_classFields.Length];
            for (int i = 0; i < _classFields.Length; i++)
            {
                var field = _classFields[i];
                if (NumericInputParser.TryParseUInt32(
                    field.Input.Text,
                    0,
                    byte.MaxValue,
                    out values[i]))
                {
                    continue;
                }

                CoreState.Services?.ShowError(R._(
                    "{0} must be 0-255 decimal or 0x00-0xFF hexadecimal.",
                    R._(field.DisplayName)));
                field.Input.Focus();
                return false;
            }

            return true;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
