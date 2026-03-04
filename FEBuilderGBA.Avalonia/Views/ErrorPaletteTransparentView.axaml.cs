using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorPaletteTransparentView : Window, IEditorView
    {
        public string ViewTitle => "Palette Transparent Error";
        public bool IsLoaded => false;

        public ErrorPaletteTransparentView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
