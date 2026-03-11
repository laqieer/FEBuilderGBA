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
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void ExportEvents_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: export events in EA format for selected map
        }

        void ExportWMapEvents_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: export world map events
        }

        void ExportWMapEvents2_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: export world map events (selected)
        }

        void ExportMainTable_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: export main tables (units, classes, items)
        }

        void ExportUndoData_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: export undo buffer data
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
