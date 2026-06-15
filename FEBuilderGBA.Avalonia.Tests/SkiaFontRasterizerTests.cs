// SPDX-License-Identifier: GPL-3.0-or-later
//
// Tests for the cross-platform glyph rasterizer (#796):
// FEBuilderGBA.SkiaSharp.SkiaFontRasterizer. These lock the Skia-path output
// contract — 64-byte tiles, palette-index ranges, width ordering, the item
// outline ring, graceful unknown-family fallback, and exact golden bytes for
// one text + one item glyph of the bundled license-clean Tuffy font (SIL OFL
// 1.1, see Fonts/OFL.txt). Glyphs load via FontSpec.FontFileData (FromStream)
// so the bytes are deterministic across Windows / Ubuntu / macOS CI.
using System;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.SkiaSharp;
using FEBuilderGBA.SharedTest;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SkiaFontRasterizerTests
    {
        static byte[] LoadTuffy()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fonts", "Tuffy-Regular.ttf");
            Assert.True(File.Exists(path), $"bundled font missing at {path}");
            return File.ReadAllBytes(path);
        }

        static FontSpec TuffySpec(float size = 12f) => new FontSpec
        {
            FamilyName = "Tuffy",
            Size = size,
            FontFileData = LoadTuffy(),
        };

        // ---------- shape / size invariants ----------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RasterizeGlyph_Returns64Bytes(bool isItemFont)
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont, 0, out _);
            Assert.Equal(64, tile.Length);
        }

        [Fact]
        public void TextFont_IndicesAreSubsetOf_0_and_3()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: false, 0, out _);
            foreach (byte idx in UnpackIndices(tile))
            {
                Assert.True(idx == 0 || idx == 3, $"text-font index {idx} not in {{0,3}}");
            }
        }

        [Fact]
        public void ItemFont_IndicesAreSubsetOf_0_2_3()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: true, 0, out _);
            foreach (byte idx in UnpackIndices(tile))
            {
                Assert.True(idx == 0 || idx == 2 || idx == 3,
                    $"item-font index {idx} not in {{0,2,3}}");
            }
        }

        // ---------- content invariants ----------

        [Fact]
        public void Letter_A_HasForeground()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: false, 0, out int width);
            Assert.Contains(UnpackIndices(tile), idx => idx == 3);
            Assert.True(width > 1, "rendered 'A' should advance more than 1px");
        }

        [Fact]
        public void Space_IsEmpty()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), " ", isItemFont: false, 0, out _);
            Assert.All(UnpackIndices(tile), idx => Assert.Equal(0, idx));
        }

        [Fact]
        public void Width_W_GreaterThan_Width_i()
        {
            var r = new SkiaFontRasterizer();
            r.RasterizeGlyph(TuffySpec(), "W", isItemFont: false, 0, out int wW);
            r.RasterizeGlyph(TuffySpec(), "i", isItemFont: false, 0, out int wI);
            Assert.True(wW > wI, $"expected width(W)={wW} > width(i)={wI}");
        }

        [Fact]
        public void ItemFont_HasOutlineRingAroundGlyphFill()
        {
            // Synthetic high-contrast case: a tall, dense glyph guarantees the
            // 4-neighbour outline pass produces idx-3 ring cells adjacent to
            // idx-2 glyph-fill cells. We assert that EVERY idx-2 cell has at
            // least one orthogonal neighbour that is idx-3 OR idx-2 (i.e. the
            // outline wraps the fill — an interior fill pixel is surrounded by
            // fill, an edge fill pixel by outline).
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(20f), "W", isItemFont: true, 0, out _);
            byte[,] grid = SkiaFontGoldens.UnpackGrid(tile);

            int fillCount = 0;
            int ringCount = 0;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (grid[x, y] == 2) fillCount++;
                    if (grid[x, y] == 3) ringCount++;
                }

            Assert.True(fillCount > 0, "expected some glyph-fill (idx 2) cells");
            Assert.True(ringCount > 0, "expected some outline-ring (idx 3) cells");

            // Every glyph-fill cell must be adjacent (4-neighbour) to an outline
            // or another fill — never floating in bare background.
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (grid[x, y] != 2) continue;
                    bool wrapped =
                        (x > 0 && (grid[x - 1, y] == 2 || grid[x - 1, y] == 3)) ||
                        (x < 15 && (grid[x + 1, y] == 2 || grid[x + 1, y] == 3)) ||
                        (y > 0 && (grid[x, y - 1] == 2 || grid[x, y - 1] == 3)) ||
                        (y < 15 && (grid[x, y + 1] == 2 || grid[x, y + 1] == 3));
                    Assert.True(wrapped, $"glyph-fill at ({x},{y}) is not wrapped by outline/fill");
                }
        }

        // ---------- fallback ----------

        [Fact]
        public void UnknownFamily_NoFontData_FallsBackWithoutCrash()
        {
            var r = new SkiaFontRasterizer();
            var spec = new FontSpec
            {
                FamilyName = "ThisFamilyDefinitelyDoesNotExist_12345_xyz",
                Size = 12f,
                // No FontFileData / FontFilePath -> resolves family name,
                // SKTypeface falls back to the platform default.
            };
            byte[] tile = r.RasterizeGlyph(spec, "A", isItemFont: false, 0, out int width);
            Assert.Equal(64, tile.Length);
            Assert.InRange(width, 1, 16);
        }

        // ---------- GOLDEN: Skia-path regression lock (pixel tolerance) ----------
        //
        // The golden tiles, widths, and per-glyph-kind tolerances live in the
        // shared SkiaFontGoldens (FEBuilderGBA.SharedTest) so this desktop lock
        // and the cross-platform render parity smoke test
        // (FEBuilderGBA.Core.Tests/SkiaRenderByteParityTests.cs, #1125) can never
        // silently diverge — both <Compile … Link>-include the same source file.
        // We compare with a small PER-PIXEL tolerance (not exact byte equality)
        // because arm64 Skia AA can land a glyph-edge pixel on the other side of
        // the `R < 0xA0` foreground threshold; see SkiaFontGoldens for the full
        // rationale and the observed-drift-vs-tolerance margins.

        [Fact]
        public void Golden_TextGlyph_A_WithinPixelTolerance()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: false, 0, out int width);
            int diff = SkiaFontGoldens.CountDifferingPixels(SkiaFontGoldens.GoldenTextA, tile);
            Assert.True(diff <= SkiaFontGoldens.GoldenTextPixelTolerance,
                $"text glyph 'A' differs from baseline golden by {diff} pixels (tolerance {SkiaFontGoldens.GoldenTextPixelTolerance})");
            Assert.Equal(SkiaFontGoldens.GoldenTextAWidth, width);
        }

        [Fact]
        public void Golden_ItemGlyph_A_WithinPixelTolerance()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: true, 0, out int width);
            int diff = SkiaFontGoldens.CountDifferingPixels(SkiaFontGoldens.GoldenItemA, tile);
            Assert.True(diff <= SkiaFontGoldens.GoldenItemPixelTolerance,
                $"item glyph 'A' differs from baseline golden by {diff} pixels (tolerance {SkiaFontGoldens.GoldenItemPixelTolerance})");
            Assert.Equal(SkiaFontGoldens.GoldenItemAWidth, width);
        }

        // ---------- helpers ----------

        static byte[] UnpackIndices(byte[] tile) => SkiaFontGoldens.UnpackIndices(tile);
    }
}
