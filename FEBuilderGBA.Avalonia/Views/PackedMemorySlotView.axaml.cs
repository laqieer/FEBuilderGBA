using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PackedMemorySlotView : Window, IEditorView
    {
        public string ViewTitle => "Packed Memory Slot";
        public bool IsLoaded => false;

        public PackedMemorySlotView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
