using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolInitWizardView : Window, IEditorView, IDataVerifiableView
    {
        readonly ToolInitWizardViewModel _vm = new();
        public string ViewTitle => "Setup Wizard";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolInitWizardView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
