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
    /// <c>.report.7z</c> archive (log + per-ROM etc config + any sibling .ups) is
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

            HeaderLabel.Text = R._("Problem Reporter");
            HintLabel.Text = R._("Describe the problem, then create a report to attach when you ask for help. The report includes a diagnostic log and your editor settings (no save data is collected).");
            ProblemLabel.Text = R._("Problem description");
            CreateButton.Content = R._("Create Report");
            AboutLabel.Text = R._("About report.7z:");
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

                string suggested = "REPORT." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".report.7z";
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
                CreateButton.IsEnabled = false;
                StatusLabel.Text = R._("Creating report...");

                string err = await System.Threading.Tasks.Task.Run(() => _vm.CreateReport(path));

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
