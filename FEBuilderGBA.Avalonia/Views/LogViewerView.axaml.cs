using System;
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// #1467: Real Log Viewer — surfaces the live application log
    /// (Core <see cref="FEBuilderGBA.Log"/>) instead of the previous dummy
    /// address-list placeholder. Mirrors WinForms <c>LogForm</c>:
    /// SyncLog → LogToString on load, refresh on <c>Log.UpdateEvent</c>, and a
    /// Save / Copy-to-clipboard / Open-log-folder / Refresh button row.
    /// Read-only — no ROM writes, no undo.
    /// </summary>
    public partial class LogViewerView : TranslatedWindow, IEditorView
    {
        readonly LogViewerViewModel _vm = new();
        EventHandler? _logUpdateHandler;
        bool _reloadPending;

        public string ViewTitle => "Log Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        /// <summary>Test/diagnostic accessor for the bound view-model.</summary>
        public ViewModelBase DataViewModel => _vm;

        public LogViewerView()
        {
            InitializeComponent();
            DataContext = _vm;

            RefreshButton.Click += OnRefresh;
            SaveButton.Click += OnSave;
            CopyButton.Click += OnCopy;
            OpenDirButton.Click += OnOpenDir;

            Opened += OnOpened;
            Closed += OnClosed;
        }

        void OnOpened(object? sender, EventArgs e)
        {
            ReloadLog();

            // Guard against a double Opened (e.g. re-shown window): unsubscribe
            // any prior handler before re-subscribing so we never leak/duplicate.
            if (_logUpdateHandler != null)
            {
                Log.UpdateEvent -= _logUpdateHandler;
            }

            // Live refresh: re-pull the log whenever a new message is logged.
            // Marshal to the UI thread (Log.UpdateEvent can fire from any thread)
            // and COALESCE — a burst of UpdateEvents (heavy logging) schedules at
            // most one pending reload so we don't flood the Dispatcher queue or
            // re-read the file once per message.
            _logUpdateHandler = (_, _) => ScheduleReload();
            Log.UpdateEvent += _logUpdateHandler;
        }

        void ScheduleReload()
        {
            if (_reloadPending) return;
            _reloadPending = true;
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _reloadPending = false;
                    ReloadLog();
                });
            }
            catch (Exception)
            {
                // Dispatcher gone during shutdown — clear the flag so a later
                // event can re-arm, and ignore.
                _reloadPending = false;
            }
        }

        void OnClosed(object? sender, EventArgs e)
        {
            if (_logUpdateHandler != null)
            {
                Log.UpdateEvent -= _logUpdateHandler;
                _logUpdateHandler = null;
            }
        }

        void ReloadLog()
        {
            try
            {
                _vm.Refresh();
            }
            catch (Exception ex)
            {
                Log.Error($"LogViewerView.ReloadLog failed: {ex}");
            }
        }

        void OnRefresh(object? sender, RoutedEventArgs e) => ReloadLog();

        async void OnSave(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.SaveFile(
                    this, R._("Save"), R._("TEXT"), "*.txt", "log.txt");
                if (string.IsNullOrEmpty(path)) return;
                _vm.SaveToFile(path);
                StatusLabel.Text = R._("Saved to {0}", path);
            }
            catch (Exception ex)
            {
                Log.Error($"LogViewerView.OnSave failed: {ex}");
                StatusLabel.Text = ex.Message;
            }
        }

        async void OnCopy(object? sender, RoutedEventArgs e)
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;
                await clipboard.SetTextAsync(_vm.GetClipboardText());
                StatusLabel.Text = R._("Copied to clipboard");
            }
            catch (Exception ex)
            {
                Log.Error($"LogViewerView.OnCopy failed: {ex}");
                StatusLabel.Text = ex.Message;
            }
        }

        void OnOpenDir(object? sender, RoutedEventArgs e)
        {
            try
            {
                string logFile = _vm.LogFilePath;
                string dir = _vm.LogDirectory;

                // Cross-platform: only Windows supports `explorer.exe /select,<file>`.
                // Elsewhere open the containing folder via the shell. Never throw.
                if (OperatingSystem.IsWindows() && File.Exists(logFile))
                {
                    var psi = new ProcessStartInfo("explorer.exe", $"/select,\"{logFile}\"")
                    { UseShellExecute = true };
                    Process.Start(psi);
                }
                else if (Directory.Exists(dir))
                {
                    var psi = new ProcessStartInfo(dir) { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    StatusLabel.Text = R._("Log folder not found") + ": " + dir;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LogViewerView.OnOpenDir failed: {ex}");
                StatusLabel.Text = ex.Message;
            }
        }

        // IEditorView — the Log Viewer is not an address-indexed editor.
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
