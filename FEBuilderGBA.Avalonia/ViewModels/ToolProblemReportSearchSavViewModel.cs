namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchSavViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _savFilename = "";
        bool _dialogConfirmed;
        string _messageText = "";

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

        public void Initialize()
        {
            MessageText = "No SAV file was found.\n" +
                "A SAV file is needed to reliably reproduce the problem.\n" +
                "If you have a matching SAV file, please specify its path.";
            IsLoaded = true;
        }

        public string GetFilename() => SavFilename;
    }
}
