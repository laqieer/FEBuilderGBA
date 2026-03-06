using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class GraphicsToolPatchMakerView : Window, IEditorView, IDataVerifiableView
    {
        readonly GraphicsToolPatchMakerViewViewModel _vm = new();

        public string ViewTitle => "Graphics Tool Patch Maker";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolPatchMakerView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        void Save_Click(object? sender, RoutedEventArgs e)
        {
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
