using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteChangeColorsView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteChangeColorsViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Change Colors";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteChangeColorsView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Palette Change Colors");
            try
            {
                // Placeholder: apply color changes to palette
                _undoService.Commit();
                _vm.MarkClean();
                _vm.StatusMessage = "Color applied.";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("PaletteChangeColorsView.Apply", ex.ToString());
                _vm.StatusMessage = $"Apply failed: {ex.Message}";
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
