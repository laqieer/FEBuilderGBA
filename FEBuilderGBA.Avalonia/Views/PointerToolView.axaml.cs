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
            _vm.Initialize();
        }

        void Search_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
