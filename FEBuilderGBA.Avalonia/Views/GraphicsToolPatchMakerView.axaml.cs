using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class GraphicsToolPatchMakerView : Window, IEditorView, IDataVerifiableView
    {
        readonly GraphicsToolPatchMakerViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Graphics Tool Patch Maker";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolPatchMakerView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void BrowseOriginal_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (!string.IsNullOrEmpty(path))
                _vm.OriginalRomPath = path;
        }

        async void BrowseModified_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (!string.IsNullOrEmpty(path))
                _vm.ModifiedRomPath = path;
        }

        void Generate_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.GeneratePatch();
            }
            catch (Exception ex)
            {
                Log.Error("GraphicsToolPatchMaker.Generate", ex.ToString());
                _vm.StatusMessage = "Error: " + ex.Message;
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Graphics Patch",
                    SuggestedFileName = "graphics_patch.txt",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Text Patch File") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                    },
                });

                var outputPath = file?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(outputPath))
                {
                    _vm.SavePatch(outputPath);
                    _vm.MarkClean();
                }
            }
            catch (Exception ex)
            {
                Log.Error("GraphicsToolPatchMakerView.Save", ex.ToString());
                _vm.StatusMessage = "Error saving: " + ex.Message;
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
