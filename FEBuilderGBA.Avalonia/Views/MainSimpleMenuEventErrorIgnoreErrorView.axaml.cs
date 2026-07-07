using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainSimpleMenuEventErrorIgnoreErrorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly MainSimpleMenuEventErrorIgnoreErrorViewModel _vm = new();
        public string ViewTitle => "Hide this error";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Hide this error", 820, 355, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MainSimpleMenuEventErrorIgnoreErrorView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
