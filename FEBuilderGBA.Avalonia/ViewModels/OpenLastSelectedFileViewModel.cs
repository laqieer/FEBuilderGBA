using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Last-Used-File tool — parity with WinForms <c>OpenLastSelectedFileForm</c> (#1195).
    /// Shows the last-selected file (the currently-loaded ROM — the file the user opened most
    /// recently) and offers Open-file / Open-containing-folder actions. WinForms reads
    /// <c>Program.LastSelectedFilename.GetLastFilename()</c>, which is seeded with the opened ROM.
    /// </summary>
    public class OpenLastSelectedFileViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _lastFile = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The last-selected file path (empty when no ROM is loaded).</summary>
        public string LastFile { get => _lastFile; set => SetField(ref _lastFile, value); }
        /// <summary>True when a last file exists on disk (Open / Open-folder are actionable).</summary>
        public bool HasFile => !string.IsNullOrEmpty(_lastFile) && File.Exists(_lastFile);

        /// <summary>The last-selected file = the currently-loaded ROM path (the most recently opened file).</summary>
        public string GetLastFile() => CoreState.ROM?.Filename ?? "";

        public void Load()
        {
            IsLoaded = true;
            LastFile = GetLastFile();
        }
    }
}
