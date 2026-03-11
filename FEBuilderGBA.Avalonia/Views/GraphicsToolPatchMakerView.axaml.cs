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
        readonly UndoService _undoService = new();

        public string ViewTitle => "Graphics Tool Patch Maker";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolPatchMakerView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        void Save_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Graphics Patch Save");
            try
            {
                // Placeholder: save graphics patch data
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("GraphicsToolPatchMakerView.Save", ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
