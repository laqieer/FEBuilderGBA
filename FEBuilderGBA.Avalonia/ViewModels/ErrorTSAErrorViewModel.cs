namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorTSAErrorViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _tsaErrorDetails = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string TsaErrorDetails { get => _tsaErrorDetails; set => SetField(ref _tsaErrorDetails, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
