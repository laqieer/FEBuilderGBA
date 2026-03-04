using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorWarningOverrideView : Window, IEditorView
    {
        public string ViewTitle => "Map Style Editor - Override Warning";
        public bool IsLoaded => false;

        public MapStyleEditorWarningOverrideView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
