using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolView : Window, IEditorView
    {
        readonly RAMRewriteToolViewViewModel _vm = new();
        public string ViewTitle => "RAM Rewrite Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public RAMRewriteToolView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
