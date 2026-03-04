using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAnimationCreatorView : Window, IEditorView
    {
        readonly ToolAnimationCreatorViewViewModel _vm = new();
        public string ViewTitle => "Animation Creator";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolAnimationCreatorView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Create_Click(object? sender, RoutedEventArgs e) { }
        void BrowseImage_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
