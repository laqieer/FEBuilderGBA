using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorPaletteMissMatchView : Window, IEditorView
    {
        readonly ErrorPaletteMissMatchViewModel _vm = new();
        public string ViewTitle => "Palette Mismatch Error";
        public bool IsLoaded => _vm.IsLoaded;

        public ErrorPaletteMissMatchView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
