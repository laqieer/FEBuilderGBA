using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorResizeDialogView : Window, IEditorView
    {
        public string ViewTitle => "Map Editor - Resize";
        public bool IsLoaded => false;

        public MapEditorResizeDialogView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
