using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAutomaticRecoveryROMHeaderView : Window, IEditorView
    {
        readonly ToolAutomaticRecoveryROMHeaderViewViewModel _vm = new();
        public string ViewTitle => "Automatic Recovery ROM Header";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolAutomaticRecoveryROMHeaderView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void SelectFile_Click(object? sender, RoutedEventArgs e)
        {
        }

        void Recover_Click(object? sender, RoutedEventArgs e)
        {
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
