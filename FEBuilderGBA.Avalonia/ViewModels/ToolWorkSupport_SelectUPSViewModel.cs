namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupport_SelectUPSViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalFilename = "";
        string _upsFilename = "";
        bool _dialogConfirmed;
        string _instructionText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Path to the vanilla (unmodified) ROM file.
        /// WinForms: OrignalFilename TextBoxEx.
        /// </summary>
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }

        /// <summary>
        /// Path to the UPS patch file to be applied.
        /// </summary>
        public string UpsFilename { get => _upsFilename; set => SetField(ref _upsFilename, value); }

        /// <summary>
        /// Whether the user confirmed the dialog (clicked Apply UPS Patch).
        /// Maps to WinForms DialogResult after ApplyUPSPatchButton_Click.
        /// </summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        /// <summary>
        /// Instruction text at the top of the dialog.
        /// WinForms: label1 - "Select an unmodified ROM to open the UPS patch".
        /// </summary>
        public string InstructionText { get => _instructionText; set => SetField(ref _instructionText, value); }

        public void Initialize()
        {
            InstructionText = "Select an unmodified ROM to open the UPS patch.";
            IsLoaded = true;
        }

        public void OpenUPS(string upsFilename)
        {
            UpsFilename = upsFilename;
        }

        public string GetOriginalFilename() => OriginalFilename;
    }
}
