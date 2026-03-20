using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Tool browser. Enter addresses and click Draw to view tiles.";
        string _imageAddressText = string.Empty;
        string _paletteAddressText = string.Empty;
        int _tileCountX = 8;
        int _tileCountY = 8;
        bool _is4bpp = true;
        bool _isCompressed;
        int _zoom = 1;
        IImage? _currentImage;
        string _imageInfo = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>ROM address of the image data (hex string).</summary>
        public string ImageAddressText { get => _imageAddressText; set => SetField(ref _imageAddressText, value); }
        /// <summary>ROM address of the palette data (hex string).</summary>
        public string PaletteAddressText { get => _paletteAddressText; set => SetField(ref _paletteAddressText, value); }
        /// <summary>Number of tiles horizontally.</summary>
        public int TileCountX { get => _tileCountX; set => SetField(ref _tileCountX, value); }
        /// <summary>Number of tiles vertically.</summary>
        public int TileCountY { get => _tileCountY; set => SetField(ref _tileCountY, value); }
        /// <summary>True for 4bpp, false for 8bpp.</summary>
        public bool Is4bpp { get => _is4bpp; set => SetField(ref _is4bpp, value); }
        /// <summary>Whether the tile data is LZ77 compressed.</summary>
        public bool IsCompressed { get => _isCompressed; set => SetField(ref _isCompressed, value); }
        /// <summary>Zoom level for display.</summary>
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }
        /// <summary>The currently rendered image (set after DrawTiles).</summary>
        public IImage? CurrentImage { get => _currentImage; set => SetField(ref _currentImage, value); }
        /// <summary>Info string about the current image.</summary>
        public string ImageInfo { get => _imageInfo; set => SetField(ref _imageInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Load and decode tiles from the ROM using ImageUtilCore.
        /// Returns the decoded IImage, or null on failure (with StatusMessage set).
        /// </summary>
        public IImage? DrawTiles()
        {
            if (CoreState.ROM == null)
            {
                StatusMessage = "Error: No ROM loaded.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            if (CoreState.ImageService == null)
            {
                StatusMessage = "Error: No image service available.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            uint imageAddr = ParseHex(ImageAddressText);
            uint paletteAddr = ParseHex(PaletteAddressText);

            if (paletteAddr == 0)
            {
                StatusMessage = "Error: Please enter a valid palette address.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            if (TileCountX <= 0 || TileCountY <= 0 || TileCountX > 64 || TileCountY > 64)
            {
                StatusMessage = "Error: Tile count must be between 1 and 64.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            // Auto-convert GBA pointer to ROM offset
            if (imageAddr >= 0x08000000 && imageAddr < 0x0A000000)
                imageAddr -= 0x08000000;
            if (paletteAddr >= 0x08000000 && paletteAddr < 0x0A000000)
                paletteAddr -= 0x08000000;

            int colorCount = Is4bpp ? 16 : 256;
            byte[]? palette = ImageUtilCore.GetPalette(paletteAddr, colorCount);
            if (palette == null)
            {
                StatusMessage = $"Error: Could not read palette at 0x{paletteAddr:X08}.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            IImage? image;
            if (Is4bpp)
                image = ImageUtilCore.LoadROMTiles4bpp(imageAddr, palette, TileCountX, TileCountY, IsCompressed);
            else
                image = ImageUtilCore.LoadROMTiles8bpp(imageAddr, palette, TileCountX, TileCountY, IsCompressed);

            if (image == null)
            {
                StatusMessage = $"Error: Could not decode tiles at 0x{imageAddr:X08}.";
                CurrentImage = null;
                ImageInfo = string.Empty;
                return null;
            }

            CurrentImage = image;
            int bpp = Is4bpp ? 4 : 8;
            string compressed = IsCompressed ? " (LZ77)" : "";
            ImageInfo = $"{image.Width}x{image.Height} pixels, {TileCountX}x{TileCountY} tiles, {bpp}bpp{compressed}";
            StatusMessage = $"Loaded {image.Width}x{image.Height} image from 0x{imageAddr + 0x08000000:X08}.";

            return image;
        }

        /// <summary>Parse a hex string like "0x80000", "80000", or "0x08080000".</summary>
        internal static uint ParseHex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            input = input.Trim();
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(2);

            if (uint.TryParse(input, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint result))
                return result;

            return 0;
        }
    }
}
