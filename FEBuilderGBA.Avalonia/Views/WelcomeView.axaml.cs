using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WelcomeView : Window, IEditorView, IDataVerifiableView
    {
        readonly WelcomeViewModel _vm = new();
        public string ViewTitle => "Welcome";
        public bool IsLoaded => _vm.IsLoaded;

        public WelcomeView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
