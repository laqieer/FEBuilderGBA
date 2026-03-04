using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassFontFE8UView : Window, IEditorView
    {
        public string ViewTitle => "OP Class Font (FE8U)";
        public bool IsLoaded => false;

        public OPClassFontFE8UView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
