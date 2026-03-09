namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorUnknownROMViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _selectedVersion = "NAZO";
        string _versionInfoText = "ROM version info:";
        string _detailMessage = "This ROM does not have a recognized version signature.\nIs this a valid GBA Fire Emblem ROM?\nIf so, please select the correct version below.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedVersion { get => _selectedVersion; set => SetField(ref _selectedVersion, value); }
        public string VersionInfoText { get => _versionInfoText; set => SetField(ref _versionInfoText, value); }
        public string DetailMessage { get => _detailMessage; set => SetField(ref _detailMessage, value); }

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
