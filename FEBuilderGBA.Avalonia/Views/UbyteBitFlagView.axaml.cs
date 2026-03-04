using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UbyteBitFlagView : Window, IEditorView, IDataVerifiableView
    {
        readonly UbyteBitFlagViewModel _vm = new();
        readonly CheckBox[] _bitBoxes;

        public string ViewTitle => "Byte Bit Flags";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public UbyteBitFlagView()
        {
            InitializeComponent();
            _bitBoxes = new CheckBox[] { Bit0Box, Bit1Box, Bit2Box, Bit3Box, Bit4Box, Bit5Box, Bit6Box, Bit7Box };
            foreach (var cb in _bitBoxes)
            {
                cb.IsCheckedChanged += OnBitChanged;
            }
            _vm.Load(0);
            UpdateUI();
        }

        void OnBitChanged(object? sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 8; i++)
            {
                bool isSet = _bitBoxes[i].IsChecked == true;
                if (isSet) _vm.Value |= (1u << i);
                else _vm.Value &= ~(1u << i);
            }
            ValueLabel.Text = $"Value: {_vm.ValueHex}";
        }

        void UpdateUI()
        {
            for (int i = 0; i < 8; i++)
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
