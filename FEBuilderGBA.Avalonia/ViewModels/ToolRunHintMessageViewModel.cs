namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolRunHintMessageViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _headerText = "";
        string _detailText = "";
        bool _doNotShowAgain;
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Bold header text at the top of the dialog.
        /// WinForms: label1 (Font=Bold 11pt) - "Starting test execution now."
        /// </summary>
        public string HeaderText { get => _headerText; set => SetField(ref _headerText, value); }

        /// <summary>
        /// Detail text in the read-only multiline textbox.
        /// WinForms: Detail TextBoxEx (890x332, multiline).
        /// </summary>
        public string DetailText { get => _detailText; set => SetField(ref _detailText, value); }

        /// <summary>
        /// Whether to suppress this dialog in future.
        /// WinForms: DoNotShowThisMessageAgain CheckBox. Saves "RunTestMessage"=1 to config.
        /// </summary>
        public bool DoNotShowAgain { get => _doNotShowAgain; set => SetField(ref _doNotShowAgain, value); }

        /// <summary>
        /// Whether the user confirmed (clicked Start).
        /// </summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            HeaderText = "Starting test execution now.";
            DetailText = "This will start a test run.\n\n" +
                "The emulator will launch with the current ROM.\n" +
                "You can test your changes in real time.\n\n" +
                "Make sure you have saved any pending changes before testing.";
            IsLoaded = true;
        }
    }
}
