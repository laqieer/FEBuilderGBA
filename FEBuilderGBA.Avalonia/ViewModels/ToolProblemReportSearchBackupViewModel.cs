namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchBackupViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _backupFilename = "";
        bool _dialogConfirmed;
        string _messageText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Path to the selected backup file.
        /// WinForms: BackupFilename TextBoxEx.
        /// </summary>
        public string BackupFilename { get => _backupFilename; set => SetField(ref _backupFilename, value); }

        /// <summary>
        /// Whether the user confirmed the dialog (selected a file).
        /// Maps to WinForms DialogResult.OK.
        /// </summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        /// <summary>
        /// The informational message displayed at the top.
        /// WinForms: label1 - explains no backups found, ask user to locate one.
        /// </summary>
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        public void Initialize()
        {
            MessageText = "No past backups were found.\n" +
                "Is the backup saved under a different name?\n" +
                "If you have a problem-free working backup, please specify its path.";
            IsLoaded = true;
        }

        public string GetFilename() => BackupFilename;
    }
}
