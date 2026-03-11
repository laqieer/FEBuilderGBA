using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolMAPView : Window, IEditorView
    {
        readonly RAMRewriteToolMAPViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "RAM Rewrite Tool (MAP)";
        public bool IsLoaded => _vm.IsLoaded;

        public RAMRewriteToolMAPView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("RAM Rewrite MAP");
            try
            {
                // Placeholder: write MAP data
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("RAMRewriteToolMAPView.Write", ex.ToString());
            }
        }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
