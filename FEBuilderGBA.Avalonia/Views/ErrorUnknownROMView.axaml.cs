using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorUnknownROMView : Window, IEditorView
    {
        readonly ErrorUnknownROMViewModel _vm = new();
        public string ViewTitle => "Unknown ROM Error";
        public bool IsLoaded => _vm.IsLoaded;

        public ErrorUnknownROMView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void FE6_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE6";
            Close();
        }

        void FE7J_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE7J";
            Close();
        }

        void FE7U_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE7U";
            Close();
        }

        void FE8J_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE8J";
            Close();
        }

        void FE8U_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE8U";
            Close();
        }

        void Unknown_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "Unknown";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
