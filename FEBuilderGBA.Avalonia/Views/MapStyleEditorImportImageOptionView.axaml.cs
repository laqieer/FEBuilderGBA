using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorImportImageOptionView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorImportImageOptionViewModel _vm = new();

        public string ViewTitle => "Map Style Editor - Import Image Options";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapStyleEditorImportImageOptionView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            if (AppendOption.IsChecked == true)
                _vm.SelectedOption = 1;
            else if (InsertOption.IsChecked == true)
                _vm.SelectedOption = 2;
            else
                _vm.SelectedOption = 0;
            Close(_vm.SelectedOption);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
