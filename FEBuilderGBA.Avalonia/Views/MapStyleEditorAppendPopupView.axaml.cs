using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorAppendPopupView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapStyleEditorAppendPopupViewModel _vm = new();

        public string ViewTitle => "Map Style Editor - Append";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Style Editor - Append", 1253, 790, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapStyleEditorAppendPopupView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Confirmed = true;
            DialogResult = true; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
