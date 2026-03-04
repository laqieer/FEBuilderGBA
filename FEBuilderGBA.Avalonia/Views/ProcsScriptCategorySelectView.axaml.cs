using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ProcsScriptCategorySelectView : Window, IEditorView
    {
        public string ViewTitle => "Procs Script Category Select";
        public bool IsLoaded => false;

        public ProcsScriptCategorySelectView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
