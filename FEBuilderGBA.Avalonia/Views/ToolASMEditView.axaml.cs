using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolASMEditView : Window, IEditorView
    {
        readonly ToolASMEditViewViewModel _vm = new();
        public string ViewTitle => "ASM Edit";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolASMEditView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Compile_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
