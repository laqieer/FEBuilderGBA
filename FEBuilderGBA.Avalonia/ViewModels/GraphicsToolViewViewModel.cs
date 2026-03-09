using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Tool browser. Select images from the categorized list.\nCategories: Portraits, Battle Animations, Map Sprites, Icons, CGs, Title Screen, etc.";
        uint _imageAddress;
        uint _tsaAddress;
        uint _paletteAddress;
        int _paletteNumber;
        int _imageOption;
        int _tsaOption;
        int _picWidth = 256;
        int _picHeight = 160;
        int _zoom = 1;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>ROM address of the image data.</summary>
        public uint ImageAddress { get => _imageAddress; set => SetField(ref _imageAddress, value); }
        /// <summary>ROM address of the TSA (tile screen arrangement) data.</summary>
        public uint TsaAddress { get => _tsaAddress; set => SetField(ref _tsaAddress, value); }
        /// <summary>ROM address of the palette data.</summary>
        public uint PaletteAddress { get => _paletteAddress; set => SetField(ref _paletteAddress, value); }
        /// <summary>Palette slot number (0-15).</summary>
        public int PaletteNumber { get => _paletteNumber; set => SetField(ref _paletteNumber, value); }
        /// <summary>Image decoding option index.</summary>
        public int ImageOption { get => _imageOption; set => SetField(ref _imageOption, value); }
        /// <summary>TSA decoding option index.</summary>
        public int TsaOption { get => _tsaOption; set => SetField(ref _tsaOption, value); }
        /// <summary>Display width in pixels.</summary>
        public int PicWidth { get => _picWidth; set => SetField(ref _picWidth, value); }
        /// <summary>Display height in pixels.</summary>
        public int PicHeight { get => _picHeight; set => SetField(ref _picHeight, value); }
        /// <summary>Zoom level for display.</summary>
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
