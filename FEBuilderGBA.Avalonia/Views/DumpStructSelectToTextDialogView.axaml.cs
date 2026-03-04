using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DumpStructSelectToTextDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly DumpStructSelectToTextDialogViewModel _vm = new();

        public string ViewTitle => "Dump Struct to Text";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public DumpStructSelectToTextDialogView()
        {
            InitializeComponent();
            _vm.Load("dump.txt", "(No content loaded yet.)");
            UpdateUI();
        }

        void UpdateUI()
        {
            FileNameBox.Text = _vm.FileName;
            ContentBox.Text = _vm.TextContent;
        }

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var txtType = new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Text Dump",
                    SuggestedFileName = _vm.FileName,
                    FileTypeChoices = new[] { txtType, allType },
                });
                if (file != null)
                {
                    string? path = file.TryGetLocalPath();
                    if (path != null)
                    {
                        File.WriteAllText(path, _vm.TextContent);
                        CoreState.Services.ShowInfo($"Saved to {path}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectToTextDialogView.Save failed: {0}", ex.Message);
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
