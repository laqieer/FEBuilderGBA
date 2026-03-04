using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeView : Window, IEditorView
    {
        readonly ToolThreeMargeViewViewModel _vm = new();
        public string ViewTitle => "Three-Way Merge";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolThreeMargeView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Merge_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
