using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorPaletteShowView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ErrorPaletteShowViewModel _vm = new();
        public string ViewTitle => "Palette Error Display";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Palette Error Display", 1210, 716, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ErrorPaletteShowView()
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
