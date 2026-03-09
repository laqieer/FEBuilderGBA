namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupport_UpdateQuestionDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _versionInfo = "";
        string _dialogResult = "cancel";
        string _version = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// The formatted version info message displayed in the dialog.
        /// WinForms: labelEx1 (Font=Bold 11pt) - "Current version is the latest. No update needed."
        /// </summary>
        public string VersionInfo { get => _versionInfo; set => SetField(ref _versionInfo, value); }

        /// <summary>
        /// Dialog result: "cancel" (close) or "retry" (force update).
        /// Maps to WinForms DialogResult.Cancel / Retry.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// The raw version string passed via SetVersion().
        /// </summary>
        public string Version { get => _version; set => SetField(ref _version, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void SetVersion(string version)
        {
            Version = version;
            VersionInfo = $"The current version is the latest. No update needed. version: {version}";
        }
    }
}
