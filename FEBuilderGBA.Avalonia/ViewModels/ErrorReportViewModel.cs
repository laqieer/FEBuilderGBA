namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorReportViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _errorText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string ErrorText { get => _errorText; set => SetField(ref _errorText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
