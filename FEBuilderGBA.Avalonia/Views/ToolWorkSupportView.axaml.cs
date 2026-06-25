using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Single-work Work Support window (#1454). The "Update" button drives the
    /// loaded ROM hack's OWN update pipeline — read CHECK_URL/UPDATE_URL from its
    /// <c>.updateinfo.txt</c>, scrape the work's release, download + extract, and
    /// apply the staged UPS against a user-selected vanilla ROM — exactly like
    /// WinForms <c>ToolWorkSupportForm</c>. It no longer queries the editor's own
    /// GitHub release. All orchestration lives in Core
    /// (<see cref="WorkSupportUpdateCheckCore"/> + <see cref="WorkSupportUpdateDownloadCore"/>);
    /// this view only supplies the host network/extract/ROM delegates and the UI.
    /// </summary>
    public partial class ToolWorkSupportView : TranslatedWindow, IEditorView
    {
        readonly ToolWorkSupportViewModel _vm = new();
        public string ViewTitle => "Work Support";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolWorkSupportView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void Update_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // ---- CheckReady (WF parity finding #1): no ROM / virtual / unsaved ----
                if (CoreState.ROM == null)
                {
                    _vm.AutoFeedbackStatus = R._("Load a ROM first.");
                    return;
                }
                if (CoreState.ROM.IsVirtualROM)
                {
                    _vm.AutoFeedbackStatus = R._("A virtual ROM cannot be updated.");
                    return;
                }
                if (!_vm.HasUpdateInfo)
                {
                    _vm.AutoFeedbackStatus = R._("No .updateinfo.txt found for this ROM.");
                    return;
                }
                if (CoreState.ROM.Modified)
                {
                    var go = await MessageBoxWindow.Show(this,
                        R._("Warning: unsaved changes are not stored in the ROM. Update anyway?"),
                        R._("Work Support"), MessageBoxMode.YesNo);
                    if (go != MessageBoxResult.Yes) return;
                }

                _vm.AutoFeedbackStatus = R._("Checking for updates...");

                // ---- CheckUpdate (Core) — off the UI thread (network-bound) ----
                WorkSupportUpdateCheckCore.UpdateResult ur = await Task.Run(() =>
                    _vm.CheckUpdate(url => U.HttpGet(url), HttpHeadLastModified, GetRomDateTime));

                if (ur == WorkSupportUpdateCheckCore.UpdateResult.Error)
                {
                    _vm.AutoFeedbackStatus = R._("Update check failed (missing CHECK_URL/CHECK_REGEX or unreachable).");
                    return;
                }
                if (ur == WorkSupportUpdateCheckCore.UpdateResult.Latest)
                {
                    // WF parity finding #2: offer a force-update via the question dialog.
                    var q = new ToolWorkSupport_UpdateQuestionDialogView();
                    q.SetVersion(_vm.Version);
                    string? choice = await q.ShowDialog<string?>(this);
                    if (choice != "retry")
                    {
                        _vm.AutoFeedbackStatus = R._("You are up to date.");
                        return;
                    }
                    // forced — fall through to the download/apply pipeline
                }

                await RunDownloadAndApply();
            }
            catch (Exception ex)
            {
                _vm.AutoFeedbackStatus = string.Format(R._("Update failed: {0}"), ex.Message);
                Log.Error("ToolWorkSupportView.Update", ex.ToString());
            }
        }

        async Task RunDownloadAndApply()
        {
            // ---- resolve the download URL from UPDATE_URL/UPDATE_REGEX ----
            _vm.AutoFeedbackStatus = R._("Resolving download URL...");
            WorkSupportUpdateDownloadCore.ResolveResult resolve = await Task.Run(() =>
                _vm.ResolveDownloadUrl(url => U.HttpGet(url)));
            if (resolve.Status != WorkSupportUpdateDownloadCore.ResolveStatus.Ok
                || string.IsNullOrEmpty(resolve.Url))
            {
                _vm.AutoFeedbackStatus = string.Format(
                    R._("Could not resolve the update URL ({0})."), resolve.Status);
                return;
            }

            // ---- download + stage (download/extract) ----
            _vm.AutoFeedbackStatus = R._("Downloading and extracting...");
            WorkSupportUpdateDownloadCore.StageResult stage = await Task.Run(() =>
                _vm.DownloadAndStage(resolve.Url, DownloadFile, ExtractArchive));
            if (stage.Status != WorkSupportUpdateDownloadCore.StageStatus.Ok)
            {
                _vm.AutoFeedbackStatus = string.Format(
                    R._("Download/extract failed ({0}). {1}"), stage.Status, stage.Error);
                return;
            }

            // ---- select the vanilla ROM (CRC32 auto-find inside the dialog) ----
            var sel = new ToolWorkSupport_SelectUPSView();
            sel.OpenUPS(stage.UpsFiles[0]);
            bool confirmed = await sel.ShowDialog<bool>(this);
            if (!confirmed)
            {
                _vm.AutoFeedbackStatus = R._("Update cancelled.");
                return;
            }
            string original = sel.SelectedOriginal;
            if (string.IsNullOrEmpty(original) || !File.Exists(original))
            {
                _vm.AutoFeedbackStatus = R._("No vanilla ROM selected.");
                return;
            }

            // ---- apply each staged UPS against the original (validate-all-before-write) ----
            _vm.AutoFeedbackStatus = R._("Applying UPS patch...");
            WorkSupportUpdateDownloadCore.ApplyResult apply = await Task.Run(() =>
                _vm.ApplyUps(stage.UpsFiles, original, ApplyOneUps));
            if (apply.Status != WorkSupportUpdateDownloadCore.ApplyStatus.Ok)
            {
                _vm.AutoFeedbackStatus = string.Format(
                    R._("Failed to apply UPS patch ({0}). {1}"), apply.Status, apply.Error);
                return;
            }

            // ---- surface non-fatal CRC warnings (WF parity finding #3) ----
            if (apply.Warnings.Count > 0)
            {
                await MessageBoxWindow.Show(this,
                    R._("The update was applied, but with warnings:") + "\r\n" +
                    string.Join("\r\n", apply.Warnings),
                    R._("Work Support"), MessageBoxMode.Ok);
            }

            // ---- reopen the patched ROM in the main window ----
            _vm.AutoFeedbackStatus = R._("Update complete. Reopening ROM...");
            string reopen = apply.SavedRoms.Count > 0 ? apply.SavedRoms[0] : original;
            if (WindowManager.Instance.MainWindow is MainWindow mw && File.Exists(reopen))
            {
                mw.LoadRomFile(reopen);
            }
            _vm.AutoFeedbackStatus = R._("Update complete.");
        }

        // ---- host delegates (network / archive / ROM) -------------------------

        /// <summary>Download a URL to a local file. Wraps Core <c>U.HttpDownloadFile</c>.</summary>
        static (bool ok, string error) DownloadFile(string url, string destPath)
        {
            bool ok = U.HttpDownloadFile(url, destPath, out string error);
            return (ok, error);
        }

        /// <summary>Extract an archive. Wraps Core <c>ArchSevenZip.Extract</c> (returns errorOrEmpty).</summary>
        static string ExtractArchive(string archivePath, string destDir)
        {
            return ArchSevenZip.Extract(archivePath, destDir, isHide: true);
        }

        /// <summary>
        /// Apply ONE UPS to the original bytes in memory. Maps <c>UPSUtilCore.ApplyUPS</c>
        /// semantics: null bytes ⇒ hard error (source CRC mismatch); non-null bytes with a
        /// non-empty message ⇒ a non-fatal CRC warning (patched bytes still produced).
        /// </summary>
        static (byte[]? bytes, string error, string warning) ApplyOneUps(byte[] original, string upsPath)
        {
            try
            {
                byte[] patch = File.ReadAllBytes(upsPath);
                byte[] result = UPSUtilCore.ApplyUPS(original, patch, out string msg);
                if (result == null)
                {
                    return (null, string.IsNullOrEmpty(msg) ? ("apply failed: " + upsPath) : msg, "");
                }
                // result produced, but msg may carry a patch/result CRC warning.
                return (result, "", msg ?? "");
            }
            catch (Exception ex)
            {
                return (null, ex.Message, "");
            }
        }

        void Community_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.CommunityUrl))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.CommunityUrl) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.Community", ex.ToString());
            }
        }

        void OpenInfo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_vm.InfoText) && System.IO.File.Exists(_vm.InfoText))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(_vm.InfoText) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportView.OpenInfo", ex.ToString());
            }
        }

        void ShowAllWorks_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ToolAllWorkSupportView>();
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        // One shared HttpClient for all HEAD probes (per .NET guidance) with a
        // bounded timeout so a slow/unreachable host cannot hang the update check.
        static readonly System.Net.Http.HttpClient s_httpClient = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };

        /// <summary>HTTP HEAD probe for a URL's Last-Modified header (null when absent/unreachable/timed out).</summary>
        static string? HttpHeadLastModified(string url)
        {
            try
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                using var resp = s_httpClient.Send(req);
                if (resp.Content.Headers.LastModified.HasValue)
                {
                    return resp.Content.Headers.LastModified.Value.ToString();
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Local ROM/UPS timestamp (the newer of the ROM file and its sibling
        /// <c>.ups</c>). Ports WF <c>ToolWorkSupportForm.GetROMDateTime</c>.
        /// </summary>
        static DateTime GetRomDateTime(string romFilename)
        {
            DateTime dt = File.GetLastWriteTime(romFilename);
            string ups = Path.ChangeExtension(romFilename, ".ups");
            if (File.Exists(ups))
            {
                DateTime upsDt = File.GetLastWriteTime(ups);
                if (upsDt > dt) dt = upsDt;
            }
            return dt;
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
