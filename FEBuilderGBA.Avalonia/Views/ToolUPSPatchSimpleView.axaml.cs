using System;
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUPSPatchSimpleView : TranslatedWindow, IEditorView
    {
        readonly ToolUPSPatchSimpleViewModel _vm = new();

        public string ViewTitle => "UPS Patch Creator";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolUPSPatchSimpleView()
        {
            InitializeComponent();
            Opened += (_, _) =>
            {
                _vm.IsLoaded = true;
                // Best-effort auto-fill the clean original ROM (WinForms parity).
                string found = _vm.FindOriginal();
                if (!string.IsNullOrEmpty(found))
                    OriginalRomTextBox.Text = found;
            };
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
                Log.Error("ToolUPSPatchSimpleView.Select_Click failed: " + ex);
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

                string suggested = ToolUPSPatchSimpleViewModel.SuggestedName(DateTime.Now.ToString("yyyyMMddHHmmss"));
                string? output = await FileDialogHelper.SaveUpsFile(this, suggested);
                if (string.IsNullOrEmpty(output))
                    return;   // user cancelled

                var result = _vm.MakeUps(original, output);
                StatusText.Text = result switch
                {
                    ToolUPSPatchSimpleViewModel.MakeResult.Ok => R._("UPS patch created: {0}", output),
                    ToolUPSPatchSimpleViewModel.MakeResult.NoRom => R._("No ROM is loaded."),
                    ToolUPSPatchSimpleViewModel.MakeResult.OriginalMissing => R._("Please select a valid original (unmodified) ROM."),
                    ToolUPSPatchSimpleViewModel.MakeResult.OriginalUnreadable => R._("The original ROM could not be read."),
                    ToolUPSPatchSimpleViewModel.MakeResult.OriginalNotMatching => R._("The selected ROM is not the unmodified original for this game (CRC32 mismatch). Select the official clean ROM."),
                    _ => R._("Failed to create the UPS patch."),
                };
                if (result == ToolUPSPatchSimpleViewModel.MakeResult.Ok)
                    RevealInExplorer(output);
            }
            catch (Exception ex)
            {
                Log.Error("ToolUPSPatchSimpleView.Make_Click failed: " + ex);
            }
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
