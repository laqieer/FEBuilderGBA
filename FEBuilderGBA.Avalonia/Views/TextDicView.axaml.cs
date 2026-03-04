using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextDicView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextDicViewModel _vm = new();

        public string ViewTitle => "Text Dictionary";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextDicView()
        {
            InitializeComponent();
            _vm.Initialize();
            ResultList.ItemsSource = _vm.Entries;
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SearchTerm = SearchBox.Text ?? "";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Entries.Count > 0) ResultList.SelectedIndex = 0; }
    }
}
