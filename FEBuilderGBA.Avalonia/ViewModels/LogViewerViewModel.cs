using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// #1467: Log Viewer view-model. Surfaces the real application log
    /// (Core <see cref="FEBuilderGBA.Log"/>) instead of the previous dummy
    /// address-list placeholder. Read-only — no ROM writes.
    /// Mirrors WinForms <c>LogForm</c>: SyncLog → LogToString, Save, Copy,
    /// Open-log-folder, and live refresh on <c>Log.UpdateEvent</c>.
    /// </summary>
    public class LogViewerViewModel : ViewModelBase
    {
        string _logText = string.Empty;
        bool _isLoaded;

        /// <summary>The most recent log lines (Core <c>Log.LogToString()</c>).</summary>
        public string LogText { get => _logText; set => SetField(ref _logText, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>On-disk log file path (<c>config/log/log.txt</c>).</summary>
        public string LogFilePath => Log.GetLogFilePath();

        /// <summary>Folder that contains the log file (for "Open log folder").</summary>
        public string LogDirectory
        {
            get
            {
                try { return Path.GetDirectoryName(LogFilePath) ?? "."; }
                catch (Exception) { return "."; }
            }
        }

        /// <summary>
        /// Flush the in-memory buffer to disk and re-read the latest lines into
        /// <see cref="LogText"/>. Safe to call repeatedly (the WinForms LogForm
        /// reloads the whole list on every <c>UpdateEvent</c>).
        /// </summary>
        public void Refresh()
        {
            Log.SyncLog();
            LogText = Log.LogToString();
            IsLoaded = true;
        }

        /// <summary>Current full log text for the clipboard (Core <c>Log.LogToString()</c>).</summary>
        public string GetClipboardText()
        {
            Log.SyncLog();
            return Log.LogToString();
        }

        /// <summary>
        /// Write the current log text to <paramref name="path"/>
        /// (mirrors WinForms <c>Log.ToFile</c> minus the explorer-select).
        /// </summary>
        public void SaveToFile(string path)
        {
            Log.SyncLog();
            File.WriteAllText(path, Log.LogToString());
        }
    }
}
