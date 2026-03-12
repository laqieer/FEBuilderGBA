using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Represents one patch entry for UI display, wrapping PatchMetadataCore.PatchInfo.</summary>
    public class PatchEntry
    {
        public string Name { get; set; } = "";
        public string DirectoryPath { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Type { get; set; } = "";
        public PatchMetadataCore.PatchStatus Status { get; set; } = PatchMetadataCore.PatchStatus.Unknown;
        public string PatchFilePath { get; set; } = "";
        public int DependencyCount { get; set; }
        public int UnsatisfiedDependencyCount { get; set; }
        public List<PatchMetadataCore.PatchDependency> UnsatisfiedDependencies { get; set; } = new();

        public string StatusText => Status switch
        {
            PatchMetadataCore.PatchStatus.Installed => "Installed",
            PatchMetadataCore.PatchStatus.NotInstalled => "Available",
            _ => "Unknown"
        };

        /// <summary>True when this patch has unmet dependencies.</summary>
        public bool HasUnmetDependencies => UnsatisfiedDependencyCount > 0;

        /// <summary>Human-readable summary of unmet dependencies.</summary>
        public string DependencyWarning
        {
            get
            {
                if (UnsatisfiedDependencyCount == 0) return "";
                var parts = new List<string>();
                foreach (var dep in UnsatisfiedDependencies)
                {
                    if (!string.IsNullOrEmpty(dep.Comment))
                        parts.Add(dep.Comment);
                    else
                        parts.Add($"Condition: {dep.Condition}");
                }
                return string.Join("\n", parts);
            }
        }

        /// <summary>Create from core PatchInfo.</summary>
        public static PatchEntry FromPatchInfo(PatchMetadataCore.PatchInfo info)
        {
            return new PatchEntry
            {
                Name = info.Name,
                DirectoryPath = info.DirectoryPath,
                Description = info.Description,
                Author = info.Author,
                Tags = info.Tags,
                Type = info.Type,
                Status = info.Status,
                PatchFilePath = info.PatchFilePath,
                DependencyCount = info.DependencyCount,
                UnsatisfiedDependencyCount = info.UnsatisfiedDependencyCount,
                UnsatisfiedDependencies = info.UnsatisfiedDependencies,
            };
        }
    }

    public class PatchManagerViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _filterText = "";
        PatchEntry? _selectedPatch;
        int _totalCount;
        int _installedCount;
        string _statusMessage = "";

        readonly List<PatchEntry> _allPatches = new();
        readonly ObservableCollection<PatchEntry> _filteredPatches = new();

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetField(ref _filterText, value))
                    ApplyFilter();
            }
        }

        public PatchEntry? SelectedPatch
        {
            get => _selectedPatch;
            set
            {
                if (SetField(ref _selectedPatch, value))
                {
                    OnPropertyChanged(nameof(CanInstall));
                    OnPropertyChanged(nameof(CanUninstall));
                }
            }
        }

        public int TotalCount { get => _totalCount; set => SetField(ref _totalCount, value); }
        public int InstalledCount { get => _installedCount; set => SetField(ref _installedCount, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>True when a patch is selected and not already installed.</summary>
        public bool CanInstall =>
            _selectedPatch != null &&
            _selectedPatch.Status != PatchMetadataCore.PatchStatus.Installed &&
            !string.IsNullOrEmpty(_selectedPatch.PatchFilePath) &&
            (_selectedPatch.Type == "BIN" || string.IsNullOrEmpty(_selectedPatch.Type));

        /// <summary>True when a selected patch is installed and has a backup file for restore.</summary>
        public bool CanUninstall =>
            _selectedPatch != null &&
            _selectedPatch.Status == PatchMetadataCore.PatchStatus.Installed &&
            !string.IsNullOrEmpty(_selectedPatch.PatchFilePath) &&
            PatchMetadataCore.HasBackup(_selectedPatch.PatchFilePath);

        public ObservableCollection<PatchEntry> FilteredPatches => _filteredPatches;

        /// <summary>
        /// Load the list of patches from config/patch2/{version}/.
        /// </summary>
        public void LoadPatchList()
        {
            _allPatches.Clear();
            _filteredPatches.Clear();

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                IsLoaded = true;
                return;
            }

            string version = rom.RomInfo.VersionToFilename;
            string patchDir = ResolvePatchDirectory(version);
            string lang = PatchMetadataCore.GetLanguageSuffix();

            var infos = PatchMetadataCore.EnumeratePatches(patchDir, rom, lang);
            foreach (var info in infos)
                _allPatches.Add(PatchEntry.FromPatchInfo(info));

            TotalCount = _allPatches.Count;
            InstalledCount = _allPatches.Count(p => p.Status == PatchMetadataCore.PatchStatus.Installed);

            ApplyFilter();
            IsLoaded = true;
        }

        /// <summary>
        /// Check dependencies for the currently selected patch.
        /// Returns a non-empty warning string if dependencies are unmet, or empty if all OK.
        /// </summary>
        public string CheckSelectedPatchDependencies()
        {
            if (_selectedPatch == null) return "";

            ROM rom = CoreState.ROM;
            if (rom == null) return "";

            string lang = PatchMetadataCore.GetLanguageSuffix();
            var missing = PatchMetadataCore.CheckDependencies(rom, _selectedPatch.PatchFilePath, lang);
            if (missing.Count == 0) return "";

            var parts = new List<string>();
            parts.Add($"This patch has {missing.Count} unmet dependency condition(s):");
            foreach (var dep in missing)
            {
                if (!string.IsNullOrEmpty(dep.Comment))
                    parts.Add("  - " + dep.Comment);
                else
                    parts.Add("  - Condition not met: " + dep.Condition);
            }
            parts.Add("");
            parts.Add("Install the required patches first, or proceed at your own risk.");
            return string.Join("\n", parts);
        }

        /// <summary>
        /// Install the currently selected patch. Returns the result message.
        /// Set forceIgnoreDependencies to true to skip dependency checks.
        /// </summary>
        public string InstallPatch(bool forceIgnoreDependencies = false)
        {
            if (_selectedPatch == null)
                return "No patch selected.";

            ROM rom = CoreState.ROM;
            if (rom == null)
                return "No ROM loaded.";

            // Check dependencies unless forced
            if (!forceIgnoreDependencies)
            {
                string depWarning = CheckSelectedPatchDependencies();
                if (!string.IsNullOrEmpty(depWarning))
                {
                    StatusMessage = depWarning;
                    return depWarning;
                }
            }

            Undo? undo = CoreState.Undo;
            Undo.UndoData? undoData = null;
            if (undo != null)
                undoData = undo.NewUndoData("PatchInstall", _selectedPatch.Name);

            var result = PatchMetadataCore.ApplyPatch(rom, _selectedPatch.PatchFilePath, undoData);

            if (result.Success)
            {
                if (undo != null && undoData != null)
                    undo.Push(undoData);

                // Refresh the status of this patch
                RefreshSelectedPatchStatus();
                StatusMessage = result.Message;
            }
            else
            {
                // Rollback on failure
                if (undo != null && undoData != null)
                    undo.Rollback(undoData);
                StatusMessage = "Install failed: " + result.Message;
            }

            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            return StatusMessage;
        }

        /// <summary>
        /// Uninstall the currently selected patch by restoring original bytes from backup.
        /// </summary>
        public string UninstallPatch()
        {
            if (_selectedPatch == null)
                return "No patch selected.";

            ROM rom = CoreState.ROM;
            if (rom == null)
                return "No ROM loaded.";

            var result = PatchMetadataCore.UninstallPatch(rom, _selectedPatch.PatchFilePath);

            if (result.Success)
            {
                RefreshSelectedPatchStatus();
                StatusMessage = result.Message;
            }
            else
            {
                StatusMessage = "Uninstall failed: " + result.Message;
            }

            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            return StatusMessage;
        }

        /// <summary>Re-check installation status of the selected patch and update counts.</summary>
        void RefreshSelectedPatchStatus()
        {
            if (_selectedPatch == null) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            string lang = PatchMetadataCore.GetLanguageSuffix();
            var refreshed = PatchMetadataCore.ParsePatchFile(
                _selectedPatch.PatchFilePath,
                Path.GetFileName(_selectedPatch.DirectoryPath),
                rom, lang);

            _selectedPatch.Status = refreshed.Status;

            // Update the corresponding entry in _allPatches
            var match = _allPatches.FirstOrDefault(p => p.PatchFilePath == _selectedPatch.PatchFilePath);
            if (match != null)
                match.Status = refreshed.Status;

            InstalledCount = _allPatches.Count(p => p.Status == PatchMetadataCore.PatchStatus.Installed);
        }

        void ApplyFilter()
        {
            _filteredPatches.Clear();
            string filter = _filterText.Trim();
            foreach (var p in _allPatches)
            {
                if (string.IsNullOrEmpty(filter) ||
                    p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Tags.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Author.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    _filteredPatches.Add(p);
                }
            }
        }

        static string ResolvePatchDirectory(string version)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            string path = Path.Combine(exeDir, "config", "patch2", version);
            if (Directory.Exists(path)) return path;

            path = Path.Combine(Directory.GetCurrentDirectory(), "config", "patch2", version);
            if (Directory.Exists(path)) return path;

            // Development: find repo root
            string dir = exeDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                {
                    path = Path.Combine(dir, "config", "patch2", version);
                    if (Directory.Exists(path)) return path;
                    break;
                }
                string parent = Path.GetDirectoryName(dir) ?? "";
                if (parent == dir) break;
                dir = parent;
            }

            return Path.Combine(exeDir, "config", "patch2", version);
        }
    }
}
