using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UbyteBitFlagView : Window, IEditorView
    {
        public string ViewTitle => "Byte Bit Flags";
        public bool IsLoaded => false;

        public UbyteBitFlagView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
