using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolASMEditView : Window, IEditorView
    {
        readonly ToolASMEditViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "ASM Edit";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolASMEditView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Compile_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("ASM Compile");
            try
            {
                // Placeholder: compile and write ASM to ROM
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ToolASMEditView.Compile", ex.ToString());
            }
        }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
