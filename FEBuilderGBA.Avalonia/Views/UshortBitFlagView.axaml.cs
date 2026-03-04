using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UshortBitFlagView : Window, IEditorView
    {
        public string ViewTitle => "Short Bit Flags";
        public bool IsLoaded => false;

        public UshortBitFlagView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
