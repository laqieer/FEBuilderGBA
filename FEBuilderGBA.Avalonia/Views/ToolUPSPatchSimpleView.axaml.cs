using global::Avalonia;
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
    public partial class ToolUPSPatchSimpleView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolUPSPatchSimpleViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "UPS Patch Creator";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("UPS Patch Creator", 760, 320);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolUPSPatchSimpleView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                _vm.IsLoaded = true;
                // Best-effort auto-fill the clean original ROM (WinForms parity).
                string found = _vm.FindOriginal();
                if (!string.IsNullOrEmpty(found))
                    OriginalRomTextBox.Text = found;
            }
        }

        async void Select_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
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
                // #1639: the .ups patch is a single-file output → pick the handle
                // and write through the SAF bridge so Android content:// targets work.
                var file = await FileDialogHelper.SaveUpsFilePick(TopLevel.GetTopLevel(this) as Window, suggested);
                if (file == null)
                    return;   // user cancelled

                ToolUPSPatchSimpleViewModel.MakeResult result = ToolUPSPatchSimpleViewModel.MakeResult.Ok;
                string? output = await FileDialogHelper.WriteViaAsync(file, p => { result = _vm.MakeUps(original, p); });
                if (output == null) return;
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
