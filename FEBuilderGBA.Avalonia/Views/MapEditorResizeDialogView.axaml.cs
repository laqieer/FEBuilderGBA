using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorResizeDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapEditorResizeDialogViewModel _vm = new();

        public string ViewTitle => "Map Editor - Resize";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapEditorResizeDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.NewWidth = (uint)(NewWidthInput.Value ?? 15);
            _vm.NewHeight = (uint)(NewHeightInput.Value ?? 10);
            Close(new { Width = _vm.NewWidth, Height = _vm.NewHeight });
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
