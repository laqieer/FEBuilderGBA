using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolSubtitleOverlayView : Window, IEditorView
    {
        public string ViewTitle => "Subtitle Overlay";
        public bool IsLoaded => false;

        public ToolSubtitleOverlayView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
