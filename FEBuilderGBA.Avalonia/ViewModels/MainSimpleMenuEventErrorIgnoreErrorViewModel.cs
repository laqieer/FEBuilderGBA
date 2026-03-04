namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MainSimpleMenuEventErrorIgnoreErrorViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _errorText = "";
        string _comment = "";
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string ErrorText { get => _errorText; set => SetField(ref _errorText, value); }
        public string Comment { get => _comment; set => SetField(ref _comment, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void SetError(string errorAddress, string errorMessage)
        {
            ErrorText = $"0x{errorAddress}: {errorMessage}";
        }

        public string GetComment()
        {
            return string.IsNullOrWhiteSpace(Comment) ? " " : Comment;
        }
    }
}
