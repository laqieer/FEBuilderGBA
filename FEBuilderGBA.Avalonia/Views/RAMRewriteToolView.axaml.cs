using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolView : Window, IEditorView
    {
        public string ViewTitle => "RAM Rewrite Tool";
        public bool IsLoaded => false;

        public RAMRewriteToolView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
