using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorWarningOverrideView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapStyleEditorWarningOverrideViewModel _vm = new();

        public string ViewTitle => "Map Style Editor - Override Warning";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Style Editor - Override Warning", 450, 240, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapStyleEditorWarningOverrideView()
        {
            InitializeComponent();
            _vm.Initialize();
            WarningText.Text = _vm.WarningMessage;
        }

        void OK_Click(object? sender, RoutedEventArgs e) { DialogResult = true; RequestClose(); }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
