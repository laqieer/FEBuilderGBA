using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorImportImageOptionView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapStyleEditorImportImageOptionViewModel _vm = new();

        public string ViewTitle => "Import Map Chip";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Import Map Chip", 1092, 360, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapStyleEditorImportImageOptionView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void WithPalette_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 0; // WithPalette
            DialogResult = 0; RequestClose();
        }

        void ImageOnly_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 1; // ImageOnly
            DialogResult = 1; RequestClose();
        }

        void OnePicture_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedOption = 2; // OnePicture
            DialogResult = 2; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
