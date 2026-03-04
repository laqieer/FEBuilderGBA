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
            _vm.Initialize();
        }

        void CreatePatch_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Patch creation is not yet implemented in the Avalonia version.";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
