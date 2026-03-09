using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteChangeColorsViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Palette Color Editor allows editing individual colors in a 16-color GBA palette.\nSelect a palette slot to modify its RGB values.";
        int _selectedColorIndex;
        int _newColorR;
        int _newColorG;
        int _newColorB;
        string _oldColorInfo = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Index of the currently selected palette color (0-15).</summary>
        public int SelectedColorIndex { get => _selectedColorIndex; set => SetField(ref _selectedColorIndex, value); }
        /// <summary>New red component (0-31 for GBA 5-bit color).</summary>
        public int NewColorR { get => _newColorR; set => SetField(ref _newColorR, value); }
        /// <summary>New green component (0-31 for GBA 5-bit color).</summary>
        public int NewColorG { get => _newColorG; set => SetField(ref _newColorG, value); }
        /// <summary>New blue component (0-31 for GBA 5-bit color).</summary>
        public int NewColorB { get => _newColorB; set => SetField(ref _newColorB, value); }
        /// <summary>Display string for the old color value (hex).</summary>
        public string OldColorInfo { get => _oldColorInfo; set => SetField(ref _oldColorInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
