// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the PortraitRendererCore.DrawPortraitMap(uint, byte[]) block-palette
// overload added for the Palette Editor live preview (#1023).
//
// The key correctness contract these tests pin down:
//   PaletteCore.PackToBytes(colors)  ==  ImageUtilCore.GetPalette(ptr, 16)
// (byte-for-byte) when the ROM bytes at ptr were written from PackToBytes of the
// same 8-bit colors. Because LoadROMTiles4bpp is driven entirely by that 32-byte
// block, byte-identical blocks render pixel-identical images by construction —
// which is what the live preview relies on (the grid colors packed via
// PackToBytes recolor the portrait exactly as a post-Write ROM read would).
//
// The Core.Tests project ships no real decoder (StubImageService returns blank
// images), so this file installs a minimal but REAL 4bpp tile decoder that
// applies the supplied palette block. That makes the ptr-vs-block render
// equivalence a genuine pixel comparison rather than a trivial size check.

using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PortraitRendererCoreBlockPaletteTests : IDisposable
    {
        // Layout inside the synthetic ROM.
        const uint FaceOffset = 0x1000;    // LZ77-compressed 4x4-tile (32x32) 4bpp face
        const uint PaletteOffset = 0x4000; // 32-byte raw GBA palette block

        readonly ROM _savedRom;
        readonly IImageService _savedSvc;

        public PortraitRendererCoreBlockPaletteTests()
        {
            _savedRom = CoreState.ROM;
            _savedSvc = CoreState.ImageService;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.ImageService = _savedSvc;
        }

        // ---- helpers ----

        // 16 distinct, non-degenerate 8-bit colors so the pack/unpack round-trip
        // and the per-pixel palette lookup are both exercised on real values.
        static (byte r, byte g, byte b)[] SampleColors()
        {
            var colors = new (byte, byte, byte)[16];
            for (int i = 0; i < 16; i++)
                colors[i] = ((byte)(i * 16), (byte)(255 - i * 16), (byte)(i * 8 + 7));
            return colors;
        }

        static ROM MakeRomWithFace(byte[] paletteBlock)
        {
            var rom = new ROM();
            byte[] data = new byte[0x100000];
            rom.LoadLow("synth.gba", data, "BE8E01");

            // Build a deterministic 4x4-tile (16 tiles) 4bpp face: 16 tiles * 32
            // bytes = 512 raw bytes. Each pixel-pair byte uses both nibbles so a
            // range of palette indices 0..15 is hit across the image.
            byte[] raw = new byte[16 * 32];
            for (int i = 0; i < raw.Length; i++)
            {
                int lo = (i) & 0xF;
                int hi = (i * 3 + 1) & 0xF;
                raw[i] = (byte)((hi << 4) | lo);
            }
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, FaceOffset, comp.Length);

            // Raw 32-byte palette block at PaletteOffset.
            Array.Copy(paletteBlock, 0, rom.Data, PaletteOffset, paletteBlock.Length);
            return rom;
        }

        // ---- guard tests (no ImageService / ROM needed) ----

        [Fact]
        public void BlockOverload_NullRom_ReturnsNull()
        {
            CoreState.ROM = null;
            CoreState.ImageService = new DecodingImageService();
            byte[] block = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
            Assert.Null(PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), block));
        }

        [Fact]
        public void BlockOverload_NullImageService_ReturnsNull()
        {
            CoreState.ROM = MakeRomWithFace(new byte[PaletteCore.PALETTE_BLOCK_SIZE]);
            CoreState.ImageService = null;
            byte[] block = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
            Assert.Null(PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), block));
        }

        [Fact]
        public void BlockOverload_NullBlock_ReturnsNull()
        {
            CoreState.ROM = MakeRomWithFace(new byte[PaletteCore.PALETTE_BLOCK_SIZE]);
            CoreState.ImageService = new DecodingImageService();
            Assert.Null(PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), (byte[])null));
        }

        [Fact]
        public void BlockOverload_ShortBlock_ReturnsNull()
        {
            CoreState.ROM = MakeRomWithFace(new byte[PaletteCore.PALETTE_BLOCK_SIZE]);
            CoreState.ImageService = new DecodingImageService();
            // 30 bytes < the required 32 -> rejected, no render.
            Assert.Null(PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), new byte[30]));
        }

        [Fact]
        public void BlockOverload_ZeroMapFacePtr_ReturnsNull()
        {
            CoreState.ROM = MakeRomWithFace(new byte[PaletteCore.PALETTE_BLOCK_SIZE]);
            CoreState.ImageService = new DecodingImageService();
            Assert.Null(PortraitRendererCore.DrawPortraitMap(0, new byte[PaletteCore.PALETTE_BLOCK_SIZE]));
        }

        [Fact]
        public void BlockOverload_ValidBlock_ReturnsNonNullImage()
        {
            byte[] block = PaletteCore.PackToBytes(SampleColors());
            CoreState.ROM = MakeRomWithFace(block);
            CoreState.ImageService = new DecodingImageService();

            var img = PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), block);
            Assert.NotNull(img);
            Assert.Equal(32, img.Width);
            Assert.Equal(32, img.Height);
        }

        // ---- the core correctness contract ----

        [Fact]
        public void PackToBytes_MatchesGetPalette_ByteForByte()
        {
            // Pack 8-bit colors -> 32-byte block, plant it in the ROM, read it
            // back via GetPalette: the two blocks MUST be byte-identical. This is
            // the format-agreement guarantee the live preview depends on.
            byte[] packed = PaletteCore.PackToBytes(SampleColors());
            CoreState.ROM = MakeRomWithFace(packed);
            CoreState.ImageService = new DecodingImageService();

            byte[] fromRom = ImageUtilCore.GetPalette(PaletteOffset, 16);
            Assert.NotNull(fromRom);
            Assert.Equal(PaletteCore.PALETTE_BLOCK_SIZE, packed.Length);
            Assert.Equal(packed, fromRom);
        }

        [Fact]
        public void BlockOverload_EqualsPtrOverload_PixelForPixel()
        {
            // Render the SAME face with (a) the ptr overload reading the palette
            // from ROM and (b) the block overload using GetPalette(ptr,16) bytes.
            // They must be pixel-identical, proving the block overload is a
            // faithful substitute for the ptr overload.
            byte[] block = PaletteCore.PackToBytes(SampleColors());
            CoreState.ROM = MakeRomWithFace(block);
            CoreState.ImageService = new DecodingImageService();

            byte[] romBlock = ImageUtilCore.GetPalette(PaletteOffset, 16);

            var ptrImg = PortraitRendererCore.DrawPortraitMap(
                U.toPointer(FaceOffset), U.toPointer(PaletteOffset));
            var blockImg = PortraitRendererCore.DrawPortraitMap(
                U.toPointer(FaceOffset), romBlock);

            Assert.NotNull(ptrImg);
            Assert.NotNull(blockImg);
            Assert.Equal(ptrImg.Width, blockImg.Width);
            Assert.Equal(ptrImg.Height, blockImg.Height);

            byte[] a = ptrImg.GetPixelData();
            byte[] b = blockImg.GetPixelData();
            Assert.Equal(a, b);

            // And the rendered pixels are NOT all-zero — the decoder actually
            // applied the palette (so the equivalence is meaningful).
            bool anyNonZero = false;
            foreach (byte x in a) { if (x != 0) { anyNonZero = true; break; } }
            Assert.True(anyNonZero, "rendered image should contain non-zero pixels");
        }

        [Fact]
        public void BlockOverload_DifferentColors_ProduceDifferentPixels()
        {
            // A live edit to the grid changes the rendered colors: packing two
            // different color sets must yield two different rendered images.
            CoreState.ImageService = new DecodingImageService();

            var colorsA = SampleColors();
            var colorsB = SampleColors();
            for (int i = 0; i < 16; i++)
                colorsB[i] = ((byte)(colorsA[i].r ^ 0xFF), colorsA[i].g, colorsA[i].b);

            byte[] blockA = PaletteCore.PackToBytes(colorsA);
            byte[] blockB = PaletteCore.PackToBytes(colorsB);

            CoreState.ROM = MakeRomWithFace(blockA);
            var imgA = PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), blockA);
            var imgB = PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), blockB);

            Assert.NotNull(imgA);
            Assert.NotNull(imgB);
            Assert.NotEqual(imgA.GetPixelData(), imgB.GetPixelData());
        }

        [Fact]
        public void BlockOverload_LongerBlock_SlicedToFirst32Bytes()
        {
            // A multi-bank (64-byte) buffer must render identically to its first
            // 32 bytes — the overload slices defensively so a stray second bank
            // never leaks into the render.
            CoreState.ImageService = new DecodingImageService();
            byte[] block32 = PaletteCore.PackToBytes(SampleColors());
            CoreState.ROM = MakeRomWithFace(block32);

            byte[] block64 = new byte[64];
            Array.Copy(block32, block64, 32);
            // Fill the second bank with junk that must be ignored.
            for (int i = 32; i < 64; i++) block64[i] = (byte)(i * 7);

            var img32 = PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), block32);
            var img64 = PortraitRendererCore.DrawPortraitMap(U.toPointer(FaceOffset), block64);

            Assert.NotNull(img32);
            Assert.NotNull(img64);
            Assert.Equal(img32.GetPixelData(), img64.GetPixelData());
        }

        // ---- a real 4bpp decoder so the equivalence comparison is meaningful ----

        sealed class DecodingImageService : IImageService
        {
            public IImage CreateImage(int w, int h) => new MemImage(w, h);
            public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new MemImage(w, h);
            public IImage LoadImage(string f) => null;
            public IImage LoadImageFromBytes(byte[] d) => null;

            public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
            {
                r = (byte)((gbaColor & 0x1F) << 3);
                g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
            }

            public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;

            // Real 4bpp tile decode: walk 8x8 tiles row-major, look each 4-bit
            // index up in the supplied 32-byte palette block. Index 0 -> alpha 0.
            public IImage Decode4bppTiles(byte[] tiles, int offset, int w, int h, byte[] gbaPalette)
            {
                var img = new MemImage(w, h);
                byte[] px = new byte[w * h * 4];
                int tilesX = w / 8;
                int tilesY = h / 8;
                int pos = offset;
                for (int ty = 0; ty < tilesY; ty++)
                {
                    for (int tx = 0; tx < tilesX; tx++)
                    {
                        for (int row = 0; row < 8; row++)
                        {
                            for (int colPair = 0; colPair < 4; colPair++)
                            {
                                if (pos >= tiles.Length) { img.SetPixelData(px); return img; }
                                byte bvz = tiles[pos++];
                                int idxLo = bvz & 0xF;
                                int idxHi = (bvz >> 4) & 0xF;
                                PlacePixel(px, w, gbaPalette, tx * 8 + colPair * 2, ty * 8 + row, idxLo);
                                PlacePixel(px, w, gbaPalette, tx * 8 + colPair * 2 + 1, ty * 8 + row, idxHi);
                            }
                        }
                    }
                }
                img.SetPixelData(px);
                return img;
            }

            void PlacePixel(byte[] px, int w, byte[] pal, int x, int y, int idx)
            {
                int p = (y * w + x) * 4;
                if (p + 3 >= px.Length) return;
                if (idx == 0)
                {
                    px[p] = 0; px[p + 1] = 0; px[p + 2] = 0; px[p + 3] = 0;
                    return;
                }
                int pi = idx * 2;
                ushort gba = (ushort)(pal[pi] | (pal[pi + 1] << 8));
                GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
                px[p] = r; px[p + 1] = g; px[p + 2] = b; px[p + 3] = 255;
            }

            public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new MemImage(w, h);
            public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new MemImage(w, h);
            public byte[] Encode4bppTiles(IImage i) => null;
            public byte[] Encode8bppTiles(IImage i) => null;
            public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
            public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
        }

        sealed class MemImage : IImage
        {
            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed => false;
            byte[] _pixels;

            public MemImage(int w, int h) { Width = w; Height = h; _pixels = new byte[w * h * 4]; }

            public byte[] GetPixelData() => _pixels;
            public void SetPixelData(byte[] data) { _pixels = data; }
            public byte[] GetPaletteGBA() => Array.Empty<byte>();
            public void SetPaletteGBA(byte[] p) { }
            public byte[] GetPaletteRGBA() => Array.Empty<byte>();
            public void Save(string f) { }
            public byte[] EncodePng() => Array.Empty<byte>();
            public void Dispose() { }
        }
    }
}
