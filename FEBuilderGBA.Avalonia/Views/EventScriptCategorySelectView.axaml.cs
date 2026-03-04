using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptCategorySelectView : Window, IEditorView
    {
        public string ViewTitle => "Event Script Category Select";
        public bool IsLoaded => false;

        public EventScriptCategorySelectView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
