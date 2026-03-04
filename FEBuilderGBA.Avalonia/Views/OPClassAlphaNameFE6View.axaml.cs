using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassAlphaNameFE6View : Window, IEditorView
    {
        public string ViewTitle => "OP Class Alpha Name (FE6)";
        public bool IsLoaded => false;

        public OPClassAlphaNameFE6View()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
