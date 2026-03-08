using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorAppendPopupViewModel : ViewModelBase
    {
        bool _isLoaded;
        bool _confirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool Confirmed { get => _confirmed; set => SetField(ref _confirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
