using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchFilterExView : Window, IEditorView
    {
        readonly PatchFilterExViewModel _vm = new();
        public string ViewTitle => "Patch Filter";
        public bool IsLoaded => _vm.IsLoaded;

        public PatchFilterExView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            Close();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
