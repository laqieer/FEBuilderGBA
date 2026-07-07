using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolBatchInputView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly PointerToolBatchInputViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool - Batch Input";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Pointer Tool - Batch Input", 918, 585, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                DialogResult = "OK"; RequestClose();
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
