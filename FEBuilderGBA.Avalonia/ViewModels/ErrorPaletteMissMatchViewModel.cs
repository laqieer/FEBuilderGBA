namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorPaletteMissMatchViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _details = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Details { get => _details; set => SetField(ref _details, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
