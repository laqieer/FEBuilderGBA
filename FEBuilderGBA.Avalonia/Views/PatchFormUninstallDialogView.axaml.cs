using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchFormUninstallDialogView : Window, IEditorView
    {
        readonly PatchFormUninstallDialogViewModel _vm = new();
        public string ViewTitle => "Patch Uninstall";
        public bool IsLoaded => _vm.IsLoaded;

        public PatchFormUninstallDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Yes_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserConfirmed = true;
            Close();
        }

        void No_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserConfirmed = false;
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
