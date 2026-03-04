using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitsShortTextView : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitsShortTextViewModel _vm = new();

        public string ViewTitle => "Units Short Text";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitsShortTextView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
