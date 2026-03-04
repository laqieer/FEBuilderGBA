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
            ValueLabel.Text = $"Value: {_vm.ValueHex}";
        }

        void UpdateUI()
        {
            for (int i = 0; i < 32; i++)
            {
                _bitBoxes[i].IsChecked = (_vm.Value & (1u << i)) != 0;
            }
            ValueLabel.Text = $"Value: {_vm.ValueHex}";
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            Close(_vm.Value);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { _vm.Load(0); UpdateUI(); }
    }
}
