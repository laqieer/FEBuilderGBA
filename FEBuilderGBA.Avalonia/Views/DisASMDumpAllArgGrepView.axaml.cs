using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMDumpAllArgGrepView : Window, IEditorView
    {
        public string ViewTitle => "Disassembly Dump Grep";
        public bool IsLoaded => false;

        public DisASMDumpAllArgGrepView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
