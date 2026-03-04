using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingDifficultyDialogView : Window, IEditorView
    {
        public string ViewTitle => "Map Setting - Difficulty";
        public bool IsLoaded => false;

        public MapSettingDifficultyDialogView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
