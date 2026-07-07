using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UbyteBitFlagView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly UbyteBitFlagViewModel _vm = new();
        readonly CheckBox[] _bitBoxes;

        public string ViewTitle => "Byte Bit Flags";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Byte Bit Flags (8-bit)", 708, 420, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public UbyteBitFlagView()
        {
            InitializeComponent();
            DataContext = _vm;
            _bitBoxes = new CheckBox[] { Bit0Box, Bit1Box, Bit2Box, Bit3Box, Bit4Box, Bit5Box, Bit6Box, Bit7Box };
            foreach (var cb in _bitBoxes)
            {
                cb.IsCheckedChanged += OnBitChanged;
            }
            B40.ValueChanged += OnHexChanged;
            _vm.Load(0);
            UpdateUI();
        }

        void OnBitChanged(object? sender, RoutedEventArgs e)
        {
            uint val = 0;
            for (int i = 0; i < 8; i++)
            {
                if (_bitBoxes[i].IsChecked == true)
                    val |= (1u << i);
            }
            _vm.Value = val;
            B40.ValueChanged -= OnHexChanged;
            B40.Value = (decimal)_vm.Value;
            B40.ValueChanged += OnHexChanged;
        }

        void OnHexChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            _vm.Value = (uint)(B40.Value ?? 0);
            UpdateCheckboxes();
        }

        void UpdateCheckboxes()
        {
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged -= OnBitChanged;
            for (int i = 0; i < 8; i++)
                _bitBoxes[i].IsChecked = (_vm.Value & (1u << i)) != 0;
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged += OnBitChanged;
        }

        void UpdateUI()
        {
            UpdateCheckboxes();
            B40.ValueChanged -= OnHexChanged;
            B40.Value = (decimal)_vm.Value;
            B40.ValueChanged += OnHexChanged;
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = _vm.Value; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { _vm.Load(0); UpdateUI(); }
    }
}
