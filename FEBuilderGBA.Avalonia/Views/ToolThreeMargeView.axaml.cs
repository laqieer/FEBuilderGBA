using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeView : Window, IEditorView
    {
        readonly ToolThreeMargeViewViewModel _vm = new();
        public string ViewTitle => "Three-Way Merge";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolThreeMargeView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void BrowseOriginal_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (path != null)
                _vm.OriginalPath = path;
        }

        async void BrowseMine_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (path != null)
                _vm.MyPath = path;
        }

        async void BrowseTheirs_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (path != null)
                _vm.TheirsPath = path;
        }

        void Merge_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanMerge)
                return;
            _vm.RunMerge();
        }

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.SaveRomFile(this, "merged.gba");
            if (path != null)
                _vm.SaveMerged(path);
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
