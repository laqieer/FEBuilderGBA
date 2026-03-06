using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUndoPopupDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly ToolUndoPopupDialogViewModel _vm = new();
        public string ViewTitle => "Undo";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ToolUndoPopupDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "TestPlay";
            Close("TestPlay");
        }

        void RunUndo_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "RunUndo";
            Close("RunUndo");
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "Cancel";
            Close(null);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
