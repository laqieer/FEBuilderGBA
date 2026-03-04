using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorImportImageOptionView : Window, IEditorView
    {
        public string ViewTitle => "Map Style Editor - Import Image Options";
        public bool IsLoaded => false;

        public MapStyleEditorImportImageOptionView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
