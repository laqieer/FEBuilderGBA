using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteSwapView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteSwapViewViewModel _vm = new();

        public string ViewTitle => "Palette Swap";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteSwapView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Swap_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Swap executed.";
            Close(true);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
