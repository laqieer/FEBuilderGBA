using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorWarningOverrideViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _warningMessage = string.Empty;
        bool _userConfirmed;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Warning message describing what will be overridden.</summary>
        public string WarningMessage { get => _warningMessage; set => SetField(ref _warningMessage, value); }
        /// <summary>True if the user confirmed the override.</summary>
        public bool UserConfirmed { get => _userConfirmed; set => SetField(ref _userConfirmed, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            if (string.IsNullOrEmpty(WarningMessage))
                WarningMessage = "Overriding map style data may cause visual artifacts. Are you sure?";
            IsLoaded = true;
        }
    }
}
