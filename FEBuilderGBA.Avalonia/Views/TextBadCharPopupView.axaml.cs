using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextBadCharPopupView : Window, IEditorView
    {
        public string ViewTitle => "Text Bad Character";
        public bool IsLoaded => false;

        public TextBadCharPopupView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
