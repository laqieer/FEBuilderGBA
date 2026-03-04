using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextScriptCategorySelectView : Window, IEditorView
    {
        public string ViewTitle => "Text Script Category Select";
        public bool IsLoaded => false;

        public TextScriptCategorySelectView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
