// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the FE6 World Map Image editor Core helpers (#1183):
//   * ByteToImage256Liner / EncodeImage256Liner — the flat 256-color LINEAR codec.
//   * TryRenderFE6BigFieldMap — render one of the 5 zoom views (full + NW/NE/SW/SE)
//     at 240x160; FE6-only.
//   * CanRenderFE6BigFieldMap / CanImportFE6BigFieldMap — cheap UI gates.
//   * ImportFE6BigFieldMap — the inverse: validate-all-before-mutate, byte-
//     identical fault restore, LZ77-compress image AND palette + repoint both slots.
//
// Uses a SYNTHETIC FE6 ROM (8 MB, signature AFEJ01 -> version 6) so the version
// gate is satisfied deterministically, with a bidirectional 5-5-5 ImageService so
// the encode/decode round-trips exactly for colors whose low 3 bits are 0.
// [Collection("SharedState")] because they set CoreState.ROM / ImageService.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapFE6CoreTests
    {
        const int W = 240;
        const int H = 160;
        static readonly uint[] Slots = { 0u, 8u, 16u, 24u, 32u };

        // =================================================================
        // ByteToImage256Liner — pure decode
        // =================================================================

        [Fact]
        public void ByteToImage256Liner_NullService_ReturnsNull()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                Assert.Null(ImageUtilCore.ByteToImage256Liner(new byte[4], 0, new byte[512], 0, 2, 2));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void ByteToImage256Liner_DecodesDirectIndices()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();

                // 2x2 image: indices 0,1,2,3 -> 4 distinct palette colors.
                byte[] image = { 0, 1, 2, 3 };
                byte[] palette = new byte[256 * 2];
                PutColor(palette, 0, 0x08, 0x00, 0x00); // index 0 = R=8
                PutColor(palette, 1, 0x00, 0x08, 0x00); // index 1 = G=8
                PutColor(palette, 2, 0x00, 0x00, 0x08); // index 2 = B=8
                PutColor(palette, 3, 0x08, 0x08, 0x08); // index 3 = grey

                using IImage img = ImageUtilCore.ByteToImage256Liner(image, 0, palette, 0, 2, 2);
                Assert.NotNull(img);
                Assert.Equal(2, img.Width);
                Assert.Equal(2, img.Height);

                byte[] rgba = img.GetPixelData();
                // pixel 0 -> index 0 (R=8), opaque.
                Assert.Equal(8, rgba[0]); Assert.Equal(0, rgba[1]); Assert.Equal(0, rgba[2]); Assert.Equal(255, rgba[3]);
                // pixel 1 -> index 1 (G=8).
                Assert.Equal(0, rgba[4]); Assert.Equal(8, rgba[5]); Assert.Equal(0, rgba[6]);
                // pixel 3 -> index 3 (grey).
                Assert.Equal(8, rgba[12]); Assert.Equal(8, rgba[13]); Assert.Equal(8, rgba[14]);
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void ByteToImage256Liner_ShortStream_PartialNotThrow()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // Only 2 bytes for a 2x2 (needs 4) image -> partial, no throw.
                using IImage img = ImageUtilCore.ByteToImage256Liner(new byte[] { 1, 1 }, 0, new byte[512], 0, 2, 2);
                Assert.NotNull(img);
                Assert.Equal(2, img.Width);
            }
            finally { CoreState.ImageService = saved; }
        }

        // =================================================================
        // EncodeImage256Liner — pure encode (inverse)
        // =================================================================

        [Fact]
        public void EncodeImage256Liner_BuildsIndicesAndPalette()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // 2x2 RGBA: 3 distinct colors (px0=px3 share a color).
                byte[] rgba = new byte[2 * 2 * 4];
                SetPx(rgba, 0, 8, 0, 0);
                SetPx(rgba, 1, 0, 8, 0);
                SetPx(rgba, 2, 0, 0, 8);
                SetPx(rgba, 3, 8, 0, 0); // == px0

                Assert.True(ImageUtilCore.EncodeImage256Liner(rgba, 2, 2,
                    out byte[] image256, out byte[] palette512));
                Assert.NotNull(image256);
                Assert.NotNull(palette512);
                Assert.Equal(4, image256.Length);
                Assert.Equal(512, palette512.Length);
                // First-seen order: px0->0, px1->1, px2->2, px3 reuses 0.
                Assert.Equal(0, image256[0]);
                Assert.Equal(1, image256[1]);
                Assert.Equal(2, image256[2]);
                Assert.Equal(0, image256[3]);
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void EncodeImage256Liner_Over256Colors_ReturnsFalse()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // 257 colors that are distinct AFTER RGB555 truncation (multiples
                // of 8 per channel; 32 levels each). r = (i%32)*8, g = (i/32)*8.
                int n = 257;
                byte[] rgba = new byte[n * 4];
                for (int i = 0; i < n; i++)
                {
                    rgba[i * 4 + 0] = (byte)((i % 32) * 8);
                    rgba[i * 4 + 1] = (byte)((i / 32) * 8);
                    rgba[i * 4 + 2] = 0;
                    rgba[i * 4 + 3] = 255;
                }
                Assert.False(ImageUtilCore.EncodeImage256Liner(rgba, n, 1,
                    out byte[] image256, out byte[] palette512));
                Assert.Null(image256);
                Assert.Null(palette512);
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void EncodeImage256Liner_RGB888CollapsingToSameRGB555_ShareOnePaletteEntry()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // Two RGB888 colors that differ only in the low 3 bits of red:
                // 0x08 (00001000) and 0x0F (00001111) both -> RGB555 red = 1.
                byte[] rgba = new byte[2 * 1 * 4];
                SetPx(rgba, 0, 0x08, 0, 0);
                SetPx(rgba, 1, 0x0F, 0, 0);

                Assert.True(ImageUtilCore.EncodeImage256Liner(rgba, 2, 1,
                    out byte[] image256, out byte[] palette512));
                // Both pixels collapse to the SAME GBA color -> index 0 for both
                // (one palette entry, not two).
                Assert.Equal(0, image256[0]);
                Assert.Equal(0, image256[1]);
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void EncodeImage256Liner_AlphaVariants_MapToSamePaletteColor()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // Same RGB, different alpha -> alpha is IGNORED -> one palette entry.
                byte[] rgba = new byte[2 * 1 * 4];
                rgba[0] = 8; rgba[1] = 0; rgba[2] = 0; rgba[3] = 255;
                rgba[4] = 8; rgba[5] = 0; rgba[6] = 0; rgba[7] = 0;

                Assert.True(ImageUtilCore.EncodeImage256Liner(rgba, 2, 1,
                    out byte[] image256, out byte[] palette512));
                Assert.Equal(0, image256[0]);
                Assert.Equal(0, image256[1]);
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Encode_Then_Decode_RoundTrips()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new RoundTripImageService();
                // 4x2 RGBA, 4 colors (low 3 bits zero so 5-5-5 round-trips exactly).
                byte[] rgba = new byte[4 * 2 * 4];
                for (int p = 0; p < 8; p++)
                    SetPx(rgba, p, (byte)((p % 4) * 8 + 8), (byte)((p % 2) * 8), 0);

                Assert.True(ImageUtilCore.EncodeImage256Liner(rgba, 4, 2,
                    out byte[] image256, out byte[] palette512));
                using IImage img = ImageUtilCore.ByteToImage256Liner(image256, 0, palette512, 0, 4, 2);
                Assert.NotNull(img);
                byte[] outRgba = img.GetPixelData();
                for (int p = 0; p < 8; p++)
                {
                    Assert.Equal(rgba[p * 4 + 0], outRgba[p * 4 + 0]);
                    Assert.Equal(rgba[p * 4 + 1], outRgba[p * 4 + 1]);
                    Assert.Equal(rgba[p * 4 + 2], outRgba[p * 4 + 2]);
                    Assert.Equal(255, outRgba[p * 4 + 3]);
                }
            }
            finally { CoreState.ImageService = saved; }
        }

        // =================================================================
        // TryRenderFE6BigFieldMap + gates
        // =================================================================

        [Fact]
        public void Render_NullRom_ReturnsNull()
        {
            Assert.Null(ImageWorldMapCore.TryRenderFE6BigFieldMap(null, 0));
        }

        [Fact]
        public void Render_NonFE6_ReturnsNull()
        {
            WithFE8(rom =>
            {
                // FE8's worldmap_big_* slots are resolvable, but the FE6 256-liner
                // decoder must NOT interpret them — the version gate returns null.
                Assert.Null(ImageWorldMapCore.TryRenderFE6BigFieldMap(rom, 0));
                Assert.False(ImageWorldMapCore.CanRenderFE6BigFieldMap(rom));
                Assert.False(ImageWorldMapCore.CanImportFE6BigFieldMap(rom, 0));
            });
        }

        [Fact]
        public void Render_FE6_AllFiveSlots_ReturnNonNull_240x160()
        {
            WithFE6Planted(rom =>
            {
                Assert.True(ImageWorldMapCore.CanRenderFE6BigFieldMap(rom));
                foreach (uint slot in Slots)
                {
                    Assert.True(ImageWorldMapCore.CanImportFE6BigFieldMap(rom, slot));
                    using IImage img = ImageWorldMapCore.TryRenderFE6BigFieldMap(rom, slot);
                    Assert.NotNull(img);
                    Assert.Equal(W, img.Width);
                    Assert.Equal(H, img.Height);
                }
            });
        }

        // =================================================================
        // ImportFE6BigFieldMap — round-trip + reject + fault restore
        // =================================================================

        [Fact]
        public void Import_NonFE6_ReturnsError_NoMutation()
        {
            WithFE8(rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                byte[] rgba = SolidRgba(W, H, 8, 8, 8);
                string err = ImageWorldMapCore.ImportFE6BigFieldMap(rom, 0, rgba, W, H);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Import_WrongSize_ReturnsError_NoMutation()
        {
            WithFE6Planted(rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                byte[] rgba = SolidRgba(100, 100, 8, 8, 8);
                string err = ImageWorldMapCore.ImportFE6BigFieldMap(rom, 0, rgba, 100, 100);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Import_Over256Colors_ReturnsError_NoMutation()
        {
            WithFE6Planted(rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                // 240x160 with > 256 distinct RGB555 colors (multiples of 8 per
                // channel so they survive the 5-5-5 truncation; 32*32 = 1024 distinct).
                byte[] rgba = new byte[W * H * 4];
                for (int p = 0; p < W * H; p++)
                {
                    rgba[p * 4 + 0] = (byte)((p % 32) * 8);
                    rgba[p * 4 + 1] = (byte)(((p / 32) % 32) * 8);
                    rgba[p * 4 + 2] = 0;
                    rgba[p * 4 + 3] = 255;
                }
                string err = ImageWorldMapCore.ImportFE6BigFieldMap(rom, 0, rgba, W, H);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(8u)]
        [InlineData(16u)]
        [InlineData(24u)]
        [InlineData(32u)]
        public void Import_Success_RepointsSlots_UndoRestores(uint slot)
        {
            WithFE6Planted(rom =>
            {
                CoreState.Undo = new Undo();
                try
                {
                    byte[] snapshot = (byte[])rom.Data.Clone();
                    uint imgSlot = rom.RomInfo.worldmap_big_image_pointer + slot;
                    uint palSlot = rom.RomInfo.worldmap_big_palette_pointer + slot;
                    uint oldImg = rom.u32(imgSlot);
                    uint oldPal = rom.u32(palSlot);

                    byte[] rgba = SolidRgba(W, H, 16, 24, 32);

                    var undo = CoreState.Undo.NewUndoData("test");
                    string err;
                    using (ROM.BeginUndoScope(undo))
                        err = ImageWorldMapCore.ImportFE6BigFieldMap(rom, slot, rgba, W, H);
                    Assert.True(string.IsNullOrEmpty(err), err);
                    CoreState.Undo.Push(undo);

                    // Both slots repointed to fresh free-space data.
                    Assert.NotEqual(oldImg, rom.u32(imgSlot));
                    Assert.NotEqual(oldPal, rom.u32(palSlot));

                    // The new image LZ77-decompresses to the 256-linear bytes.
                    uint newImgOff = U.toOffset(rom.u32(imgSlot));
                    byte[] dec = LZ77.decompress(rom.Data, newImgOff);
                    Assert.NotNull(dec);
                    Assert.True(dec.Length >= W * H);

                    // Undo restores the ROM byte-identically.
                    CoreState.Undo.RunUndo();
                    Assert.Equal(snapshot, rom.Data);
                }
                finally { CoreState.Undo = null; }
            });
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithFE6Planted(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x800000]; // 8 MB (min for FE6 detection)
                rom.LoadLow("synth_fe6.gba", data, "AFEJ01");
                CoreState.ROM = rom;
                CoreState.ImageService = new RoundTripImageService();

                // Plant a small LZ77 256-linear image + palette for each of the 5
                // zoom slots, well clear of each other and the danger zone.
                uint imgBase = rom.RomInfo.worldmap_big_image_pointer;
                uint palBase = rom.RomInfo.worldmap_big_palette_pointer;
                uint dataOff = 0x100000;
                for (int s = 0; s < Slots.Length; s++)
                {
                    // 240x160 of solid index 0 (plus a couple distinct indices).
                    byte[] image = new byte[W * H];
                    image[0] = 1; image[1] = 2;
                    byte[] palette = new byte[256 * 2];
                    PutColor(palette, 0, 0x08, 0x08, 0x08);
                    PutColor(palette, 1, 0x10, 0x00, 0x00);
                    PutColor(palette, 2, 0x00, 0x10, 0x00);

                    byte[] imgZ = LZ77.compress(image);
                    byte[] palZ = LZ77.compress(palette);
                    Array.Copy(imgZ, 0, rom.Data, dataOff, imgZ.Length);
                    rom.write_u32(imgBase + Slots[s], U.toPointer(dataOff));
                    dataOff += 0x20000;
                    Array.Copy(palZ, 0, rom.Data, dataOff, palZ.Length);
                    rom.write_u32(palBase + Slots[s], U.toPointer(dataOff));
                    dataOff += 0x20000;
                }

                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        static void WithFE8(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
                rom.LoadLow("synth_fe8.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new RoundTripImageService();
                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        static void PutColor(byte[] palette, int index, byte r, byte g, byte b)
        {
            ushort gba = (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
            palette[index * 2 + 0] = (byte)(gba & 0xFF);
            palette[index * 2 + 1] = (byte)((gba >> 8) & 0xFF);
        }

        static void SetPx(byte[] rgba, int p, byte r, byte g, byte b)
        {
            rgba[p * 4 + 0] = r; rgba[p * 4 + 1] = g; rgba[p * 4 + 2] = b; rgba[p * 4 + 3] = 255;
        }

        static byte[] SolidRgba(int w, int h, byte r, byte g, byte b)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int p = 0; p < w * h; p++) SetPx(rgba, p, r, g, b);
            return rgba;
        }
    }

    /// <summary>A test IImageService with a bidirectional 5-5-5 conversion so the
    /// 256-linear encode/decode round-trips exactly for colors whose low 3 bits
    /// are 0. The pixel store is a flat RGBA buffer (256-liner is RGBA, not
    /// indexed).</summary>
    internal sealed class RoundTripImageService : IImageService
    {
        public IImage CreateImage(int w, int h) => new RtImage(w, h);
        public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new RtImage(w, h);
        public IImage LoadImage(string f) => null;
        public IImage LoadImageFromBytes(byte[] d) => null;
        public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }
        public ushort RGBAToGBAColor(byte r, byte g, byte b)
            => (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => new RtImage(w, h);
        public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new RtImage(w, h);
        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new RtImage(w, h);
        public byte[] Encode4bppTiles(IImage i) => null;
        public byte[] Encode8bppTiles(IImage i) => null;
        public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
    }

    internal sealed class RtImage : IImage
    {
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => false;
        byte[] _pixels;
        public RtImage(int w, int h) { Width = w; Height = h; _pixels = new byte[w * h * 4]; }
        public byte[] GetPixelData() => _pixels;
        public void SetPixelData(byte[] data) => _pixels = data;
        public byte[] GetPaletteGBA() => Array.Empty<byte>();
        public void SetPaletteGBA(byte[] p) { }
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string f) { }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }
}
