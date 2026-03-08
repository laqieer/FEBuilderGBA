using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolChangeProjectnameViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentName = string.Empty;
        string _newName = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string CurrentName { get => _currentName; set => SetField(ref _currentName, value); }
        public string NewName { get => _newName; set => SetField(ref _newName, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
