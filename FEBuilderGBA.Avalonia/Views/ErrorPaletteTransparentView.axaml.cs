
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorPaletteTransparentView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ErrorPaletteTransparentViewModel _vm = new();
        public string ViewTitle => "Palette Transparent Error";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Palette Transparency Error", 1110, 857, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ErrorPaletteTransparentView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
