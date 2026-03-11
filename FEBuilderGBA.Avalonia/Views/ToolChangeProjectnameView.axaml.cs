using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolChangeProjectnameView : Window, IEditorView
    {
        readonly ToolChangeProjectnameViewViewModel _vm = new();
        public string ViewTitle => "Change Project Name";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolChangeProjectnameView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_vm.NewName))
            {
                _vm.StatusMessage = "Please enter a new project name.";
                return;
            }
            if (_vm.CurrentName == _vm.NewName)
            {
                _vm.StatusMessage = "The new name is the same as the current name.";
                return;
            }
            Close("OK");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
