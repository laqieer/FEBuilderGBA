namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolEmulatorSetupMessageViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _useInitWizardResult = "";
        string _messageText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Action result: "wizard" (use init wizard), "manual" (open options).
        /// Maps to WinForms UseInitWizardButton / UseOptionManualButton2 clicks.
        /// </summary>
        public string UseInitWizardResult { get => _useInitWizardResult; set => SetField(ref _useInitWizardResult, value); }

        /// <summary>
        /// The message text displayed in the dialog.
        /// WinForms: label1 - "No emulator is configured. Please configure..."
        /// </summary>
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        public void Initialize()
        {
            MessageText = "No emulator is configured.\nPlease configure the emulator to be used for test play.";
            IsLoaded = true;
        }
    }
}
