using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeCloseAlertView : Window, IEditorView
    {
        readonly ToolThreeMargeCloseAlertViewModel _vm = new();
        public string ViewTitle => "Close Merge";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolThreeMargeCloseAlertView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            Close();
        }

        void No_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "no";
            Close();
        }

        void Yes_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "yes";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
