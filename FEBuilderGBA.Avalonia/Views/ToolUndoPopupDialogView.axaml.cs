using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUndoPopupDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly ToolUndoPopupDialogViewModel _vm = new();
        public string ViewTitle => "Undo";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolUndoPopupDialogView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
