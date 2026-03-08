using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerNewPLISTPopupViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _plistId;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint PlistId { get => _plistId; set => SetField(ref _plistId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
