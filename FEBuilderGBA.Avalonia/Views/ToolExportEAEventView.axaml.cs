using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolExportEAEventView : Window, IEditorView
    {
        readonly ToolExportEAEventViewViewModel _vm = new();
        public string ViewTitle => "Export EA Event";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolExportEAEventView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Export_Click(object? sender, RoutedEventArgs e) { }
        void Browse_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
