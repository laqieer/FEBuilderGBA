using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolView : Window, IEditorView
    {
        readonly PointerToolViewModel _vm = new();
        public string ViewTitle => "Pointer Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AddressInput = AddressTextBox.Text ?? "";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address)
        {
            _vm.AddressInput = $"0x{address:X08}";
            AddressTextBox.Text = _vm.AddressInput;
        }

        public void SelectFirstItem() { }
    }
}
