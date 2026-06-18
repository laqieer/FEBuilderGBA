using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolROMRebuildView : TranslatedWindow, IEditorView
    {
        readonly ToolROMRebuildViewModel _vm = new();

        public string ViewTitle => "ROM Rebuild Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolROMRebuildView()
        {
            InitializeComponent();
            IntroText.Text = R._("Analyze the modified ROM against the clean original and write a .rebuild report listing the modified regions and reusable free space. The full defragment (compacting Make/Apply with auto-reopen) is not yet available in this build.");
            Opened += (_, _) =>
            {
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
            };
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
                string? path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                    OriginalRomTextBox.Text = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolROMRebuildView.Select_Click failed: " + ex);
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

                if (!TryParseHex(RebuildAddressTextBox.Text, out uint rebuildAddress))
                {
                    StatusText.Text = R._("The rebuild address is not a valid hexadecimal number.");
                    return;
                }

                // WF CheckRebuildAddress: hard-fail on misaligned/unsafe, warn (Yes/No) when below extends.
                var check = _vm.ValidateRebuildAddress(rebuildAddress);
                if (check == ToolROMRebuildViewModel.AddressCheck.NotAligned)
                {
                    StatusText.Text = R._("This address ({0}) cannot be used: it is not a multiple of 4.", "0x" + string.Format("{0:X8}", rebuildAddress));
                    return;
                }
                if (check == ToolROMRebuildViewModel.AddressCheck.Unsafe)
                {
                    StatusText.Text = R._("This address ({0}) cannot be used: the address range is dangerous.", "0x" + string.Format("{0:X8}", rebuildAddress));
                    return;
                }
                if (check == ToolROMRebuildViewModel.AddressCheck.BelowExtends)
                {
                    var dr = await MessageBoxWindow.Show(this,
                        R._("Rebuilding an address ({0}) below the extended region is dangerous. Continue anyway?", "0x" + string.Format("{0:X8}", rebuildAddress)),
                        ViewTitle, MessageBoxMode.YesNo);
                    if (dr != MessageBoxResult.Yes)
                        return;
                }

                string suggested = ToolROMRebuildViewModel.SuggestedName(DateTime.Now.ToString("yyyyMMddHHmmss"));
                string? output = await FileDialogHelper.SaveFile(this,
                    R._("Save ROMRebuild report"), R._("ROMRebuild report"), "*.rebuild", suggested);
                if (string.IsNullOrEmpty(output))
                    return;   // user cancelled

                MakeButton.IsEnabled = false;
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
