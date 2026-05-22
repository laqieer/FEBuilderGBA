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

        async void DecompressSrcBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenFile(this, "Open source file", "*");
            if (!string.IsNullOrEmpty(path))
                _vm.DecompressSrcPath = path;
        }

        async void DecompressDestBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save Decompressed File"),
                SuggestedFileName = "decompressed.bin",
            });
            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.DecompressDestPath = path;
        }

        void DecompressFire_Click(object? sender, RoutedEventArgs e) => _vm.RunDecompress();

        async void CompressSrcBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenFile(this, "Open source file", "*");
            if (!string.IsNullOrEmpty(path))
                _vm.CompressSrcPath = path;
        }

        async void CompressDestBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save Compressed File"),
                SuggestedFileName = "compressed.bin",
            });
            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.CompressDestPath = path;
        }

        void CompressFire_Click(object? sender, RoutedEventArgs e) => _vm.RunCompress();

        async void ZeroClear_Click(object? sender, RoutedEventArgs e)
        {
            if (!ToolLZ77ViewModel.TryParseHex(_vm.ZeroClearFromText, out uint fromRaw)
                || !ToolLZ77ViewModel.TryParseHex(_vm.ZeroClearToText, out uint toRaw))
            {
                _vm.RunZeroClear();
                return;
            }

            uint from = U.toOffset(fromRaw);
            uint to = U.toOffset(toRaw);
            if (to < from) { uint t = from; from = to; to = t; }

            if (_vm.ZeroClearNeedsConfirmation(from, to))
            {
                var result = await MessageBoxWindow.Show(this,
                    R._("Zeroing 0x{0:X8}..0x{1:X8} hits a dangerous low-address region (ROM header, fixed tables). Continue?", from, to),
                    R._("Confirm Zero Clear"),
                    MessageBoxMode.YesNo);
                if (result != MessageBoxResult.Yes)
                {
                    _vm.StatusText = R._("ZeroClear: canceled by user.");
                    return;
                }
                _vm.ZeroClearConfirmed = true;
            }
            _vm.RunZeroClear();
        }

        async void Base64TextToFile_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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