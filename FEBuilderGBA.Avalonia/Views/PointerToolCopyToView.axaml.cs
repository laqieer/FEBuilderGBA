using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolCopyToView : Window, IEditorView
    {
        public string ViewTitle => "Pointer Tool Copy To";
        public bool IsLoaded => false;

        public PointerToolCopyToView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
