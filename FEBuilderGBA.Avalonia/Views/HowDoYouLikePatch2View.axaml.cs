using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HowDoYouLikePatch2View : Window, IEditorView
    {
        public string ViewTitle => "Patch Feedback (Extended)";
        public bool IsLoaded => false;

        public HowDoYouLikePatch2View()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
