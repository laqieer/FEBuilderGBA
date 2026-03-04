using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorPaletteMissMatchView : Window, IEditorView
    {
        public string ViewTitle => "Palette Mismatch Error";
        public bool IsLoaded => false;

        public ErrorPaletteMissMatchView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
