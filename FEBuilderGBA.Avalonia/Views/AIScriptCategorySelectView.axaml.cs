using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIScriptCategorySelectView : Window, IEditorView
    {
        public string ViewTitle => "AI Script Category Select";
        public bool IsLoaded => false;

        public AIScriptCategorySelectView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
