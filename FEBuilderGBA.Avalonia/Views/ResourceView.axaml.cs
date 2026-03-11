using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ResourceView : Window, IEditorView, IDataVerifiableView
    {
        readonly ResourceViewModel _vm = new();
        public string ViewTitle => "Resources";
        public bool IsLoaded => _vm.IsLoaded;

        public ResourceView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
