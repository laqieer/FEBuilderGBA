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
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e) => Close("OK");
        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
