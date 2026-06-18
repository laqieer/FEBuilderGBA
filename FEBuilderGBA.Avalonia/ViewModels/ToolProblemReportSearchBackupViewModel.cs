using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchBackupViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _backupFilename = "";
        bool _dialogConfirmed;
        string _messageText = "";
        string _errorMessage = "";

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

        /// <summary>Inline validation message shown when OK is pressed with an invalid path.</summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set { SetField(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
        }

        /// <summary>True when <see cref="ErrorMessage"/> is non-empty (drives the error label visibility).</summary>
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public void Initialize()
        {
            MessageText = "No past backups were found.\n" +
                "Is the backup saved under a different name?\n" +
                "If you have a problem-free working backup, please specify its path.";
            IsLoaded = true;
        }

        /// <summary>
        /// Validate the entered path on OK: must be non-empty AND an existing file.
        /// Sets <see cref="ErrorMessage"/> (so the dialog stays open) and returns false
        /// when invalid; clears the error and returns true when valid.
        /// </summary>
        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(BackupFilename))
            {
                ErrorMessage = R._("Please specify a backup ROM path.");
                return false;
            }
            if (!File.Exists(BackupFilename))
            {
                ErrorMessage = R._("The specified file does not exist.");
                return false;
            }
            ErrorMessage = "";
            return true;
        }

        public string GetFilename() => BackupFilename;
    }
}
