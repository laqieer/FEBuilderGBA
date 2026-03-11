using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolView : Window, IEditorView
    {
        readonly PointerToolViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AddressInput = AddressTextBox.Text ?? "";
            _vm.RunSearch();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("PointerTool Write");
            try
            {
                _vm.WritePointerValue();
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
            _vm.AddressInput = $"0x{address:X08}";
            AddressTextBox.Text = _vm.AddressInput;
            _vm.RunSearch();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        public void SelectFirstItem() { }
    }
}
