using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Avalonia image import service. Wraps: file dialog → load image → quantize → return result.
    /// </summary>
    public static class ImageImportService
    {
        /// <summary>Result of loading and quantizing an image for import.</summary>
        public class LoadResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            /// <summary>Indexed pixel data (1 byte per pixel).</summary>
            public byte[] IndexedPixels { get; set; }
            /// <summary>GBA palette (2 bytes per color).</summary>
            public byte[] GBAPalette { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        /// <summary>
        /// Open file dialog, load image, validate dimensions, quantize to maxColors.
        /// Returns null if user cancels.
        /// </summary>
        public static async Task<LoadResult> LoadAndQuantize(Window owner, int expectedWidth, int expectedHeight,
            int maxColors = 16, bool strictSize = false)
        {
            string filePath = await FileDialogHelper.OpenImageFile(owner);
            if (string.IsNullOrEmpty(filePath))
                return null; // User cancelled

            return LoadAndQuantizeFromFile(filePath, expectedWidth, expectedHeight, maxColors, strictSize);
        }

        /// <summary>
        /// Load image from file path, validate dimensions, quantize.
        /// </summary>
        public static LoadResult LoadAndQuantizeFromFile(string filePath, int expectedWidth, int expectedHeight,
            int maxColors = 16, bool strictSize = false)
        {
            var result = new LoadResult();
            var imgService = CoreState.ImageService;
            if (imgService == null)
            {
                result.Error = "Image service not initialized";
                return result;
            }

            IImage image;
            try
            {
                image = imgService.LoadImage(filePath);
            }
            catch (Exception ex)
            {
                result.Error = $"Failed to load image: {ex.Message}";
                return result;
            }

            using (image)
            {
                if (strictSize && (image.Width != expectedWidth || image.Height != expectedHeight))
                {
                    result.Error = $"Image must be {expectedWidth}x{expectedHeight} pixels (got {image.Width}x{image.Height})";
                    return result;
                }

                if (image.Width % 8 != 0 || image.Height % 8 != 0)
                {
                    result.Error = $"Image dimensions must be multiples of 8 (got {image.Width}x{image.Height})";
                    return result;
                }

                // Get RGBA pixel data for quantization
                byte[] rgbaPixels;
                if (image.IsIndexed)
                {
                    // Convert indexed to RGBA for quantization
                    byte[] indexData = image.GetPixelData();
                    byte[] palRgba = image.GetPaletteRGBA();
                    rgbaPixels = new byte[image.Width * image.Height * 4];
                    for (int i = 0; i < indexData.Length && i < image.Width * image.Height; i++)
                    {
                        int palIdx = indexData[i];
                        int palOff = palIdx * 4;
                        if (palOff + 3 < palRgba.Length)
                        {
                            rgbaPixels[i * 4 + 0] = palRgba[palOff + 0];
                            rgbaPixels[i * 4 + 1] = palRgba[palOff + 1];
                            rgbaPixels[i * 4 + 2] = palRgba[palOff + 2];
                            rgbaPixels[i * 4 + 3] = palRgba[palOff + 3];
                        }
                    }
                }
                else
                {
                    rgbaPixels = image.GetPixelData();
                }

                // Quantize
                var qr = DecreaseColorCore.Quantize(rgbaPixels, image.Width, image.Height, maxColors);
                if (qr == null)
                {
                    result.Error = "Color quantization failed";
                    return result;
                }

                result.Success = true;
                result.IndexedPixels = qr.IndexData;
                result.GBAPalette = qr.GBAPalette;
                result.Width = qr.Width;
                result.Height = qr.Height;
            }

            return result;
        }
        /// <summary>
        /// Open file dialog, load image, validate dimensions, remap to an existing ROM palette.
        /// Used for editors that share a global palette (e.g. item icons, system icons).
        /// Returns null if user cancels.
        /// </summary>
        public static async Task<LoadResult> LoadAndRemapToExistingPalette(Window owner,
            int expectedWidth, int expectedHeight, byte[] existingGBAPalette, int colorCount,
            bool strictSize = false)
        {
            string filePath = await FileDialogHelper.OpenImageFile(owner);
            if (string.IsNullOrEmpty(filePath))
                return null;

            return LoadAndRemapFromFile(filePath, expectedWidth, expectedHeight,
                existingGBAPalette, colorCount, strictSize);
        }

        /// <summary>
        /// Load image from file, validate, remap pixels to existing palette by closest color match.
        /// </summary>
        public static LoadResult LoadAndRemapFromFile(string filePath,
            int expectedWidth, int expectedHeight, byte[] existingGBAPalette, int colorCount,
            bool strictSize = false)
        {
            var result = new LoadResult();
            var imgService = CoreState.ImageService;
            if (imgService == null)
            {
                result.Error = "Image service not initialized";
                return result;
            }

            IImage image;
            try
            {
                image = imgService.LoadImage(filePath);
            }
            catch (Exception ex)
            {
                result.Error = $"Failed to load image: {ex.Message}";
                return result;
            }

            using (image)
            {
                if (strictSize && (image.Width != expectedWidth || image.Height != expectedHeight))
                {
                    result.Error = $"Image must be {expectedWidth}x{expectedHeight} pixels (got {image.Width}x{image.Height})";
                    return result;
                }

                if (image.Width % 8 != 0 || image.Height % 8 != 0)
                {
                    result.Error = $"Image dimensions must be multiples of 8 (got {image.Width}x{image.Height})";
                    return result;
                }

                // Get RGBA pixel data
                byte[] rgbaPixels;
                if (image.IsIndexed)
                {
                    byte[] indexData = image.GetPixelData();
                    byte[] palRgba = image.GetPaletteRGBA();
                    rgbaPixels = new byte[image.Width * image.Height * 4];
                    for (int i = 0; i < indexData.Length && i < image.Width * image.Height; i++)
                    {
                        int palIdx = indexData[i];
                        int palOff = palIdx * 4;
                        if (palOff + 3 < palRgba.Length)
                        {
                            rgbaPixels[i * 4 + 0] = palRgba[palOff + 0];
                            rgbaPixels[i * 4 + 1] = palRgba[palOff + 1];
                            rgbaPixels[i * 4 + 2] = palRgba[palOff + 2];
                            rgbaPixels[i * 4 + 3] = palRgba[palOff + 3];
                        }
                    }
                }
                else
                {
                    rgbaPixels = image.GetPixelData();
                }

                // Remap to existing palette instead of quantizing a new one
                byte[] indexed = ImageImportCore.RemapToExistingPalette(
                    rgbaPixels, image.Width, image.Height, existingGBAPalette, colorCount);
                if (indexed == null)
                {
                    result.Error = "Failed to remap image to existing palette";
                    return result;
                }

                result.Success = true;
                result.IndexedPixels = indexed;
                result.GBAPalette = existingGBAPalette;
                result.Width = image.Width;
                result.Height = image.Height;
            }

            return result;
        }
    }
}
