
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolSubtitleOverlayView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolSubtitleOverlayViewViewModel _vm = new();
        public string ViewTitle => "Subtitle Overlay";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Subtitle Overlay", 992, 230, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolSubtitleOverlayView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Apply_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
