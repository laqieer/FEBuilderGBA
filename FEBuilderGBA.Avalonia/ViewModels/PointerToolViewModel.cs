using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _addressInput = string.Empty;
        string _searchResults = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AddressInput { get => _addressInput; set => SetField(ref _addressInput, value); }
        public string SearchResults { get => _searchResults; set => SetField(ref _searchResults, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
