using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WelcomeViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _versionInfo = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Version string displayed on the Welcome screen.</summary>
        public string VersionInfo { get => _versionInfo; set => SetField(ref _versionInfo, value); }

        public void Initialize()
        {
            try
            {
#if DEBUG
                VersionInfo = "Version: Debug Build";
#else
                VersionInfo = $"Version: {U.getAppVersion()}";
#endif
            }
            catch
            {
                VersionInfo = "";
            }

            IsLoaded = true;
        }
    }
}
