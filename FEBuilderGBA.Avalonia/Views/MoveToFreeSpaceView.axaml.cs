using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveToFreeSpaceView : Window, IEditorView
    {
        public string ViewTitle => "Move to Free Space";
        public bool IsLoaded => false;

        public MoveToFreeSpaceView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
