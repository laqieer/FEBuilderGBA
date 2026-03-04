using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolExportEAEventView : Window, IEditorView
    {
        public string ViewTitle => "Export EA Event";
        public bool IsLoaded => false;

        public ToolExportEAEventView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
