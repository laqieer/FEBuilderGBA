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
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.NewName = NewNameTextBox.Text ?? "";
            Close("OK");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
