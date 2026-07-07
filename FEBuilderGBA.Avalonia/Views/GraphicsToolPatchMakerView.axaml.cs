using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class GraphicsToolPatchMakerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly GraphicsToolPatchMakerViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Graphics Tool Patch Maker";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Graphics Tool Patch Maker", 620, 580, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
            var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
            if (!string.IsNullOrEmpty(path))
                _vm.OriginalRomPath = path;
        }

        async void BrowseModified_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
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

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Save Graphics Patch"),
                    SuggestedFileName = "graphics_patch.txt",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(R._("Text Patch File")) { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    },
                });

                if (file != null)
                {
                    // #1639: write via the SAF bridge so Android content:// targets
                    // (no local path) are written through OpenWriteAsync.
                    bool ok = false;
                    string? written = await FileDialogHelper.WriteViaAsync(file, p => { ok = _vm.SavePatch(p); });
                    if (written != null && ok)
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
