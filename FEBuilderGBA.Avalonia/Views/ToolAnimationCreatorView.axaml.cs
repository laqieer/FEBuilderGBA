using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAnimationCreatorView : Window, IEditorView
    {
        public string ViewTitle => "Animation Creator";
        public bool IsLoaded => false;

        public ToolAnimationCreatorView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
