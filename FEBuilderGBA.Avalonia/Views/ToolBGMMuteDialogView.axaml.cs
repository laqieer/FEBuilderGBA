using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolBGMMuteDialogView : Window, IEditorView
    {
        public string ViewTitle => "BGM Mute";
        public bool IsLoaded => false;

        public ToolBGMMuteDialogView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
