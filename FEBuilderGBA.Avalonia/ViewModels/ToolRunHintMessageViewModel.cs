namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolRunHintMessageViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _detailText = "";
        bool _doNotShowAgain;
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DetailText { get => _detailText; set => SetField(ref _detailText, value); }
        public bool DoNotShowAgain { get => _doNotShowAgain; set => SetField(ref _doNotShowAgain, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            DetailText = "This will start a test run.\n\n" +
                "The emulator will launch with the current ROM.\n" +
                "You can test your changes in real time.\n\n" +
                "Make sure you have saved any pending changes before testing.";
            IsLoaded = true;
        }
    }
}
