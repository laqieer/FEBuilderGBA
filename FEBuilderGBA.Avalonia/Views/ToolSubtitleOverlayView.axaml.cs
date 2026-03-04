using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolSubtitleOverlayView : Window, IEditorView
    {
        readonly ToolSubtitleOverlayViewViewModel _vm = new();
        public string ViewTitle => "Subtitle Overlay";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolSubtitleOverlayView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Apply_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
