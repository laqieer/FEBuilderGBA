using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveToFreeSpaceView : Window, IEditorView
    {
        readonly MoveToFreeSpaceViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Move to Free Space";
        public bool IsLoaded => _vm.IsLoaded;

        public MoveToFreeSpaceView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Move_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Move to Free Space");
            try
            {
                _vm.ExecuteMove();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch
            {
                _undoService.Rollback();
                throw;
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            _vm.CurrentAddress = $"0x{address:X08}";
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        public void SelectFirstItem() { }
    }
}
