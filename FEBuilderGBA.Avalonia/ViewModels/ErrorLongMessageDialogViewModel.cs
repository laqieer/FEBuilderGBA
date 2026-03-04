namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorLongMessageDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _longMessage = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string LongMessage { get => _longMessage; set => SetField(ref _longMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
