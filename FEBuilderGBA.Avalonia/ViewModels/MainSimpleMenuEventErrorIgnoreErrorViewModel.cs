namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MainSimpleMenuEventErrorIgnoreErrorViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _errorText = "";
        string _comment = "";
        bool _dialogConfirmed;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Error address and message text (read-only display).</summary>
        public string ErrorText { get => _errorText; set => SetField(ref _errorText, value); }
        /// <summary>User comment explaining why the error is being ignored.</summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value); }
        /// <summary>True if the user clicked OK to confirm ignoring the error.</summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

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
