// SPDX-License-Identifier: GPL-3.0-or-later
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class FontLibraryPngLegacyImportTests
{
    [Theory]
    [InlineData(FontLibraryFormat.Main16x16, FontLibraryStyle.Item)]
    [InlineData(FontLibraryFormat.Main16x16, FontLibraryStyle.Text)]
    [InlineData(FontLibraryFormat.Zh16x13, FontLibraryStyle.Item)]
    [InlineData(FontLibraryFormat.Zh16x13, FontLibraryStyle.Text)]
    public void CanonicalPng_RoundTripsThroughRealLegacyNearestColorLoader(
        FontLibraryFormat format,
        FontLibraryStyle style)
    {
        IImageService? prior = CoreState.ImageService;
        string path = Path.Combine(
            Path.GetTempPath(),
            "font-library-palette-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            CoreState.ImageService = new SkiaImageService();
            var indices = new byte[16 * 16];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = (byte)(i & 3);
            File.WriteAllBytes(
                path,
                IndexedPngCore.Encode(indices, format, style));
            bool item = style == FontLibraryStyle.Item;
            byte[] palette = format == FontLibraryFormat.Main16x16
                ? FontGlyphRenderCore.GetFontPaletteGBA(item)
                : FontGlyphZHCore.GetFontPaletteGBA(item);

            ImageImportService.LoadResult loaded =
                ImageImportService.LoadAndRemapFromFile(
                    path,
                    16,
                    16,
                    palette,
                    4,
                    strictSize: true);

            Assert.NotNull(loaded);
            Assert.True(loaded.Success, loaded.Error);
            Assert.Equal(indices, loaded.IndexedPixels);
        }
        finally
        {
            CoreState.ImageService = prior;
            try { File.Delete(path); }
            catch { }
        }
    }
}
