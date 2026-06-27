using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolDiffView : TranslatedWindow, IEditorView
    {
        readonly ToolDiffViewModel _vm = new();

        public string ViewTitle => "ROM Diff Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolDiffView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        // ---------- 2-ROM Diff tab ----------

        async void OtherBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (!string.IsNullOrEmpty(path))
                _vm.OtherPath = path;
        }

        async void MakeBinPatch_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save Binary Patch File"),
                SuggestedFileName = "PATCH_diff.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Patch") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                },
            });
            // #1639: RunMakeBinPatch writes the patch by path → route through the
            // SAF bridge (temp + write-back on Android).
            await FileDialogHelper.WriteViaAsync(file, p => _vm.RunMakeBinPatch(p));
        }

        // ---------- 3-ROM Diff tab ----------

        async void ABrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (!string.IsNullOrEmpty(path))
                _vm.AFilePath = path;
        }

        async void BBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (!string.IsNullOrEmpty(path))
                _vm.BFilePath = path;
        }

        async void MakeBinPatch3_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save 3-Way Binary Patch File"),
                SuggestedFileName = "PATCH_diff3.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Patch") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                },
            });
            // #1639: RunMakeBinPatch3 writes the patch by path → route through the
            // SAF bridge (temp + write-back on Android).
            await FileDialogHelper.WriteViaAsync(file, p => _vm.RunMakeBinPatch3(p));
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
