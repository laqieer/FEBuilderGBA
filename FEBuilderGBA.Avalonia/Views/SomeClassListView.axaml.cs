using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SomeClassListView : Window, IEditorView, IDataVerifiableView
    {
        readonly SomeClassListViewModel _vm = new();

        public string ViewTitle => "Class List";
        public bool IsLoaded => _vm.IsLoaded;

        public SomeClassListView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
