namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorUnknownROMViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _selectedVersion = "NAZO";
        string _versionInfoText = "ROM version info:";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedVersion { get => _selectedVersion; set => SetField(ref _selectedVersion, value); }
        public string VersionInfoText { get => _versionInfoText; set => SetField(ref _versionInfoText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void Init(string version)
        {
            VersionInfoText = "ROM version info: " + version;
        }
    }
}
