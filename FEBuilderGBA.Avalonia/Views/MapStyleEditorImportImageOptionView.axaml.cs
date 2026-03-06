using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorImportImageOptionView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorImportImageOptionViewModel _vm = new();

        public string ViewTitle => "Import Map Chip";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapStyleEditorImportImageOptionView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void WithPalette_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 0; // WithPalette
            Close(0);
        }

        void ImageOnly_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 1; // ImageOnly
            Close(1);
        }

        void OnePicture_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 2; // OnePicture
            Close(2);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
