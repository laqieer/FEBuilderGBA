using global::Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolROMRebuildView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolROMRebuildViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "ROM Rebuild Tool";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("ROM Rebuild Tool", 760, 480);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolROMRebuildView()
        {
            InitializeComponent();
            IntroText.Text = R._("Rebuild (defragment) the ROM: produce a faithful rebuild manifest from the modified ROM against the clean original, apply it onto the clean base, and write the compacted ROM. You can also write a .rebuild analysis report listing the modified regions and reusable free space.");
            RebuildHelpText.Text = R._("Rebuilds the ROM end-to-end and writes the compacted ROM to a file you choose. If this ROM carries an installed patch the rebuilder cannot reproduce, the rebuild is refused with an explanation (no file is written).");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                try
                {
                    bool ok = _vm.Load();
                    AddressHelpText.Text = BuildAddressHelp();
                    // Always populate the address field with the real default so it
                    // matches the help text — even when the ROM can't be rebuilt, where
                    // Load() leaves RebuildAddress unset and the field would otherwise
                    // fall back to the misleading watermark example.
                    RebuildAddressTextBox.Text = string.Format("{0:X8}",
                        ok ? _vm.RebuildAddress : _vm.DefaultRebuildAddress());
                    if (!ok)
                    {
                        StatusText.Text = R._("This ROM does not use an extended region, so it cannot be rebuilt.");
                        MakeButton.IsEnabled = false;
                        RebuildButton.IsEnabled = false;
                        return;
                    }
                    string found = _vm.FindOriginal();
                    if (!string.IsNullOrEmpty(found))
                        OriginalRomTextBox.Text = found;
                }
                catch (Exception ex)
                {
                    Log.Error("ToolROMRebuildView.Opened failed: " + ex);
                }
            }
        }

        string BuildAddressHelp()
        {
            uint def = _vm.DefaultRebuildAddress();
            // Mirrors the gist of WF GetExplainRebuildAddress without the WinForms-only details.
            return R._("Data from this address onward is rebuilt. The default is {0}. Normally leave it unchanged; raise it if the rebuild fails because of custom-installed ASM.", "0x" + string.Format("{0:X8}", def));
        }

        async void Select_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path))
                    OriginalRomTextBox.Text = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolROMRebuildView.Select_Click failed: " + ex);
            }
        }

        /// <summary>
        /// Shared address gate for both the Rebuild and analysis-report flows: parse the hex
        /// field, hard-fail on misaligned/unsafe, Yes/No prompt on below-extends. Returns the
        /// parsed address on success; null when the caller must abort (bad field, unsafe, or the
        /// user declined the below-extends warning) — a status message is set in every abort case.
        /// </summary>
        async Task<uint?> ValidateAddressForFlow()
        {
            if (!TryParseHex(RebuildAddressTextBox.Text, out uint rebuildAddress))
            {
                StatusText.Text = R._("The rebuild address is not a valid hexadecimal number.");
                return null;
            }

            var check = _vm.ValidateRebuildAddress(rebuildAddress);
            if (check == ToolROMRebuildViewModel.AddressCheck.NotAligned)
            {
                StatusText.Text = R._("This address ({0}) cannot be used: it is not a multiple of 4.", "0x" + string.Format("{0:X8}", rebuildAddress));
                return null;
            }
            if (check == ToolROMRebuildViewModel.AddressCheck.Unsafe)
            {
                StatusText.Text = R._("This address ({0}) cannot be used: the address range is dangerous.", "0x" + string.Format("{0:X8}", rebuildAddress));
                return null;
            }
            if (check == ToolROMRebuildViewModel.AddressCheck.BelowExtends)
            {
                var dr = await MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                    R._("Rebuilding an address ({0}) below the extended region is dangerous. Continue anyway?", "0x" + string.Format("{0:X8}", rebuildAddress)),
                    ViewTitle, MessageBoxMode.YesNo);
                if (dr != MessageBoxResult.Yes)
                {
                    // Honour the doc-comment contract: set a status message in every abort case so
                    // the user sees why nothing happened (rather than a stale/blank status).
                    StatusText.Text = R._("Cancelled.");
                    return null;
                }
            }
            return rebuildAddress;
        }

        async void Rebuild_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string original = OriginalRomTextBox.Text ?? "";
                if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                {
                    StatusText.Text = R._("Please select a valid original (unmodified) ROM.");
                    return;
                }

                uint? validated = await ValidateAddressForFlow();
                if (validated == null) return;
                uint rebuildAddress = validated.Value;

                string suggested = "rebuilt_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".gba";
                // #1639: ROM rebuild compares the output path against the original
                // ROM, reveals it in the file explorer, and is a desktop power
                // flow — it needs a real local path. Pick the handle so we can tell
                // a genuine CANCEL (null handle → silent return) apart from a SAF
                // pick with no local path (→ explicit Android message).
                var outFile = await FileDialogHelper.SaveRomFilePick(TopLevel.GetTopLevel(this), suggested);
                if (outFile == null) return;   // user cancelled
                string? output = outFile.TryGetLocalPath();
                if (string.IsNullOrEmpty(output))
                {
                    StatusText.Text = R._("ROM rebuild needs desktop file-system access and is not available on this device.");
                    return;
                }

                MakeButton.IsEnabled = false;
                RebuildButton.IsEnabled = false;
                StatusText.Text = R._("Rebuilding…");

                // Progress<T> captures the UI SynchronizationContext here, so Report() marshals
                // back to the UI thread — set the text directly (see Make_Click rationale).
                var progress = new Progress<string>(msg => StatusText.Text = msg);

                // The produce→apply→write pipeline is Core/headless; run it off the UI thread so
                // a large ROM (the apply step reserves 32MB + recompresses LZ77) never freezes the
                // window. RebuildRom catches the gate-refusal InvalidOperationException internally.
                var result = await Task.Run(() => _vm.RebuildRom(original, rebuildAddress, output, progress));

                StatusText.Text = result switch
                {
                    ToolROMRebuildViewModel.RebuildResult.Ok =>
                        R._("ROM rebuilt successfully. Wrote {0} ({1} bytes).", output, _vm.LastRebuiltSize.ToString()),
                    ToolROMRebuildViewModel.RebuildResult.NoRom => R._("No ROM is loaded."),
                    ToolROMRebuildViewModel.RebuildResult.OriginalMissing => R._("Please select a valid original (unmodified) ROM."),
                    ToolROMRebuildViewModel.RebuildResult.OriginalUnreadable => R._("The original ROM could not be read."),
                    ToolROMRebuildViewModel.RebuildResult.OriginalNotMatching => R._("The selected ROM is not the unmodified original for this game (CRC32 mismatch). Select the official clean ROM."),
                    ToolROMRebuildViewModel.RebuildResult.BadAddress => R._("The rebuild address is invalid."),
                    ToolROMRebuildViewModel.RebuildResult.OutputCollision => R._("The output ROM must be a different file from the original ROM and the loaded ROM."),
                    // Gate refusal — surface the Core reason verbatim (it begins "ROM rebuild unavailable: …").
                    ToolROMRebuildViewModel.RebuildResult.GateRefused => _vm.LastMessage,
                    ToolROMRebuildViewModel.RebuildResult.Cancelled => R._("The rebuild was cancelled."),
                    ToolROMRebuildViewModel.RebuildResult.ApplyFailed => R._("The rebuild could not be completed. {0}", _vm.LastMessage),
                    _ => R._("Failed to rebuild the ROM. {0}", _vm.LastMessage),
                };

                if (result == ToolROMRebuildViewModel.RebuildResult.Ok)
                    RevealInExplorer(output);
            }
            catch (Exception ex)
            {
                Log.Error("ToolROMRebuildView.Rebuild_Click failed: " + ex);
                StatusText.Text = R._("Failed to rebuild the ROM.");
            }
            finally
            {
                MakeButton.IsEnabled = true;
                RebuildButton.IsEnabled = true;
            }
        }

        async void Make_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string original = OriginalRomTextBox.Text ?? "";
                if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                {
                    StatusText.Text = R._("Please select a valid original (unmodified) ROM.");
                    return;
                }

                // WF CheckRebuildAddress: hard-fail on misaligned/unsafe, warn (Yes/No) when below extends.
                uint? validated = await ValidateAddressForFlow();
                if (validated == null) return;
                uint rebuildAddress = validated.Value;

                string suggested = ToolROMRebuildViewModel.SuggestedName(DateTime.Now.ToString("yyyyMMddHHmmss"));
                // #1639: the ROMRebuild report is part of the desktop rebuild flow
                // (path-based, revealed in the explorer). Pick the handle so a
                // genuine CANCEL (null handle) is distinguished from a SAF pick
                // with no local path (→ explicit Android message).
                var outFile = await FileDialogHelper.SaveFilePick(TopLevel.GetTopLevel(this),
                    R._("Save ROMRebuild report"), R._("ROMRebuild report"), "*.rebuild", suggested);
                if (outFile == null) return;   // user cancelled
                string? output = outFile.TryGetLocalPath();
                if (string.IsNullOrEmpty(output))
                {
                    StatusText.Text = R._("ROM rebuild needs desktop file-system access and is not available on this device.");
                    return;
                }

                MakeButton.IsEnabled = false;
                RebuildButton.IsEnabled = false;
                StatusText.Text = R._("Analyzing…");

                // Progress<T> is constructed here on the UI thread, so it captures the UI
                // SynchronizationContext and already marshals Report() callbacks back onto
                // it — set the text directly. Wrapping in an extra Dispatcher.UIThread.Post
                // would add a second async hop that could land a late progress update AFTER
                // the final result status set synchronously below, overwriting it.
                var progress = new Progress<string>(msg => StatusText.Text = msg);

                // The analysis + report write are Core/headless; run them off the UI thread so
                // a large ROM does not freeze the window (the operation can take a while).
                var result = await Task.Run(() => _vm.MakeRebuild(original, rebuildAddress, output, progress));

                StatusText.Text = result switch
                {
                    ToolROMRebuildViewModel.MakeResult.Ok => R._("ROMRebuild report created. {0}", _vm.LastMessage),
                    ToolROMRebuildViewModel.MakeResult.NoRom => R._("No ROM is loaded."),
                    ToolROMRebuildViewModel.MakeResult.OriginalMissing => R._("Please select a valid original (unmodified) ROM."),
                    ToolROMRebuildViewModel.MakeResult.OriginalUnreadable => R._("The original ROM could not be read."),
                    ToolROMRebuildViewModel.MakeResult.OriginalNotMatching => R._("The selected ROM is not the unmodified original for this game (CRC32 mismatch). Select the official clean ROM."),
                    ToolROMRebuildViewModel.MakeResult.BadAddress => R._("The rebuild address is invalid."),
                    _ => R._("Failed to create the ROMRebuild report. {0}", _vm.LastMessage),
                };

                if (result == ToolROMRebuildViewModel.MakeResult.Ok)
                    RevealInExplorer(output);
            }
            catch (Exception ex)
            {
                Log.Error("ToolROMRebuildView.Make_Click failed: " + ex);
                StatusText.Text = R._("Failed to create the ROMRebuild report.");
            }
            finally
            {
                MakeButton.IsEnabled = true;
                RebuildButton.IsEnabled = true;
            }
        }

        static bool TryParseHex(string? text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        static void RevealInExplorer(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                }
                else
                {
                    string dir = Path.GetDirectoryName(path) ?? "";
                    if (!string.IsNullOrEmpty(dir))
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }
            catch { /* reveal is best-effort */ }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
