namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorUnknownROMViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _selectedVersion = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedVersion { get => _selectedVersion; set => SetField(ref _selectedVersion, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
