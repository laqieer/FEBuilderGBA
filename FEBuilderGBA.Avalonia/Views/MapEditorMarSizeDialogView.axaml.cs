using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorMarSizeDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapEditorMarSizeDialogViewModel _vm = new();

        public string ViewTitle => "Map Margin Size";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapEditorMarSizeDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Width = (uint)(WidthInput.Value ?? 1);
            _vm.DialogResult = "OK";
            Close(_vm.Width);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
