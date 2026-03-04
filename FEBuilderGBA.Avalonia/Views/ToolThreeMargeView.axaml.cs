using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeView : Window, IEditorView
    {
        public string ViewTitle => "Three-Way Merge";
        public bool IsLoaded => false;

        public ToolThreeMargeView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
