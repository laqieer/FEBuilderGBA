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
            /// <summary>
            /// Number of distinct colors the quantizer produced (&lt;= maxColors).
            /// Lets callers detect when a source genuinely needed more colors than
            /// a single GBA palette bank (e.g. the battle-screen bulk import's
            /// single-bank guard, #989). Only set by the quantize path
            /// (<see cref="LoadAndQuantizeFromFile"/>); 0 on the remap paths.
            /// </summary>
            public int ColorCount { get; set; }
            /// <summary>
            /// Absolute path of the source file the user picked. Editors that
            /// keep a Source-File ResourceCache entry (e.g.
            /// ImagePortraitFE6View) use this to populate the Open/Select
            /// Source buttons after an import.
            /// </summary>
            public string SourcePath { get; set; }
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
        /// <param name="requireTileMultiple">
        /// When <c>true</c> (default), the image width and height must both be
        /// multiples of 8 - required for tile-based sprite/graphic import.
        /// Pass <c>false</c> for palette-only import where any image size is
        /// acceptable (mirrors WinForms <c>PaletteFormRef.MakePaletteBitmapToUI</c>
        /// which extracts <c>ColorPalette</c> from any bitmap with no dimension
        /// restriction). Does NOT override <paramref name="strictSize"/> - both
        /// flags apply independently. FIX 1 (#871).
        /// </param>
        public static LoadResult LoadAndQuantizeFromFile(string filePath, int expectedWidth, int expectedHeight,
            int maxColors = 16, bool strictSize = false, bool requireTileMultiple = true)
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

                // FIX 1 (#871): skip the tile-size check for palette-only import.
                // Mirrors WF PaletteFormRef.MakePaletteBitmapToUI which extracts
                // ColorPalette from any image with no dimension restriction.
                if (requireTileMultiple && (image.Width % 8 != 0 || image.Height % 8 != 0))
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
                result.ColorCount = qr.ColorCount;
                result.SourcePath = filePath;
            }

            return result;
        }
        /// <summary>
        /// Open file dialog, load image, validate dimensions, remap to an existing ROM palette.
        /// Used for editors that share a global palette (e.g. item icons, system icons).
        /// Returns null if user cancels.
        /// </summary>
        /// <param name="requireTileMultiple">
        /// When <c>true</c> (default), the image width and height must both be
        /// multiples of 8 - required for tile-based sprite/graphic import. Pass
        /// <c>false</c> for editors whose glyph is NOT a whole number of 8x8 tiles
        /// (e.g. the 16x13 Chinese font glyph, #1166) - the closest-color remap in
        /// <c>ImageImportCore.RemapToExistingPalette</c> handles arbitrary dims.
        /// Does NOT override <paramref name="strictSize"/> - both flags apply
        /// independently.
        /// </param>
        public static async Task<LoadResult> LoadAndRemapToExistingPalette(Window owner,
            int expectedWidth, int expectedHeight, byte[] existingGBAPalette, int colorCount,
            bool strictSize = false, bool requireTileMultiple = true)
        {
            string filePath = await FileDialogHelper.OpenImageFile(owner);
            if (string.IsNullOrEmpty(filePath))
                return null;

            return LoadAndRemapFromFile(filePath, expectedWidth, expectedHeight,
                existingGBAPalette, colorCount, strictSize, requireTileMultiple);
        }

        /// <summary>
        /// Load image from file, validate, remap pixels to existing palette by closest color match.
        /// </summary>
        /// <param name="requireTileMultiple">
        /// When <c>true</c> (default), the image width and height must both be
        /// multiples of 8. Pass <c>false</c> for non-tile glyphs (e.g. the 16x13
        /// Chinese font glyph, #1166). See the overload above.
        /// </param>
        public static LoadResult LoadAndRemapFromFile(string filePath,
            int expectedWidth, int expectedHeight, byte[] existingGBAPalette, int colorCount,
            bool strictSize = false, bool requireTileMultiple = true)
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

                // Skip the tile-size check for non-tile glyphs (e.g. the 16x13 ZH font
                // glyph, #1166). RemapToExistingPalette handles arbitrary dims.
                if (requireTileMultiple && (image.Width % 8 != 0 || image.Height % 8 != 0))
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

        /// <summary>
        /// Load image from file and return RGBA pixels (no quantization).
        /// Used for multi-palette remap where we need raw RGBA to match per-tile sub-palettes.
        /// </summary>
        public static LoadResult LoadForMultiPaletteRemap(string filePath, int expectedW, int expectedH,
            byte[] existingGBAPalette, int subPaletteCount)
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

                // Remap using multi-palette
                var remap = ImageImportCore.RemapToMultiPalette(
                    rgbaPixels, image.Width, image.Height, existingGBAPalette, subPaletteCount);
                if (remap == null)
                {
                    result.Error = "Failed to remap image to multi-palette";
                    return result;
                }

                result.Success = true;
                result.IndexedPixels = remap.IndexedPixels;
                result.GBAPalette = existingGBAPalette;
                result.Width = image.Width;
                result.Height = image.Height;
            }

            return result;
        }
    }
}
