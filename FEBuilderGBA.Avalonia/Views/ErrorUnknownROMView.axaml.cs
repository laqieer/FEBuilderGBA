using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorUnknownROMView : Window, IEditorView
    {
        public string ViewTitle => "Unknown ROM Error";
        public bool IsLoaded => false;

        public ErrorUnknownROMView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
