using global::Avalonia;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Shared helper that converts an FEBuilderGBA <see cref="IImage"/>
    /// (Core indexed or RGBA pixel data) into an Avalonia <see cref="WriteableBitmap"/>.
    /// Used by both <see cref="GbaImageControl"/> and <see cref="IconPreviewControl"/>.
    /// </summary>
    internal static class IconBitmapBuilder
    {
        /// <summary>
        /// Build a premultiplied-alpha RGBA8888 WriteableBitmap from an IImage.
        /// Returns null when the supplied image is null.
        /// </summary>
        public static WriteableBitmap? FromImage(IImage? image)
        {
            if (image == null) return null;

            byte[] rgba = image.GetPixelData();
            int w = image.Width;
            int h = image.Height;
            if (w <= 0 || h <= 0) return null;

            // If indexed, the pixel data is palette indices — get RGBA instead.
            if (image.IsIndexed)
            {
                byte[] palette = image.GetPaletteRGBA();
                byte[] indexData = rgba;
                rgba = new byte[w * h * 4];
                if (palette.Length >= 4)
                {
                    for (int i = 0; i < w * h; i++)
                    {
                        int palIdx = indexData[i];
                        if (palIdx * 4 + 3 < palette.Length)
                        {
                            rgba[i * 4 + 0] = palette[palIdx * 4 + 0];
                            rgba[i * 4 + 1] = palette[palIdx * 4 + 1];
                            rgba[i * 4 + 2] = palette[palIdx * 4 + 2];
                            rgba[i * 4 + 3] = palIdx == 0 ? (byte)0 : palette[palIdx * 4 + 3];
                        }
                    }
                }
                // If palette is empty/too small, rgba stays all zeros — produces a transparent bitmap
                // rather than throwing, matching the original GbaImageControl behaviour.
            }

            return FromRgba(rgba, w, h);
        }

        /// <summary>
        /// Build a premultiplied-alpha RGBA8888 WriteableBitmap from raw RGBA bytes.
        /// Returns null if the data is too small or dimensions are invalid.
        /// </summary>
        public static WriteableBitmap? FromRgba(byte[] rgba, int width, int height)
        {
            if (rgba == null) return null;
            if (width <= 0 || height <= 0) return null;
            if (rgba.Length < width * height * 4) return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Premul);

            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    var ptr = (byte*)fb.Address;
                    int stride = fb.RowBytes;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 4;
                            int dstIdx = y * stride + x * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0)
                            {
                                // Premul: alpha=0 → all channels must be 0.
                                ptr[dstIdx] = 0;
                                ptr[dstIdx + 1] = 0;
                                ptr[dstIdx + 2] = 0;
                                ptr[dstIdx + 3] = 0;
                            }
                            else
                            {
                                ptr[dstIdx] = rgba[srcIdx];
                                ptr[dstIdx + 1] = rgba[srcIdx + 1];
                                ptr[dstIdx + 2] = rgba[srcIdx + 2];
                                ptr[dstIdx + 3] = a;
                            }
                        }
                    }
                }
            }

            return bitmap;
        }
    }
}
