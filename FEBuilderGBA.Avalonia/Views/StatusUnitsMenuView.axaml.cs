using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusUnitsMenuView : Window, IEditorView
    {
        public string ViewTitle => "Status Units Menu";
        public bool IsLoaded => false;

        public StatusUnitsMenuView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
