using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UwordBitFlagView : Window, IEditorView
    {
        public string ViewTitle => "Word Bit Flags";
        public bool IsLoaded => false;

        public UwordBitFlagView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
