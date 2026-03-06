using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupport_UpdateQuestionDialogView : Window, IEditorView
    {
        readonly ToolWorkSupport_UpdateQuestionDialogViewModel _vm = new();
        public string ViewTitle => "Current version is the latest";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupport_UpdateQuestionDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void ForceUpdate_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "retry";
            Close();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
