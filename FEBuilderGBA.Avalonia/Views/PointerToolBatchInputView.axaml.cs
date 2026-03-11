using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolBatchInputView : Window, IEditorView
    {
        readonly PointerToolBatchInputViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool - Batch Input";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolBatchInputView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("PointerTool BatchInput");
            try
            {
                _vm.ProcessBatch();
                _undoService.Commit();
                _vm.MarkClean();
                Close("OK");
            }
            catch
            {
                _undoService.Rollback();
                throw;
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
