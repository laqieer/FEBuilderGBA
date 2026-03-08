using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class RAMRewriteToolMAPViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _address = string.Empty;
        string _value = string.Empty;
        string _mapId = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Address { get => _address; set => SetField(ref _address, value); }
        public string Value { get => _value; set => SetField(ref _value, value); }
        public string MapId { get => _mapId; set => SetField(ref _mapId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
