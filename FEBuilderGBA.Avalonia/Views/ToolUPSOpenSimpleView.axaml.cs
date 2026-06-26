using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Apply-UPS dialog (#1460) — Avalonia port of WinForms <c>ToolUPSOpenSimpleForm</c>.
    /// Pick a distributed <c>.ups</c> patch + a clean original ROM (auto-detected by the
    /// UPS's recorded source CRC32), Apply via Core <see cref="UPSUtilCore.ApplyUPS(byte[],byte[],out string)"/>,
    /// then load the patched ROM into the main window (optionally saving it as <c>.gba</c>).
    /// </summary>
    public partial class ToolUPSOpenSimpleView : TranslatedWindow, IEditorView
    {
        readonly ToolUPSOpenSimpleViewModel _vm = new();

        public string ViewTitle => "UPS Patch Applier";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolUPSOpenSimpleView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.IsLoaded = true;
        }

        async void SelectUps_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenPatchFile(this);
                if (string.IsNullOrEmpty(path))
                    return;
                UpsTextBox.Text = path;

                // Best-effort auto-fill the matching clean original ROM by CRC32 (WF parity).
                if (string.IsNullOrWhiteSpace(OriginalRomTextBox.Text))
                {
                    string found = _vm.FindOriginalForUps(path);
                    if (!string.IsNullOrEmpty(found))
                        OriginalRomTextBox.Text = found;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolUPSOpenSimpleView.SelectUps_Click failed: " + ex);
            }
        }

        async void SelectOriginal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                    OriginalRomTextBox.Text = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolUPSOpenSimpleView.SelectOriginal_Click failed: " + ex);
            }
        }

        async void Apply_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string ups = UpsTextBox.Text ?? "";
                string original = OriginalRomTextBox.Text ?? "";

                if (string.IsNullOrWhiteSpace(ups) || !File.Exists(ups))
                {
                    StatusText.Text = R._("Please select a valid UPS patch file.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                {
                    StatusText.Text = R._("Please select a valid original (unmodified) ROM.");
                    return;
                }

                var result = _vm.ApplyUps(ups, original, out byte[] patched, out string warning);
                bool applied = result == ToolUPSOpenSimpleViewModel.ApplyResult.Ok
                            || result == ToolUPSOpenSimpleViewModel.ApplyResult.OkWithWarning;
                if (!applied || patched == null)
                {
                    StatusText.Text = result switch
                    {
                        ToolUPSOpenSimpleViewModel.ApplyResult.UpsMissing => R._("Please select a valid UPS patch file."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.UpsInvalid => R._("The selected file is not a valid UPS patch."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.OriginalMissing => R._("Please select a valid original (unmodified) ROM."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.OriginalUnreadable => R._("The original ROM could not be read."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.OriginalNotClean => R._("The selected ROM is not an unmodified original (CRC32 mismatch). Select the official clean ROM the patch was made for."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.SourceCrcMismatch => R._("This UPS patch was not made for the selected ROM (source CRC32 mismatch)."),
                        ToolUPSOpenSimpleViewModel.ApplyResult.ApplyFailed => R._("Failed to apply the UPS patch."),
                        _ => R._("Failed to apply the UPS patch."),
                    };
                    return;
                }

                // Non-fatal CRC warning: WinForms applies anyway after a Yes/No prompt.
                // Ask the user before committing the patched ROM (Copilot review finding #2).
                if (result == ToolUPSOpenSimpleViewModel.ApplyResult.OkWithWarning)
                {
                    var answer = await MessageBoxWindow.Show(this,
                        R._("The UPS patch applied with a warning:") + "\r\n" + warning + "\r\n\r\n"
                            + R._("Apply it anyway?"),
                        R._("UPS Patch Applier"), MessageBoxMode.YesNo);
                    if (answer != MessageBoxResult.Yes)
                    {
                        StatusText.Text = R._("Cancelled.");
                        return;
                    }
                }

                // Where to write the patched ROM. WF: save .gba (then open) OR keep in memory
                // (LoadVirtualROM). Avalonia has no in-memory virtual-ROM load, so the
                // "don't save" path writes a temp .gba and opens that — same patched-ROM end
                // state. Parity note (Copilot review finding #4): LoadRomFile(temp) records the
                // temp path in recent/last-ROM/autosave, unlike WF LoadVirtualROM's
                // "<ups>.VIRTUAL" marker; saving (default, checked) avoids the temp entirely and
                // is the recommended path, so this is an explicit, documented parity break.
                string savePath;
                bool isTemp = false;
                if (SaveAsGbaCheck.IsChecked == true)
                {
                    string suggested = Path.GetFileNameWithoutExtension(ups) + ".gba";
                    string? chosen = await FileDialogHelper.SaveRomFile(this, suggested);
                    if (string.IsNullOrEmpty(chosen))
                        return;   // user cancelled
                    savePath = chosen;
                }
                else
                {
                    savePath = Path.Combine(Path.GetTempPath(),
                        "feb_ups_applied_" + Guid.NewGuid().ToString("N") + ".gba");
                    isTemp = true;
                }

                try
                {
                    File.WriteAllBytes(savePath, patched);
                }
                catch (Exception ex)
                {
                    StatusText.Text = R._("Failed to save the patched ROM:") + " " + ex.Message;
                    return;
                }

                // Load the patched ROM into the main window (re-inits caches/encoders for its
                // version — implicit equivalent of WF ReOpenMainForm). Same seam used by
                // ToolWorkSupportView after applying an update UPS.
                if (WindowManager.Instance.MainWindow is MainWindow mw)
                {
                    bool ok = mw.LoadRomFile(savePath);
                    if (!ok)
                    {
                        StatusText.Text = R._("Failed to load the patched ROM.");
                        return;
                    }
                }

                StatusText.Text = isTemp
                    ? R._("UPS patch applied and loaded.")
                    : R._("UPS patch applied: {0}", savePath);

                // The patched ROM is now active; close the dialog (WF closes on success).
                Close();
            }
            catch (Exception ex)
            {
                Log.Error("ToolUPSOpenSimpleView.Apply_Click failed: " + ex);
                StatusText.Text = R._("Failed to apply the UPS patch.");
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
