using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolBatchInputView : Window, IEditorView
    {
        readonly PointerToolBatchInputViewModel _vm = new();
        public string ViewTitle => "Pointer Tool - Batch Input";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolBatchInputView()
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
