using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteChangeColorsViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Palette Color Editor allows editing individual colors in a 16-color GBA palette.\nSelect a palette slot to modify its RGB values.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
