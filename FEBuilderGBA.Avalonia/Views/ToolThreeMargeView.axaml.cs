using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeView : TranslatedWindow, IEditorView
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
            // #1639: the merged ROM is a single-file output → pick the handle and
            // write through the SAF bridge so Android content:// targets work.
            var file = await FileDialogHelper.SaveRomFilePick(this, "merged.gba");
            if (file == null) return;
            await FileDialogHelper.WriteViaAsync(file, p => _vm.SaveMerged(p));
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
