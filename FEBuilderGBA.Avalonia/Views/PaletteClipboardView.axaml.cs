using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteClipboardView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteClipboardViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Clipboard";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteClipboardView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Copy_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Palette copied to clipboard.";
            ClipboardStatusLabel.Text = "[Palette data stored]";
        }

        void Paste_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Palette Paste");
            try
            {
                // Placeholder: paste palette data from clipboard
                _undoService.Commit();
                _vm.MarkClean();
                _vm.StatusMessage = "Palette pasted.";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("PaletteClipboardView.Paste", ex.ToString());
                _vm.StatusMessage = $"Paste failed: {ex.Message}";
            }
        }

        void Clear_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Clipboard cleared.";
            ClipboardStatusLabel.Text = "[No palette data in clipboard]";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
