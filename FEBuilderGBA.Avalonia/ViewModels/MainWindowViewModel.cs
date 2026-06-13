using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Represents a single entry in the Recent Files list.
    /// </summary>
    public class RecentFileEntry : ViewModelBase
    {
        string _filePath = "";
        bool _exists = true;

        public string FilePath
        {
            get => _filePath;
            set { SetField(ref _filePath, value); OnPropertyChanged(nameof(DisplayName)); }
        }

        public bool Exists
        {
            get => _exists;
            set => SetField(ref _exists, value);
        }

        /// <summary>Short display name (filename only).</summary>
        public string DisplayName => string.IsNullOrEmpty(_filePath)
            ? "(empty)"
            : Path.GetFileName(_filePath);
    }

    public class MainWindowViewModel : ViewModelBase
    {
        public const int MaxRecentFiles = 10;
        public const string RecentFileKeyPrefix = "Recent_Rom_";

        bool _isRomLoaded;
        string _romVersion = "";
        string _romFilename = "";
        string _statusText = R._("No ROM loaded");
        string _filterText = "";
        long _romSize;
        long _estimatedFreeSpace;
        bool _hasUnsavedChanges;
        bool _isDecompMode;

        /// <summary>Observable list of recent files for the menu.</summary>
        public ObservableCollection<RecentFileEntry> RecentFiles { get; } = new();

        public bool IsRomLoaded
        {
            get => _isRomLoaded;
            set { SetField(ref _isRomLoaded, value); OnPropertyChanged(nameof(IsNotRomLoaded)); }
        }

        public bool IsNotRomLoaded => !_isRomLoaded;

        public string RomVersion
        {
            get => _romVersion;
            set => SetField(ref _romVersion, value);
        }

        public string RomFilename
        {
            get => _romFilename;
            set { SetField(ref _romFilename, value); OnPropertyChanged(nameof(WindowTitle)); }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { SetField(ref _hasUnsavedChanges, value); OnPropertyChanged(nameof(WindowTitle)); }
        }

        /// <summary>
        /// True when a decomp project is open (#1129 slice 1). Drives the toolbar
        /// "build preview" badge visibility. Mirrors <see cref="CoreState.IsDecompMode"/>.
        /// </summary>
        public bool IsDecompMode
        {
            get => _isDecompMode;
            set { SetField(ref _isDecompMode, value); OnPropertyChanged(nameof(DecompBadgeText)); }
        }

        /// <summary>
        /// True when a source-backed write (#1132) flagged the project for rebuild.
        /// Reads <see cref="CoreState"/> directly so it cannot go stale; the setter
        /// only nudges the badge to re-evaluate.
        /// </summary>
        public bool DecompNeedsRebuild
        {
            get => CoreState.DecompProject?.NeedsRebuild == true;
            set { OnPropertyChanged(nameof(DecompNeedsRebuild)); OnPropertyChanged(nameof(DecompBadgeText)); }
        }

        /// <summary>
        /// Toolbar badge text shown while a decomp project is open. Appends a
        /// "needs rebuild" hint after a source-backed write (#1132).
        /// </summary>
        public string DecompBadgeText =>
            CoreState.DecompProject?.NeedsRebuild == true
                ? R._("Source-backed project · ROM is a build preview") + R._(" · needs rebuild")
                : R._("Source-backed project · ROM is a build preview");

        /// <summary>Re-read decomp mode from CoreState. Call after ROM/project loads.</summary>
        public void RefreshDecompMode()
        {
            IsDecompMode = CoreState.IsDecompMode;
            OnPropertyChanged(nameof(DecompNeedsRebuild));
            OnPropertyChanged(nameof(DecompBadgeText));
        }

        /// <summary>
        /// Computed window title: "FEBuilderGBA - [filename] *" when dirty,
        /// "FEBuilderGBA - [filename]" when clean, "FEBuilderGBA" when no ROM loaded.
        /// </summary>
        public string WindowTitle
        {
            get
            {
                if (string.IsNullOrEmpty(_romFilename))
                    return "FEBuilderGBA";
                return _hasUnsavedChanges
                    ? $"FEBuilderGBA - {_romFilename} *"
                    : $"FEBuilderGBA - {_romFilename}";
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public string FilterText
        {
            get => _filterText;
            set => SetField(ref _filterText, value);
        }

        public long RomSize
        {
            get => _romSize;
            set => SetField(ref _romSize, value);
        }

        public long EstimatedFreeSpace
        {
            get => _estimatedFreeSpace;
            set => SetField(ref _estimatedFreeSpace, value);
        }

        public void UpdateFromRom()
        {
            if (CoreState.ROM != null)
            {
                IsRomLoaded = true;
                RomVersion = CoreState.ROM.RomInfo?.VersionToFilename ?? "Unknown";
                RomFilename = System.IO.Path.GetFileName(CoreState.ROM.Filename ?? "");
                RomSize = CoreState.ROM.Data?.Length ?? 0;
                EstimatedFreeSpace = EstimateFreeSpace(CoreState.ROM);
                StatusText = $"{RomFilename} | {RomVersion} | {RomSize:N0} " + R._("bytes") + $" | " + R._("Free:") + $" ~{EstimatedFreeSpace:N0} " + R._("bytes");
                HasUnsavedChanges = false;
                IsDecompMode = CoreState.IsDecompMode;
            }
            else
            {
                IsRomLoaded = false;
                RomVersion = "";
                RomFilename = "";
                RomSize = 0;
                EstimatedFreeSpace = 0;
                StatusText = R._("No ROM loaded");
                HasUnsavedChanges = false;
                IsDecompMode = false;
            }
        }

        /// <summary>
        /// Estimate free space by counting trailing 0x00 or 0xFF bytes from end of ROM.
        /// This is a rough estimate -- real free space analysis is more complex.
        /// </summary>
        static long EstimateFreeSpace(ROM rom)
        {
            if (rom?.Data == null || rom.Data.Length == 0) return 0;
            byte[] data = rom.Data;
            long count = 0;
            // Count trailing bytes that are 0x00 or 0xFF (typical padding)
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (data[i] == 0x00 || data[i] == 0xFF)
                    count++;
                else
                    break;
            }
            return count;
        }

        // ===== Recent Files Management =====

        /// <summary>
        /// Load recent files list from config into the observable collection.
        /// </summary>
        public void LoadRecentFiles()
        {
            RecentFiles.Clear();
            if (CoreState.Config == null) return;

            for (int i = 0; i < MaxRecentFiles; i++)
            {
                string path = CoreState.Config.at(RecentFileKeyPrefix + i, "");
                if (string.IsNullOrEmpty(path)) continue;

                RecentFiles.Add(new RecentFileEntry
                {
                    FilePath = path,
                    Exists = File.Exists(path)
                });
            }
        }

        /// <summary>
        /// Add a ROM path to the top of the recent files list, removing duplicates.
        /// Persists to config and refreshes the collection.
        /// </summary>
        public void AddRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Build list: new path first, then existing (without duplicates), capped at max
            var paths = new System.Collections.Generic.List<string> { path };
            foreach (var entry in RecentFiles)
            {
                if (!string.Equals(entry.FilePath, path, System.StringComparison.OrdinalIgnoreCase)
                    && paths.Count < MaxRecentFiles)
                {
                    paths.Add(entry.FilePath);
                }
            }

            // Persist to config
            if (CoreState.Config != null)
            {
                for (int i = 0; i < MaxRecentFiles; i++)
                {
                    string key = RecentFileKeyPrefix + i;
                    if (i < paths.Count)
                        CoreState.Config[key] = paths[i];
                    else if (CoreState.Config.ContainsKey(key))
                        CoreState.Config.Remove(key);
                }
                CoreState.Config.Save();
            }

            // Refresh the observable collection
            RecentFiles.Clear();
            foreach (string p in paths)
            {
                RecentFiles.Add(new RecentFileEntry
                {
                    FilePath = p,
                    Exists = File.Exists(p)
                });
            }
        }

        /// <summary>
        /// Refresh the Exists flag on all entries (e.g., when menu is opened).
        /// </summary>
        public void RefreshRecentFileExistence()
        {
            foreach (var entry in RecentFiles)
            {
                entry.Exists = File.Exists(entry.FilePath);
            }
        }
    }
}
