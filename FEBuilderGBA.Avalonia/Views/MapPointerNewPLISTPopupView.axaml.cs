using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerNewPLISTPopupView : Window, IEditorView
    {
        public string ViewTitle => "Map Pointer - New PLIST";
        public bool IsLoaded => false;

        public MapPointerNewPLISTPopupView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
