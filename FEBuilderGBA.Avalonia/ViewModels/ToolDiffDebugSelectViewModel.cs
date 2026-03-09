using System;
using System.Collections.ObjectModel;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolDiffDebugSelectViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _prefixFilter = "";
        string _originalFilename = "";
        string _selectedFileInfo = "";
        string _instructionsText = "";
        string _dialogResult = "";
        int _selectedIndex = -1;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Prefix filter for searching backup files.
        /// WinForms: PrefixTextBox - filters BackupList by file name prefix.
        /// </summary>
        public string PrefixFilter { get => _prefixFilter; set { SetField(ref _prefixFilter, value); Reload(); } }

        /// <summary>
        /// Path to the vanilla (unmodified) ROM file.
        /// WinForms: OrignalFilename TextBoxEx.
        /// </summary>
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }

        /// <summary>
        /// Information about the currently selected backup file.
        /// WinForms: ThisFileInfo TextBoxEx (readonly multiline).
        /// </summary>
        public string SelectedFileInfo { get => _selectedFileInfo; set => SetField(ref _selectedFileInfo, value); }

        /// <summary>
        /// Instructions text explaining how to use the tool.
        /// WinForms: label3 (loaded from resources).
        /// </summary>
        public string InstructionsText { get => _instructionsText; set => SetField(ref _instructionsText, value); }

        /// <summary>
        /// Dialog result: "testplay" or "compare".
        /// WinForms: TestPlayButton / SelectROMButton clicks.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// Selected index in the backup list.
        /// WinForms: BackupList ListBoxEx.
        /// </summary>
        public int SelectedIndex { get => _selectedIndex; set { SetField(ref _selectedIndex, value); UpdateSelectedInfo(); } }

        /// <summary>
        /// List of backup ROM file paths (newest first).
        /// WinForms: BackupList ListBoxEx.
        /// </summary>
        public ObservableCollection<string> BackupList { get; } = new();

        public void Initialize()
        {
            try
            {
                LoadBackups();
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectViewModel", ex.ToString());
            }
            InstructionsText = "How to use the comparison debug tool:\n\n" +
                "1. Select a backup ROM from the list on the left\n" +
                "2. Double-click or press 'Test play' to test it in the emulator\n" +
                "3. Find the last stable (working) backup\n" +
                "4. Press 'Use this ROM as the last stable baseline' to get the differences\n" +
                "5. The tool will show what changed between the stable backup and the current ROM";
            IsLoaded = true;
        }

        void LoadBackups()
        {
            BackupList.Clear();

            if (CoreState.ROM == null) return;

            string romFilename = CoreState.ROM.Filename;
            if (string.IsNullOrEmpty(romFilename)) return;

            string dir = Path.GetDirectoryName(romFilename) ?? "";
            string backupDir = Path.Combine(dir, "backup");

            if (!Directory.Exists(backupDir))
            {
                SelectedFileInfo = "No backup directory found.";
                return;
            }

            var files = Directory.GetFiles(backupDir, "*.gba*");
            Array.Sort(files);
            Array.Reverse(files); // newest first

            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                if (!string.IsNullOrEmpty(PrefixFilter) &&
                    !name.StartsWith(PrefixFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                BackupList.Add(f);
            }

            if (BackupList.Count == 0)
                SelectedFileInfo = "No backup files found.";
        }

        void UpdateSelectedInfo()
        {
            if (_selectedIndex < 0 || _selectedIndex >= BackupList.Count)
            {
                SelectedFileInfo = "";
                return;
            }

            string path = BackupList[_selectedIndex];
            try
            {
                var fi = new FileInfo(path);
                SelectedFileInfo = $"File: {fi.Name}\nDate: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}\nSize: {fi.Length:N0} bytes";
            }
            catch
            {
                SelectedFileInfo = path;
            }
        }

        public void Reload()
        {
            LoadBackups();
        }

        /// <summary>
        /// Gets the full path of the currently selected backup, or null if none selected.
        /// </summary>
        public string? GetSelectedBackupPath()
        {
            if (_selectedIndex >= 0 && _selectedIndex < BackupList.Count)
                return BackupList[_selectedIndex];
            return null;
        }
    }
}
