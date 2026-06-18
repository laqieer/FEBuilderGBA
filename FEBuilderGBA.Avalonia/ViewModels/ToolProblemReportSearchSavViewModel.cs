using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchSavViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _savFilename = "";
        bool _dialogConfirmed;
        string _messageText = "";
        string _errorMessage = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Path to the selected SAV file.
        /// WinForms: SavFilename TextBoxEx.
        /// </summary>
        public string SavFilename { get => _savFilename; set => SetField(ref _savFilename, value); }

        /// <summary>
        /// Whether the user confirmed the dialog (selected a file).
        /// Maps to WinForms DialogResult.OK.
        /// </summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        /// <summary>
        /// The informational message displayed at the top.
        /// WinForms: label1 - explains SAV file is needed for problem reproduction.
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
            MessageText = "No SAV file was found.\n" +
                "A SAV file is needed to reliably reproduce the problem.\n" +
                "If you have a matching SAV file, please specify its path.";
            IsLoaded = true;
        }

        /// <summary>
        /// Validate the entered path on OK: must be non-empty AND an existing file.
        /// Sets <see cref="ErrorMessage"/> (so the dialog stays open) and returns false
        /// when invalid; clears the error and returns true when valid.
        /// </summary>
        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(SavFilename))
            {
                ErrorMessage = R._("Please specify a save file path.");
                return false;
            }
            if (!File.Exists(SavFilename))
            {
                ErrorMessage = R._("The specified file does not exist.");
                return false;
            }
            ErrorMessage = "";
            return true;
        }

        public string GetFilename() => SavFilename;
    }
}
