using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EmulatorMemoryView : Window, IEditorView
    {
        public string ViewTitle => "Emulator Memory";
        public bool IsLoaded => false;

        public EmulatorMemoryView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
