namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupport_UpdateQuestionDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _versionInfo = "";
        string _dialogResult = "cancel";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string VersionInfo { get => _versionInfo; set => SetField(ref _versionInfo, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void SetVersion(string version)
        {
            VersionInfo = $"The ROM is already up to date (version: {version}). Would you like to force update?";
        }
    }
}
