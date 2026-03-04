using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIScriptCategorySelectView : Window, IEditorView, IDataVerifiableView
    {
        readonly AIScriptCategorySelectViewModel _vm = new();

        public string ViewTitle => "AI Script Category Select";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public AIScriptCategorySelectView()
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

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Categories.Count > 0) CategoryList.SelectedIndex = 0; }
    }
}
