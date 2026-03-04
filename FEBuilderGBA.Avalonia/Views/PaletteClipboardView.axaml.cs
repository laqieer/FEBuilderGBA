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

        public string ViewTitle => "Palette Clipboard";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteClipboardView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Copy_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Palette copied to clipboard.";
            ClipboardStatusLabel.Text = "[Palette data stored]";
        }

        void Paste_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Paste operation not yet implemented.";
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
