using System;
using System.Diagnostics;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Problem-Report tool (#1193, ports WinForms <c>ToolProblemReportForm</c>).
    /// READ-ONLY w.r.t. the ROM: the user types a problem description and a
    /// <c>.report.7z</c> / <c>.report.zip</c> archive (log + per-ROM etc config + any
    /// sibling .ups; <c>.7z</c> when native 7-zip32.dll is present, else <c>.zip</c>) is
    /// written via <see cref="ProblemReportCore.CreateReport"/>. No ROM mutation,
    /// no undo scope. The emulator save/backup search is deferred (follow-up issue).
    /// </summary>
    public partial class ToolProblemReportView : TranslatedWindow, IEditorView
    {
        readonly ToolProblemReportViewModel _vm = new();

        public string ViewTitle => "Problem Reporter";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolProblemReportView()
        {
            InitializeComponent();
            DataContext = _vm;

            // Static labels (header / hint / button / about) are English literals in
            // the XAML so TranslatedWindow's ViewTranslationHelper stores them as the
            // re-translation keys and re-localizes on CoreState.LanguageChanged. Only
            // the runtime URL value is assigned here.
            AboutUrlButton.Content = ToolProblemReportViewModel.Report7zUrl;
        }

        async void Create_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasRom)
                {
                    StatusLabel.Text = R._("No ROM loaded.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(_vm.ProblemText))
                {
                    StatusLabel.Text = R._("Please describe the problem.");
                    return;
                }

                var storage = GetTopLevel(this)?.StorageProvider;
                if (storage == null) return;

                // ArchSevenZip.Compress writes a real .7z only when native 7-zip32.dll
                // is present; otherwise it falls back to SharpCompress and writes a .zip.
                // Suggest the extension matching what will actually be produced.
                string reportExt = ArchSevenZip.IsNative7ZipAvailable() ? ".report.7z" : ".report.zip";
                string suggested = "REPORT." + DateTime.Now.ToString("yyyyMMddHHmmss") + reportExt;
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Save report"),
                    SuggestedFileName = suggested,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(R._("Report files")) { Patterns = new[] { "*.report.7z", "*.report.zip" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } }
                    }
                });
                if (file == null) return;

                string path = file.Path.LocalPath;

                // Mirror the WinForms show-on-empty flow (CollectSaveData L350 /
                // CollectOldUPSs L263): only surface a picker when auto-discovery
                // found nothing — do NOT pop both modals on every report.

                // (a) No emulator save next to the ROM -> ask the user to point at one.
                string savFilePath = null;
                if (!_vm.HasAutoSaveData())
                {
                    var savPicker = new ToolProblemReportSearchSavView();
                    await savPicker.ShowDialog(this);
                    if (savPicker.Confirmed)
                    {
                        savFilePath = savPicker.PickedFilename;
                    }
                }

                // (b) No sibling .ups next to the ROM -> offer the backup picker so a
                //     clean / old ROM can seed a fresh UPS delta.
                string cleanRomPath = null;
                if (!_vm.HasSiblingUps())
                {
                    var backupPicker = new ToolProblemReportSearchBackupView();
                    await backupPicker.ShowDialog(this);
                    if (backupPicker.Confirmed)
                    {
                        cleanRomPath = backupPicker.PickedFilename;
                    }
                }

                CreateButton.IsEnabled = false;
                StatusLabel.Text = R._("Creating report...");

                string err = await System.Threading.Tasks.Task.Run(
                    () => _vm.CreateReport(path, cleanRomPath, savFilePath));

                if (!string.IsNullOrEmpty(err))
                {
                    StatusLabel.Text = err;
                    await MessageBoxWindow.Show(this, err, R._("Error"), MessageBoxMode.Ok);
                    return;
                }

                StatusLabel.Text = R._("Report created:") + " " + path;
                await MessageBoxWindow.Show(this,
                    R._("Report created:") + "\r\n" + path, R._("Problem Reporter"), MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportView.Create_Click failed: " + ex.ToString());
                StatusLabel.Text = R._("Could not create the report.");
            }
            finally
            {
                CreateButton.IsEnabled = true;
            }
        }

        void AboutUrl_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ToolProblemReportViewModel.Report7zUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportView.AboutUrl_Click failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) { }

        // Not a selectable list; nothing to select. The UI renders on Opened for
        // screenshots/headless capture.
        public void SelectFirstItem() { }
    }
}
