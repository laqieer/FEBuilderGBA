using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorAddMapChangeDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapEditorAddMapChangeDialogViewModel _vm = new();

        public string ViewTitle => "Add Map Change";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapEditorAddMapChangeDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "new";
            Close("new");
        }

        void Edit_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "edit";
            Close("edit");
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            Close(null);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
