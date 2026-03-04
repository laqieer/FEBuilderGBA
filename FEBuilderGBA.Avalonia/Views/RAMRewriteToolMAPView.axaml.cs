using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolMAPView : Window, IEditorView
    {
        readonly RAMRewriteToolMAPViewViewModel _vm = new();
        public string ViewTitle => "RAM Rewrite Tool (MAP)";
        public bool IsLoaded => _vm.IsLoaded;

        public RAMRewriteToolMAPView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Write_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
