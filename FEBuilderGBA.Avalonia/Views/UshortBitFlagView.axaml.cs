using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UshortBitFlagView : Window, IEditorView
    {
        readonly UshortBitFlagViewModel _vm = new();
        readonly CheckBox[] _bitBoxes;

        public string ViewTitle => "Short Bit Flags";
        public bool IsLoaded => _vm.IsLoaded;

        public UshortBitFlagView()
        {
            InitializeComponent();
            DataContext = _vm;
            _bitBoxes = new CheckBox[]
            {
                Bit0Box, Bit1Box, Bit2Box, Bit3Box, Bit4Box, Bit5Box, Bit6Box, Bit7Box,
                Bit8Box, Bit9Box, Bit10Box, Bit11Box, Bit12Box, Bit13Box, Bit14Box, Bit15Box,
            };
            foreach (var cb in _bitBoxes)
            {
                cb.IsCheckedChanged += OnBitChanged;
            }
            B40.ValueChanged += OnHexLowChanged;
            B41.ValueChanged += OnHexHighChanged;
            _vm.Load(0);
            UpdateUI();
        }

        void OnBitChanged(object? sender, RoutedEventArgs e)
        {
            uint val = 0;
            for (int i = 0; i < 16; i++)
            {
                if (_bitBoxes[i].IsChecked == true)
                    val |= (1u << i);
            }
            _vm.Value = val;
            UpdateHexInputs();
        }

        void OnHexLowChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint low = (uint)(B40.Value ?? 0);
            uint high = (uint)(B41.Value ?? 0);
            _vm.Value = low | (high << 8);
            UpdateCheckboxes();
        }

        void OnHexHighChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint low = (uint)(B40.Value ?? 0);
            uint high = (uint)(B41.Value ?? 0);
            _vm.Value = low | (high << 8);
            UpdateCheckboxes();
        }

        void UpdateCheckboxes()
        {
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged -= OnBitChanged;
            for (int i = 0; i < 16; i++)
                _bitBoxes[i].IsChecked = (_vm.Value & (1u << i)) != 0;
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged += OnBitChanged;
        }

        void UpdateHexInputs()
        {
            B40.ValueChanged -= OnHexLowChanged;
            B41.ValueChanged -= OnHexHighChanged;
            B40.Value = (decimal)(_vm.Value & 0xFF);
            B41.Value = (decimal)((_vm.Value >> 8) & 0xFF);
            B40.ValueChanged += OnHexLowChanged;
            B41.ValueChanged += OnHexHighChanged;
        }

        void UpdateUI()
        {
            UpdateCheckboxes();
            UpdateHexInputs();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            Close(_vm.Value);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { _vm.Load(0); UpdateUI(); }
    }
}
