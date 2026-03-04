using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorAppendPopupView : Window, IEditorView
    {
        public string ViewTitle => "Map Style Editor - Append";
        public bool IsLoaded => false;

        public MapStyleEditorAppendPopupView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
