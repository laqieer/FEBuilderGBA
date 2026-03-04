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

        public string ViewTitle => "Map Editor - Map Size";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapEditorMarSizeDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Width = (uint)(WidthInput.Value ?? 0);
            _vm.Height = (uint)(HeightInput.Value ?? 0);
            Close(new { Width = _vm.Width, Height = _vm.Height });
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
