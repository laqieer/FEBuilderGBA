namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolThreeMargeCloseAlertViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "cancel";
        string _alertMessage = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Dialog result: "cancel" (continue merging), "no" (abort merge, cancel all), "yes" (force close with current results).
        /// Maps to WinForms DialogResult.Cancel / No / Yes.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// The warning message displayed in the dialog.
        /// WinForms: labelEx1 - explains that closing will leave partial merge state.
        /// </summary>
        public string AlertMessage { get => _alertMessage; set => SetField(ref _alertMessage, value); }

        public void Initialize()
        {
            AlertMessage = "Do you want to close the comparison tool?\n" +
                "Currently, only some changes have been applied.\n" +
                "Do you really want to close this tool now?\n\n" +
                "If you want to abort the merge, please cancel all changes and exit.";
            IsLoaded = true;
        }
    }
}
