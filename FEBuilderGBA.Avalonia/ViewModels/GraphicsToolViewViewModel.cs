using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Tool browser. Enter addresses and click Draw to view tiles.";
        string _imageAddressText = string.Empty;
        string _paletteAddressText = string.Empty;
        string _tsaAddressText = string.Empty;
        int _tsaTypeIndex;
        string _image2AddressText = string.Empty;
        bool _isImage2Join;
        bool _isCompressedPalette;
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
        /// <summary>ROM address of the TSA data (hex string). Only consumed when
        /// <see cref="TsaTypeIndex"/> &gt; 0.</summary>
        public string TsaAddressText { get => _tsaAddressText; set => SetField(ref _tsaAddressText, value); }
        /// <summary>
        /// WinForms <c>TSAOption.SelectedIndex</c> value (0 = None/plain tiles,
        /// 1 = Compressed, 2 = Compressed Header, 3 = Raw Header, 4 = Raw).
        /// When &gt; 0 the preview routes through the TSA-composited path.
        /// </summary>
        public int TsaTypeIndex
        {
            get => _tsaTypeIndex;
            set
            {
                if (SetField(ref _tsaTypeIndex, value))
                    OnPropertyChanged(nameof(TsaModeActive));
            }
        }
        /// <summary>
        /// True when a TSA type other than None is selected. The View binds
        /// <c>Is4bppCheck</c>/<c>CompressedCheck</c> enablement to its inverse —
        /// the TSA path is fixed 4bpp + LZ77 image.
        /// </summary>
        public bool TsaModeActive => TsaTypeIndex > 0;
        /// <summary>
        /// ROM address of a SECOND image to join after the first (hex string)
        /// — WF "Image 2" (#1074). Only consumed when <see cref="IsImage2Join"/>
        /// is set AND a TSA type other than None is selected. Empty / 0 ⇒ no join
        /// (single image).
        /// </summary>
        public string Image2AddressText { get => _image2AddressText; set => SetField(ref _image2AddressText, value); }
        /// <summary>
        /// True when the second image (<see cref="Image2AddressText"/>) is joined
        /// after the first (WF <c>ImageOption == 2</c>, order image ++ image2,
        /// #1074). Set by <c>Jump(...)</c> from <c>imageType == 2</c>.
        /// </summary>
        public bool IsImage2Join { get => _isImage2Join; set => SetField(ref _isImage2Join, value); }
        /// <summary>
        /// True when the palette block is an LZ77-compressed stream (WF
        /// <c>PaletteOption == 1</c>, #1074). Set by <c>Jump(...)</c> from
        /// <c>paletteType == 1</c>. Forwarded to
        /// <see cref="ImageTSAEditorCore.TryRenderMainImage"/>.
        /// </summary>
        public bool IsCompressedPalette { get => _isCompressedPalette; set => SetField(ref _isCompressedPalette, value); }
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

            // --- TSA-composited preview path (#1030 / #1074) -----------------
            // When a TSA type other than None is selected AND a TSA address is
            // entered, route the preview through the EXISTING
            // ImageTSAEditorCore.TryRenderMainImage (no new render logic). The
            // TSA path is FIXED 4bpp + LZ77 image; the View disables
            // Is4bpp/IsCompressed in this mode. The TSA-type -> flags mapping is
            // the SAME one the TSA Editor button path uses (MapTsaType), so
            // existing Jump callers render correctly. #1074 adds the optional
            // image2-join (IsImage2Join + Image2AddressText) and compressed
            // paletteType (IsCompressedPalette) carried in from Jump.
            uint tsaParsed = ParseHex(TsaAddressText);
            if (TsaTypeIndex > 0 && tsaParsed != 0)
            {
                ROM rom = CoreState.ROM;
                uint tsaOff = U.toOffset(tsaParsed);     // GBA pointer -> ROM offset
                uint imgOff = U.toOffset(imageAddr);     // normalize image too
                uint palOff = U.toOffset(paletteAddr);   // normalize palette too
                var (isLZ77TSA, isHeaderTSA) = MapTsaType(TsaTypeIndex);

                // #1074: optional 2nd-image join. Only resolve image2 when the
                // join flag is set (Jump maps imageType==2); a missing/empty
                // address parses to 0 -> single image (Core treats 0 as no-op).
                uint image2Off = IsImage2Join ? U.toOffset(ParseHex(Image2AddressText)) : 0u;

                int w8 = TileCountX;
                int h8 = TileCountY;
                // Mirror ImageTSAEditorViewModel.Init's header-TSA min-dimension
                // clamp (Width8 = Math.Max(256/8, w); Height8 = Math.Max(160/8, h)).
                if (isHeaderTSA) { w8 = Math.Max(32, w8); h8 = Math.Max(20, h8); }

                IImage? tsaImage = ImageTSAEditorCore.TryRenderMainImage(
                    rom, (uint)w8, (uint)h8, imgOff, isHeaderTSA, isLZ77TSA, tsaOff, palOff,
                    image2Off, IsCompressedPalette);

                if (tsaImage == null)
                {
                    StatusMessage = $"Error: Could not render TSA-composited image at image 0x{imgOff:X08}, tsa 0x{tsaOff:X08}.";
                    CurrentImage = null;
                    ImageInfo = string.Empty;
                    return null;
                }

                CurrentImage = tsaImage;
                string image2Tag = (IsImage2Join && image2Off != 0) ? " · +image2" : "";
                string palTag = IsCompressedPalette ? " · LZ77 palette" : "";
                ImageInfo = $"TSA · 4bpp · LZ77 image{image2Tag}{palTag} ({w8}x{h8} tiles)";
                StatusMessage = $"Loaded TSA-composited {tsaImage.Width}x{tsaImage.Height} image.";
                return tsaImage;
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

        /// <summary>
        /// Map a WinForms <c>TSAOption.SelectedIndex</c> value to the
        /// <c>(isLZ77TSA, isHeaderTSA)</c> flag pair consumed by
        /// <see cref="ImageTSAEditorCore.TryRenderMainImage"/>. This is the SAME
        /// mapping the TSA Editor button path (<c>GraphicsToolView.TSAEditor_Click</c>)
        /// has always used, extracted here so the preview and the editor button
        /// share ONE source of truth:
        /// <list type="bullet">
        ///   <item>0 = None / plain tiles (no TSA composite).</item>
        ///   <item>1 = Compressed (LZ77).</item>
        ///   <item>2 = Compressed Header (LZ77 + header).</item>
        ///   <item>3 = Raw Header.</item>
        ///   <item>4 = Raw (else).</item>
        /// </list>
        /// </summary>
        internal static (bool isLZ77TSA, bool isHeaderTSA) MapTsaType(int tsaType)
        {
            switch (tsaType)
            {
                case 1: return (true, false);   // LZ77
                case 2: return (true, true);    // LZ77 + header
                case 3: return (false, true);   // raw header
                default: return (false, false); // 0 (None) / 4 (Raw) -> raw
            }
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
