using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBGSelectPopupView : Window, IEditorView
    {
        public string ViewTitle => "BG Image Select";
        public bool IsLoaded => false;

        public ImageBGSelectPopupView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
