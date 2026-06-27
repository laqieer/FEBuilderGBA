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

        // #1639: the dest browse stores a TARGET that the Fire button writes to
        // LATER. On Android SAF there is no local path, so retain the picked
        // handle and write the produced bytes back through it on Fire.
        IStorageFile? _decompressDestFile;
        IStorageFile? _compressDestFile;

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
            if (file == null) return;
            // #1639: retain the handle; show the local path (desktop) or the SAF
            // display name so the user sees their chosen target.
            _decompressDestFile = file;
            _vm.DecompressDestPath = file.TryGetLocalPath() ?? file.Name ?? "decompressed.bin";
        }

        async void DecompressFire_Click(object? sender, RoutedEventArgs e)
        {
            // #1639: when the target is a SAF document, write the produced bytes
            // through its handle via the bridge (the VM writes to a temp path).
            // DecompressDestPath holds the user-facing display label; swap in the
            // temp path only for the write, then RESTORE the label so the textbox
            // keeps showing the chosen target, not the temp path.
            if (_decompressDestFile != null && string.IsNullOrEmpty(_decompressDestFile.TryGetLocalPath()))
            {
                string displayLabel = _vm.DecompressDestPath;
                await FileDialogHelper.WriteViaAsync(_decompressDestFile, p => { _vm.DecompressDestPath = p; _vm.RunDecompress(); });
                _vm.DecompressDestPath = displayLabel;
            }
            else
                _vm.RunDecompress();
        }

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
            if (file == null) return;
            _compressDestFile = file;
            _vm.CompressDestPath = file.TryGetLocalPath() ?? file.Name ?? "compressed.bin";
        }

        async void CompressFire_Click(object? sender, RoutedEventArgs e)
        {
            // #1639: same as DecompressFire — swap in the temp path only for the
            // bridged write, then RESTORE the display label.
            if (_compressDestFile != null && string.IsNullOrEmpty(_compressDestFile.TryGetLocalPath()))
            {
                string displayLabel = _vm.CompressDestPath;
                await FileDialogHelper.WriteViaAsync(_compressDestFile, p => { _vm.CompressDestPath = p; _vm.RunCompress(); });
                _vm.CompressDestPath = displayLabel;
            }
            else
                _vm.RunCompress();
        }

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
            // #1639: immediate write → route through the SAF bridge (temp +
            // write-back on Android).
            await FileDialogHelper.WriteViaAsync(file, p => _vm.RunBase64TextToFile(p));
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
            // Always start a fresh confirmation flow — cancellation at any prompt
            // below must NOT leave stale flags that auto-skip prompts on next click.
            _vm.RecompressModifiedAcknowledged = false;
            _vm.RecompressConfirmed = false;

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
                    // Reset acknowledgement too — the user has not yet started a real run.
                    _vm.RecompressModifiedAcknowledged = false;
                    return;
                }
                _vm.RecompressConfirmed = true;
            }

            // Run the (potentially multi-minute) scan + recompress off the UI thread
            // so the window stays responsive. IsBusy disables interactions and acts as
            // a re-entrancy guard against double-clicks.
            if (_vm.IsBusy) return;
            _vm.IsBusy = true;
            _vm.StatusText = R._("Recompress: running (this may take several minutes)...");
            try
            {
                await System.Threading.Tasks.Task.Run(() => _vm.RunRecompress());
            }
            finally
            {
                _vm.IsBusy = false;
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}