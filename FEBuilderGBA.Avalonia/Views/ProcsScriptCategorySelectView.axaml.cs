using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ProcsScriptCategorySelectView : Window, IEditorView, IDataVerifiableView
    {
        readonly ProcsScriptCategorySelectViewModel _vm = new();

        public string ViewTitle => "Procs Script Category Select";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ProcsScriptCategorySelectView()
        {
            InitializeComponent();
            _vm.Load();
            CategoryList.ItemsSource = _vm.Categories;
            if (_vm.Categories.Count > 0)
                CategoryList.SelectedIndex = 0;
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedCategory = CategoryList.SelectedItem as string ?? "";
            Close(_vm.SelectedCategory);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Categories.Count > 0) CategoryList.SelectedIndex = 0; }
    }
}
