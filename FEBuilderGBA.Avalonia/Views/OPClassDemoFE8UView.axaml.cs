using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE8UView : Window, IEditorView
    {
        public string ViewTitle => "OP Class Demo (FE8U)";
        public bool IsLoaded => false;

        public OPClassDemoFE8UView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
