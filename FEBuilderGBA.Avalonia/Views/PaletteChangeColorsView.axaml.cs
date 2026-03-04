using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteChangeColorsView : Window, IEditorView
    {
        public string ViewTitle => "Palette Change Colors";
        public bool IsLoaded => false;

        public PaletteChangeColorsView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
