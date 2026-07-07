using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ErrorUnknownROMView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ErrorUnknownROMViewModel _vm = new();
        public string ViewTitle => "Unknown ROM Version";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unknown ROM Version", 866, 820, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ErrorUnknownROMView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "";
            RequestClose();
        }

        void FE8U_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE8U";
            RequestClose();
        }

        void FE8J_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE8J";
            RequestClose();
        }

        void FE7U_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE7U";
            RequestClose();
        }

        void FE7J_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE7J";
            RequestClose();
        }

        void FE6_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "FE6";
            RequestClose();
        }

        void Unknown_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedVersion = "NAZO";
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
