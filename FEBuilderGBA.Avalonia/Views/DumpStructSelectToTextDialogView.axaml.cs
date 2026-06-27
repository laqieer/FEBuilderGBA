using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DumpStructSelectToTextDialogView : TranslatedWindow, IEditorView, IDataVerifiableView
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

        /// <summary>
        /// Set the file name + text content shown by this preview dialog.
        /// Used by callers (e.g. <c>DumpStructSelectDialogView</c>) that
        /// generate the dump content before opening the preview.
        /// </summary>
        public void SetContent(string fileName, string textContent)
        {
            _vm.Load(fileName, textContent);
            UpdateUI();
        }

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var txtType = new FilePickerFileType(R._("Text Files")) { Patterns = new[] { "*.txt" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Save Text Dump"),
                    SuggestedFileName = _vm.FileName,
                    FileTypeChoices = new[] { txtType, allType },
                });
                if (file != null)
                {
                    // #1639: write via the SAF bridge so Android content:// targets
                    // (no local path) are written through OpenWriteAsync.
                    string content = _vm.TextContent;
                    string? written = await FileDialogHelper.WriteViaAsync(file, p => File.WriteAllText(p, content));
                    if (written != null)
                        CoreState.Services.ShowInfo($"Saved to {written}");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("DumpStructSelectToTextDialogView.Save failed: {0}", ex.Message);
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
