namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolAutomaticRecoveryROMHeaderViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _recoveryStatus = "Ready to scan ROM header for issues.";
        string _originalFilename = "";
        string _explanationText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Recovery status message.
        /// </summary>
        public string RecoveryStatus { get => _recoveryStatus; set => SetField(ref _recoveryStatus, value); }

        /// <summary>
        /// Path to the unmodified (vanilla) ROM file used for header recovery.
        /// WinForms: OrignalFilename TextBoxEx.
        /// </summary>
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }

        /// <summary>
        /// Explanation text about what the recovery does.
        /// WinForms: label1 - "Recovers corrupted ROM header 0x0 - 0x100 automatically."
        /// </summary>
        public string ExplanationText { get => _explanationText; set => SetField(ref _explanationText, value); }

        public void Initialize()
        {
            ExplanationText = "Recovers corrupted ROM header (0x0 - 0x100) automatically.";
            IsLoaded = true;
        }
    }
}
