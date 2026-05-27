// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageImportCore.RemapToExistingPalette (#662) — the nearest-color
// remap used by the Portrait Import Wizard's Share-palette / Custom-palette
// modes when remapping source pixels to a pre-existing palette.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageImportCoreRemapTests
    {
        static System.IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null) CoreState.ImageService = new StubImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : System.IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        // Helper: pack an RGB triple into a GBA BGR555 ushort (2 bytes).
        static void GbaPack(byte r, byte g, byte b, out byte lo, out byte hi)
        {
            // GBA BGR555: bits 0-4 R, 5-9 G, 10-14 B.
            int r5 = (r >> 3) & 0x1F;
            int g5 = (g >> 3) & 0x1F;
            int b5 = (b >> 3) & 0x1F;
            ushort gba = (ushort)((b5 << 10) | (g5 << 5) | r5);
            lo = (byte)(gba & 0xFF);
            hi = (byte)((gba >> 8) & 0xFF);
        }

        [Fact]
        public void RemapToExistingPalette_NullInputs_ReturnNull()
        {
            using var _ = EnsureImageService();
            Assert.Null(ImageImportCore.RemapToExistingPalette(null, 4, 4, new byte[32], 16));
            Assert.Null(ImageImportCore.RemapToExistingPalette(new byte[16 * 4], 4, 4, null, 16));
        }

        [Fact]
        public void RemapToExistingPalette_TransparentPixel_MapsToIndexZero()
        {
            using var _ = EnsureImageService();
            // Single transparent pixel -> index 0 regardless of palette.
            byte[] rgba = new byte[4] { 255, 0, 0, 0 }; // alpha=0 -> transparent
            byte[] pal = new byte[32];
            // Index 0 = white, index 5 = red. Even though red matches the pixel
            // color exactly, alpha=0 must force index 0.
            GbaPack(255, 255, 255, out pal[0], out pal[1]);
            GbaPack(255, 0, 0, out pal[10], out pal[11]);

            byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 1, 1, pal, 16);
            Assert.NotNull(indexed);
            Assert.Single(indexed);
            Assert.Equal(0, indexed[0]);
        }

        [Fact]
        public void RemapToExistingPalette_NearestColor_Matches()
        {
            using var _ = EnsureImageService();
            // 1x2 source: a near-red pixel + a near-blue pixel.
            byte[] rgba = new byte[]
            {
                250, 5, 5, 255,   // near red
                10, 10, 240, 255, // near blue
            };
            // Palette: index 0 white (transparent), index 1 red, index 2 green, index 3 blue.
            byte[] pal = new byte[32];
            GbaPack(255, 255, 255, out pal[0], out pal[1]);   // 0 white
            GbaPack(255, 0, 0, out pal[2], out pal[3]);       // 1 red
            GbaPack(0, 255, 0, out pal[4], out pal[5]);       // 2 green
            GbaPack(0, 0, 255, out pal[6], out pal[7]);       // 3 blue

            byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 2, 1, pal, 16);
            Assert.NotNull(indexed);
            Assert.Equal(2, indexed.Length);
            Assert.Equal(1, indexed[0]); // red
            Assert.Equal(3, indexed[1]); // blue
        }

        [Fact]
        public void RemapToExistingPalette_SkipsIndexZeroAsCandidate()
        {
            using var _ = EnsureImageService();
            // Opaque white pixel + palette with white at BOTH index 0 and 7.
            // The remap must NOT pick index 0 (reserved for transparency); it
            // should fall through to the next-closest candidate (index 7).
            byte[] rgba = new byte[] { 255, 255, 255, 255 };
            byte[] pal = new byte[32];
            GbaPack(255, 255, 255, out pal[0], out pal[1]);   // 0 white (skipped)
            GbaPack(0, 0, 0, out pal[2], out pal[3]);         // 1 black
            for (int i = 2; i < 7; i++)
            {
                GbaPack(128, 128, 128, out pal[i * 2], out pal[i * 2 + 1]); // gray
            }
            GbaPack(255, 255, 255, out pal[14], out pal[15]); // 7 white

            byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 1, 1, pal, 16);
            Assert.NotNull(indexed);
            Assert.Single(indexed);
            Assert.Equal(7, indexed[0]);
        }
    }
}
