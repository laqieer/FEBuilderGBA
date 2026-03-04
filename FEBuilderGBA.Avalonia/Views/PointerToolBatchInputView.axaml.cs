using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolBatchInputView : Window, IEditorView
    {
        public string ViewTitle => "Pointer Tool Batch Input";
        public bool IsLoaded => false;

        public PointerToolBatchInputView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
