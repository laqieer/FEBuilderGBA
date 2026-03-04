using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAutomaticRecoveryROMHeaderView : Window, IEditorView
    {
        public string ViewTitle => "Automatic Recovery ROM Header";
        public bool IsLoaded => false;

        public ToolAutomaticRecoveryROMHeaderView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
