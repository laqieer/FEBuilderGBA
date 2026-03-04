using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBGSelectPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly ImageBGSelectPopupViewViewModel _vm = new();

        public string ViewTitle => "BG Image Select";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ImageBGSelectPopupView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Select_Click(object? sender, RoutedEventArgs e)
        {
            var selected = BGList.SelectedItem;
            Close(selected);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
