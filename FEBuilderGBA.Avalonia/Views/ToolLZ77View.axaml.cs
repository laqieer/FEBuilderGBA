using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolLZ77View : TranslatedWindow, IEditorView
    {
        readonly ToolLZ77ViewModel _vm = new();

        public string ViewTitle => "LZ77 Compression Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolLZ77View()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        // ---------- Decompress tab ----------

        async void DecompressSrcBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenFile(this, "Open source file", "*");
            if (!string.IsNullOrEmpty(path))
                _vm.DecompressSrcPath = path;
        }

        async void DecompressDestBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = R._("Save Decompressed File"),
                SuggestedFileName = "decompressed.bin",
            });
            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.DecompressDestPath = path;
        }

        void DecompressFire_Click(object? sender, RoutedEventArgs e) => _vm.RunDecompress();

        // ---------- Compress tab ----------

        async void CompressSrcBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenFile(this, "Open source file", "*");
            if (!string.IsNullOrEmpty(path))
                _vm.CompressSrcPath = path;
        }

        async void CompressDestBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = R._("Save Compressed File"),
                SuggestedFileName = "compressed.bin",
            });
            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.CompressDestPath = path;
        }

        void CompressFire_Click(object? sender, RoutedEventArgs e) => _vm.RunCompress();

        // ---------- Erase tab ----------

        void ZeroClear_Click(object? sender, RoutedEventArgs e) => _vm.RunZeroClear();

        // ---------- Base64 tab ----------

        async void Base64TextToFile_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = R._("Save Decoded File"),
                SuggestedFileName = "decoded.bin",
            });
            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.RunBase64TextToFile(path);
        }

        async void FileToBase64Text_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenFile(this, R._("Open file to encode as base64"), "*");
            if (!string.IsNullOrEmpty(path))
                _vm.RunFileToBase64Text(path);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
