using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-agnostic image representation.
    /// Wraps platform-specific bitmap types (System.Drawing.Bitmap, SKBitmap, etc.).
    /// </summary>
    public interface IImage : IDisposable
    {
        int Width { get; }
        int Height { get; }

        /// <summary>Whether this image uses indexed (palettized) pixel format.</summary>
        bool IsIndexed { get; }

        /// <summary>
        /// Raw pixel data as a flat byte array.
        /// For indexed images: one byte per pixel (palette index).
        /// For RGBA images: 4 bytes per pixel (R, G, B, A).
        /// </summary>
        byte[] GetPixelData();

        /// <summary>Set raw pixel data. Format must match the image's pixel mode.</summary>
        void SetPixelData(byte[] data);

        /// <summary>
        /// Palette as GBA-format bytes (2 bytes per color, 15-bit BGR 5-5-5, little-endian).
        /// Returns empty array for non-indexed images.
        /// </summary>
        byte[] GetPaletteGBA();

        /// <summary>Set palette from GBA-format bytes.</summary>
        void SetPaletteGBA(byte[] gbaPalette);

        /// <summary>
        /// Palette as RGBA byte array (4 bytes per color: R, G, B, A).
        /// Returns empty array for non-indexed images.
        /// </summary>
        byte[] GetPaletteRGBA();

        /// <summary>Save this image to a file (format determined by extension: .png, .bmp).</summary>
        void Save(string filePath);

        /// <summary>Encode this image to PNG bytes in memory.</summary>
        byte[] EncodePng();
    }

    /// <summary>
    /// Platform-agnostic image factory and conversion service.
    /// Provides GBA-specific image encoding/decoding without System.Drawing dependency.
    /// </summary>
    public interface IImageService
    {
        // ---- Factory ----

        /// <summary>Create a blank RGBA image.</summary>
        IImage CreateImage(int width, int height);

        /// <summary>Create an indexed (palettized) image with the given GBA palette.</summary>
        IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount);

        /// <summary>Load an image from file (PNG, BMP).</summary>
        IImage LoadImage(string filePath);

        /// <summary>Load an image from PNG byte data.</summary>
        IImage LoadImageFromBytes(byte[] pngData);

        // ---- GBA Color Conversion ----

        /// <summary>
        /// Convert a GBA 15-bit BGR color (5-5-5) to RGBA bytes.
        /// GBA format: bit 0-4 = R, bit 5-9 = G, bit 10-14 = B.
        /// </summary>
        void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b);

        /// <summary>Convert RGBA to GBA 15-bit BGR color.</summary>
        ushort RGBAToGBAColor(byte r, byte g, byte b);

        // ---- GBA Tile Decoding ----

        /// <summary>
        /// Decode 4bpp tile data into an indexed image.
        /// Each byte = 2 pixels (low nibble first). Tiles are 8x8 pixels.
        /// </summary>
        IImage Decode4bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette);

        /// <summary>
        /// Decode 8bpp tile data into an indexed image.
        /// Each byte = 1 pixel (palette index). Tiles are 8x8 pixels.
        /// </summary>
        IImage Decode8bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette);

        /// <summary>
        /// Decode 8bpp linear (non-tiled) data into an indexed image.
        /// </summary>
        IImage Decode8bppLinear(byte[] data, int offset, int width, int height, byte[] gbaPalette);

        // ---- GBA Tile Encoding ----

        /// <summary>
        /// Encode an indexed image into 4bpp tile data.
        /// Returns tile data bytes (2 pixels per byte, 8x8 tiles).
        /// </summary>
        byte[] Encode4bppTiles(IImage image);

        /// <summary>
        /// Encode an indexed image into 8bpp tile data.
        /// Returns tile data bytes (1 pixel per byte, 8x8 tiles).
        /// </summary>
        byte[] Encode8bppTiles(IImage image);

        // ---- Palette Operations ----

        /// <summary>
        /// Convert a GBA palette byte array to an RGBA palette byte array.
        /// Input: 2 bytes per color (GBA 15-bit BGR). Output: 4 bytes per color (RGBA).
        /// </summary>
        byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount);

        /// <summary>
        /// Convert an RGBA palette byte array to GBA palette bytes.
        /// Input: 4 bytes per color (RGBA). Output: 2 bytes per color (GBA 15-bit BGR).
        /// </summary>
        byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount);
    }
}
