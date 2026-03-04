using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolMAPView : Window, IEditorView
    {
        public string ViewTitle => "RAM Rewrite Tool (MAP)";
        public bool IsLoaded => false;

        public RAMRewriteToolMAPView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
