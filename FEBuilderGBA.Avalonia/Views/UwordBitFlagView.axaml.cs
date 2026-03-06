using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UwordBitFlagView : Window, IEditorView, IDataVerifiableView
    {
        readonly UwordBitFlagViewModel _vm = new();
        readonly CheckBox[] _bitBoxes;

        public string ViewTitle => "Word Bit Flags";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public UwordBitFlagView()
        {
            InitializeComponent();
            DataContext = _vm;
            _bitBoxes = new CheckBox[]
            {
                Bit0Box, Bit1Box, Bit2Box, Bit3Box, Bit4Box, Bit5Box, Bit6Box, Bit7Box,
                Bit8Box, Bit9Box, Bit10Box, Bit11Box, Bit12Box, Bit13Box, Bit14Box, Bit15Box,
                Bit16Box, Bit17Box, Bit18Box, Bit19Box, Bit20Box, Bit21Box, Bit22Box, Bit23Box,
                Bit24Box, Bit25Box, Bit26Box, Bit27Box, Bit28Box, Bit29Box, Bit30Box, Bit31Box,
            };
            foreach (var cb in _bitBoxes)
            {
                cb.IsCheckedChanged += OnBitChanged;
            }
            B40.ValueChanged += OnHexChanged;
            B41.ValueChanged += OnHexChanged;
            B42.ValueChanged += OnHexChanged;
            B43.ValueChanged += OnHexChanged;
            _vm.Load(0);
            UpdateUI();
        }

        void OnBitChanged(object? sender, RoutedEventArgs e)
        {
            uint val = 0;
            for (int i = 0; i < 32; i++)
            {
                if (_bitBoxes[i].IsChecked == true)
                    val |= (1u << i);
            }
            _vm.Value = val;
            UpdateHexInputs();
        }

        void OnHexChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint b0 = (uint)(B40.Value ?? 0);
            uint b1 = (uint)(B41.Value ?? 0);
            uint b2 = (uint)(B42.Value ?? 0);
            uint b3 = (uint)(B43.Value ?? 0);
            _vm.Value = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
            UpdateCheckboxes();
        }

        void UpdateCheckboxes()
        {
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged -= OnBitChanged;
            for (int i = 0; i < 32; i++)
                _bitBoxes[i].IsChecked = (_vm.Value & (1u << i)) != 0;
            foreach (var cb in _bitBoxes)
                cb.IsCheckedChanged += OnBitChanged;
        }

        void UpdateHexInputs()
        {
            B40.ValueChanged -= OnHexChanged;
            B41.ValueChanged -= OnHexChanged;
            B42.ValueChanged -= OnHexChanged;
            B43.ValueChanged -= OnHexChanged;
            B40.Value = (decimal)(_vm.Value & 0xFF);
            B41.Value = (decimal)((_vm.Value >> 8) & 0xFF);
            B42.Value = (decimal)((_vm.Value >> 16) & 0xFF);
            B43.Value = (decimal)((_vm.Value >> 24) & 0xFF);
            B40.ValueChanged += OnHexChanged;
            B41.ValueChanged += OnHexChanged;
            B42.ValueChanged += OnHexChanged;
            B43.ValueChanged += OnHexChanged;
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
