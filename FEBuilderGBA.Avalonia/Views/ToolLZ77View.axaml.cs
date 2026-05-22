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

        async void Move_Click(object? sender, RoutedEventArgs e)
        {
            // Always start a fresh confirmation flow — cancellation at any prompt
            // below must NOT leave stale flags that auto-skip prompts on next click.
            _vm.MoveRawFallbackConfirmed = false;
            _vm.MovePointerCountConfirmed = false;

            // Two-phase: preflight, prompt as needed, then execute.
            var preflight = _vm.MovePreflight(out uint srcOffset, out uint dstOffset, out uint length, out var sp);
            if (preflight == ToolLZ77ViewModel.MovePreflightResult.ErrorAlreadyShown)
                return;

            // Optional raw-fallback warning.
            if (preflight == ToolLZ77ViewModel.MovePreflightResult.NeedRawFallbackConfirm)
            {
                var dr = await MessageBoxWindow.Show(this,
                    R._("LDR pointer search returned no hits. Falling back to a raw 4-byte binary pointer scan (may match unrelated bytes). Continue?"),
                    R._("Confirm Raw Pointer Fallback"),
                    MessageBoxMode.YesNo);
                if (dr != MessageBoxResult.Yes)
                {
                    _vm.StatusText = R._("Move: canceled at raw-fallback prompt.");
                    return;
                }
                _vm.MoveRawFallbackConfirmed = true;
                // Re-run preflight after confirmation.
                preflight = _vm.MovePreflight(out srcOffset, out dstOffset, out length, out sp);
            }

            // Optional pointer-count warning.
            if (preflight == ToolLZ77ViewModel.MovePreflightResult.NeedPointerCountConfirm)
            {
                var dr = await MessageBoxWindow.Show(this,
                    R._("{0} pointer reference(s) will be rewritten. Continue?", sp.Pointers.Count),
                    R._("Confirm Multi-Pointer Move"),
                    MessageBoxMode.YesNo);
                if (dr != MessageBoxResult.Yes)
                {
                    _vm.StatusText = R._("Move: canceled at pointer-count prompt.");
                    // Reset BOTH flags — earlier raw-fallback approval is now stale.
                    _vm.MoveRawFallbackConfirmed = false;
                    _vm.MovePointerCountConfirmed = false;
                    return;
                }
                _vm.MovePointerCountConfirmed = true;
            }

            // Final main-confirm prompt.
            var confirm = await MessageBoxWindow.Show(this,
                R._("Move 0x{0:X} bytes from 0x{1:X8} to 0x{2:X8}{3}?",
                    length, srcOffset, dstOffset,
                    dstOffset == 0 ? " (auto-allocate)" : ""),
                R._("Confirm Move"),
                MessageBoxMode.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                _vm.StatusText = R._("Move: canceled at final prompt.");
                _vm.MoveRawFallbackConfirmed = false;
                _vm.MovePointerCountConfirmed = false;
                return;
            }

            _vm.RunMove();
        }

        async void Recompress_Click(object? sender, RoutedEventArgs e)
        {
            var preflight = _vm.RecompressPreflight();
            if (preflight == ToolLZ77ViewModel.RecompressPreflightResult.ErrorAlreadyShown)
                return;

            if (preflight == ToolLZ77ViewModel.RecompressPreflightResult.NeedRomModifiedAck)
            {
                var dr = await MessageBoxWindow.Show(this,
                    R._("ROM has unsaved modifications. Save first before recompressing? (Pressing No will proceed anyway, which is risky.)"),
                    R._("Confirm Recompress"),
                    MessageBoxMode.YesNo);
                if (dr == MessageBoxResult.Yes)
                {
                    _vm.StatusText = R._("Recompress: save ROM and retry.");
                    return;
                }
                _vm.RecompressModifiedAcknowledged = true;
                preflight = _vm.RecompressPreflight();
            }

            if (preflight == ToolLZ77ViewModel.RecompressPreflightResult.NeedConfirm)
            {
                var dr = await MessageBoxWindow.Show(this,
                    R._("Run LZ77 recompress? This walks the entire ROM (slow) and rewrites any entries that compress smaller. Heuristic scan — may miss entries WinForms catches."),
                    R._("Confirm Recompress"),
                    MessageBoxMode.YesNo);
                if (dr != MessageBoxResult.Yes)
                {
                    _vm.StatusText = R._("Recompress: canceled by user.");
                    return;
                }
                _vm.RecompressConfirmed = true;
            }

            _vm.RunRecompress();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}