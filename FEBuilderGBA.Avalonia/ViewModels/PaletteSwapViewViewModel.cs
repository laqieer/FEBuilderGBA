using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteSwapViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Palette Swap swaps palette assignments between entries.\nSelect source and destination palette slots to exchange their color data.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
