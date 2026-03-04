using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorAddMapChangeDialogView : Window, IEditorView
    {
        public string ViewTitle => "Map Editor - Add Map Change";
        public bool IsLoaded => false;

        public MapEditorAddMapChangeDialogView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
