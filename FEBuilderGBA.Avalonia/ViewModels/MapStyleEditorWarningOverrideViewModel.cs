using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorWarningOverrideViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _warningMessage = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string WarningMessage { get => _warningMessage; set => SetField(ref _warningMessage, value); }

        public void Initialize()
        {
            if (string.IsNullOrEmpty(WarningMessage))
                WarningMessage = "Overriding map style data may cause visual artifacts. Are you sure?";
            IsLoaded = true;
        }
    }
}
