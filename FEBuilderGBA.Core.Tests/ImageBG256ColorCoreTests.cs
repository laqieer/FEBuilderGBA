// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBG256ColorCore — the Core-side 255/224-color cutscene
// background import + preview-decode pipeline under the FE8 "BG256Color"
// patch (#799).
//
// Acceptance gates (from the approved plan v2):
//   * Import255 (255): P0 LZ77 round-trips to source tiles; P4 == 0 as RAW
//     u32 at p4Addr; P8 == 512-byte palette; undo rollback restores all three.
//   * Import224: indices 0..223 → exact forward-map output; P4 == 1; a
//     pre-remap index >= 224 returns Error (no silent blackout).
//   * Short palette pads to exactly 512 bytes.
//   * Convert255ColorTo224Color / Convert224ColorTo255Color parity across
//     {0,31,32,223,224,255} (round-trip where defined).
//   * Decode255ColorBG parity for P4=0 and P4=1 reproduces the indexed image
//     (224 applies inverse remap) — exact-byte (8bpp is deterministic).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageBG256ColorCoreTests
{
    // -----------------------------------------------------------------
    // Functional 8bpp image service (deterministic — no AA). Mirrors the
    // exact tile order of SkiaImageService.Encode8bppTiles /
    // Decode8bppTiles (row-major tiles, row-major within each 8x8 tile)
    // so the encode/decode round-trip is byte-exact. The MinimalImageService
    // / StubImageService doubles return null/no-op for encode/decode, which
    // is insufficient for these round-trip assertions.
    // -----------------------------------------------------------------

    sealed class Bg8bppImage : IImage
    {
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => true;
        byte[] _pixels;
        byte[] _palGba;

        public Bg8bppImage(int w, int h, byte[] palGba)
        {
            Width = w; Height = h;
            _pixels = new byte[w * h];
            _palGba = palGba ?? Array.Empty<byte>();
        }

        public byte[] GetPixelData() => _pixels;
        public void SetPixelData(byte[] data) => _pixels = data;
        public byte[] GetPaletteGBA() => _palGba;
        public void SetPaletteGBA(byte[] gbaPalette) => _palGba = gbaPalette;
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string filePath) { }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }

    sealed class Bg8bppImageService : IImageService
    {
        public IImage CreateImage(int w, int h) => new Bg8bppImage(w, h, null);
        public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new Bg8bppImage(w, h, p);
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

        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => null;

        public IImage Decode8bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
        {
            var image = new Bg8bppImage(width, height, gbaPalette);
            byte[] pixels = new byte[width * height];
            int tileW = width / 8, tileH = height / 8, pos = offset;
            for (int ty = 0; ty < tileH; ty++)
                for (int tx = 0; tx < tileW; tx++)
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px++)
                        {
                            if (pos >= tileData.Length) goto done;
                            int x = tx * 8 + px, y = ty * 8 + py;
                            if (x < width && y < height) pixels[y * width + x] = tileData[pos];
                            pos++;
                        }
            done:
            image.SetPixelData(pixels);
            return image;
        }

        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => null;
        public byte[] Encode4bppTiles(IImage i) => null;

        public byte[] Encode8bppTiles(IImage image)
        {
            byte[] pixels = image.GetPixelData();
            int width = image.Width, height = image.Height;
            int tileW = width / 8, tileH = height / 8;
            byte[] result = new byte[tileW * tileH * 64];
            int pos = 0;
            for (int ty = 0; ty < tileH; ty++)
                for (int tx = 0; tx < tileW; tx++)
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px++)
                        {
                            int x = tx * 8 + px, y = ty * 8 + py;
                            if (pos < result.Length)
                                result[pos++] = (x < width && y < height) ? pixels[y * width + x] : (byte)0;
                        }
            return result;
        }

        public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
    }

    static Undo.UndoData NewUndo(string name = "test") => new Undo.UndoData
    {
        time = DateTime.Now,
        name = name,
        list = new System.Collections.Generic.List<Undo.UndoPostion>(),
        filesize = 0,
    };

    /// <summary>
    /// ROM with the second half filled with 0xFF so the import helpers find
    /// free space for P0/P8.
    /// </summary>
    static ROM MakeFreeSpaceRom(int size = 0x200000)
    {
        var rom = new ROM();
        var data = new byte[size];
        for (int i = size / 2; i < size; i++) data[i] = 0xFF;
        rom.SwapNewROMDataDirect(data);
        return rom;
    }

    /// <summary>BG dimensions used by every test (32x20 tiles = 256x160).</summary>
    const int W = ImageBG256ColorCore.Width;
    const int H = ImageBG256ColorCore.Height;

    /// <summary>Build a deterministic 256x160 indexed image with indices in [0, maxIndex].</summary>
    static byte[] MakeIndexedImage(int maxIndex)
    {
        byte[] px = new byte[W * H];
        for (int i = 0; i < px.Length; i++)
            px[i] = (byte)(i % (maxIndex + 1));
        return px;
    }

    /// <summary>Build a deterministic 512-byte (256-color) GBA palette.</summary>
    static byte[] Make512Palette()
    {
        byte[] pal = new byte[512];
        for (int i = 0; i < 256; i++)
        {
            ushort c = (ushort)((i * 7 + 1) & 0x7FFF);
            pal[i * 2] = (byte)(c & 0xFF);
            pal[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
        }
        return pal;
    }

    // =================================================================
    // Forward / inverse 224 remap parity
    // =================================================================

    [Theory]
    [InlineData(0, 0)]     // < 32 keep
    [InlineData(31, 31)]   // < 32 keep
    [InlineData(32, 64)]   // 32..223 -> +32
    [InlineData(223, 255)] // 32..223 -> +32 (top of range)
    [InlineData(224, 0)]   // >= 224 -> 0
    [InlineData(255, 0)]   // >= 224 -> 0
    public void Convert255To224_MatchesWFRemap(int input, int expected)
    {
        byte[] img = { (byte)input };
        ImageBG256ColorCore.Convert255ColorTo224Color(img);
        Assert.Equal((byte)expected, img[0]);
    }

    [Theory]
    [InlineData(0, 0)]     // < 32 keep
    [InlineData(31, 31)]   // < 32 keep
    [InlineData(32, 0)]    // >= 32 -> -32
    [InlineData(64, 32)]   // >= 32 -> -32
    [InlineData(255, 223)] // >= 32 -> -32
    public void Convert224To255_MatchesWFInverse(int input, int expected)
    {
        byte[] img = { (byte)input };
        ImageBG256ColorCore.Convert224ColorTo255Color(img);
        Assert.Equal((byte)expected, img[0]);
    }

    [Theory]
    [InlineData(32)]   // forward 32 -> 64 -> inverse -> 32
    [InlineData(100)]  // forward 100 -> 132 -> inverse -> 100
    [InlineData(223)]  // forward 223 -> 255 -> inverse -> 223
    public void RemapRoundTrip_DefinedDomain_32To223(int input)
    {
        // Forward map is defined+invertible on 32..223 (it lands in 64..255).
        byte[] img = { (byte)input };
        ImageBG256ColorCore.Convert255ColorTo224Color(img);
        ImageBG256ColorCore.Convert224ColorTo255Color(img);
        Assert.Equal((byte)input, img[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void RemapRoundTrip_LowReservedColors_AreUnchanged(int input)
    {
        byte[] img = { (byte)input };
        ImageBG256ColorCore.Convert255ColorTo224Color(img);
        Assert.Equal((byte)input, img[0]); // forward keeps < 32
        ImageBG256ColorCore.Convert224ColorTo255Color(img);
        Assert.Equal((byte)input, img[0]); // inverse keeps < 32
    }

    // =================================================================
    // PadPaletteTo512
    // =================================================================

    [Fact]
    public void PadPaletteTo512_ShortPalette_ZeroExtendsTo512()
    {
        byte[] shortPal = new byte[32]; // 16 colors
        for (int i = 0; i < shortPal.Length; i++) shortPal[i] = (byte)(i + 1);
        byte[] padded = ImageBG256ColorCore.PadPaletteTo512(shortPal);

        Assert.Equal(512, padded.Length);
        for (int i = 0; i < 32; i++) Assert.Equal(shortPal[i], padded[i]);
        for (int i = 32; i < 512; i++) Assert.Equal(0, padded[i]);
    }

    [Fact]
    public void PadPaletteTo512_LongPalette_TruncatesTo512()
    {
        byte[] longPal = new byte[600];
        for (int i = 0; i < longPal.Length; i++) longPal[i] = (byte)(i & 0xFF);
        byte[] padded = ImageBG256ColorCore.PadPaletteTo512(longPal);

        Assert.Equal(512, padded.Length);
        for (int i = 0; i < 512; i++) Assert.Equal(longPal[i], padded[i]);
    }

    [Fact]
    public void PadPaletteTo512_Null_ReturnsZeroed512()
    {
        byte[] padded = ImageBG256ColorCore.PadPaletteTo512(null);
        Assert.Equal(512, padded.Length);
        Assert.All(padded, b => Assert.Equal(0, b));
    }

    // =================================================================
    // Import255ColorBG — 255-color (P4 = 0)
    // =================================================================

    [Fact]
    public void Import255_WritesP0Lz77_P4RawZero_P8Palette512_AndRoundTrips()
    {
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var svc = new Bg8bppImageService();

            byte[] indexed = MakeIndexedImage(255);     // full 0..255 range
            byte[] pal = Make512Palette();

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;

            var undo = NewUndo();
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, p0, p4, p8, is224: false, svc, undo);

            Assert.True(result.Success, result.Error);

            // P4 == 0 as a RAW u32 (NOT a relocated pointer).
            Assert.Equal(0u, rom.u32(p4));

            // P8 → 512-byte palette, byte-for-byte.
            uint palPtr = rom.u32(p8);
            Assert.True(U.isPointer(palPtr));
            uint palOff = U.toOffset(palPtr);
            for (int i = 0; i < 512; i++) Assert.Equal(pal[i], rom.Data[palOff + i]);

            // P0 → LZ77 data that decompresses back to the encoded 8bpp tiles.
            uint imgPtr = rom.u32(p0);
            Assert.True(U.isPointer(imgPtr));
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(imgPtr));

            // Expected = svc.Encode8bppTiles(image with indexed pixels).
            var expectImage = svc.CreateIndexedImage(W, H, pal, 256);
            expectImage.SetPixelData(indexed);
            byte[] expectedTiles = svc.Encode8bppTiles(expectImage);
            Assert.Equal(expectedTiles.Length, decompressed.Length);
            Assert.Equal(expectedTiles, decompressed);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Import255_UndoRollback_RestoresP0_P4_P8()
    {
        // UndoPostion(addr,size) captures the "before" bytes from
        // CoreState.ROM, and Undo.Rollback patches them back into
        // CoreState.ROM — so the rollback path requires CoreState.ROM to be
        // the same ROM we import into.
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var svc = new Bg8bppImageService();
            byte[] indexed = MakeIndexedImage(200);
            byte[] pal = Make512Palette();

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;

            // Snapshot the full image region before import so we can verify a
            // total restore (pointers + data blobs).
            byte[] before = (byte[])rom.Data.Clone();

            var undo = new Undo();
            var undoData = undo.NewUndoData("bg255-rollback");
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, p0, p4, p8, is224: false, svc, undoData);
            Assert.True(result.Success, result.Error);

            // Something actually changed.
            Assert.NotEqual(0u, rom.u32(p0));

            // Roll back the recorded write set.
            Assert.True(undoData.list.Count > 0);
            undo.Rollback(undoData);

            // All three pointer slots + the data regions are restored.
            Assert.Equal(before.Length, rom.Data.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Import255_CapturesUndo_EvenWithoutAmbientScope()
    {
        // The method must open its own ambient scope when none is active, so
        // WriteCompressedToROM / WriteRawToROM writes (which use the no-undo
        // rom.write_* overloads) are still captured (reviewer reminder).
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var svc = new Bg8bppImageService();
            byte[] indexed = MakeIndexedImage(100);
            byte[] pal = Make512Palette();

            Assert.Null(ROM.GetAmbientUndoData()); // no scope open

            var undo = NewUndo();
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, 0x100, 0x104, 0x108, is224: false, svc, undo);
            Assert.True(result.Success, result.Error);

            // P0 (image), P4 (flag), P8 (palette) + their data blobs all recorded.
            Assert.True(undo.list.Count >= 3);

            // Scope must be closed again after the call returns.
            Assert.Null(ROM.GetAmbientUndoData());
        }
        finally { CoreState.ROM = prevRom; }
    }

    // =================================================================
    // Import255ColorBG — 224-color (P4 = 1)
    // =================================================================

    [Fact]
    public void Import224_AppliesForwardMap_AndWritesP4One()
    {
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var svc = new Bg8bppImageService();

            // Indices 0..223 only (the BG224 contract).
            byte[] indexed = MakeIndexedImage(223);
            byte[] pal = Make512Palette();

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;

            var undo = NewUndo();
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, p0, p4, p8, is224: true, svc, undo);
            Assert.True(result.Success, result.Error);

            // P4 == 1 (224-color mode flag), raw u32.
            Assert.Equal(1u, rom.u32(p4));

            // P0 must hold the FORWARD-mapped tiles. Build the expected output by
            // applying Convert255ColorTo224Color to a copy then encoding.
            byte[] expectIdx = (byte[])indexed.Clone();
            ImageBG256ColorCore.Convert255ColorTo224Color(expectIdx);
            var expectImage = svc.CreateIndexedImage(W, H, pal, 256);
            expectImage.SetPixelData(expectIdx);
            byte[] expectedTiles = svc.Encode8bppTiles(expectImage);

            uint imgPtr = rom.u32(p0);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(imgPtr));
            Assert.Equal(expectedTiles, decompressed);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Import224_PreRemapIndexAt224OrAbove_ReturnsError_NoBlackout()
    {
        ROM rom = MakeFreeSpaceRom();
        var svc = new Bg8bppImageService();

        // Inject an index 224 somewhere — this would silently map to 0.
        byte[] indexed = MakeIndexedImage(223);
        indexed[1000] = 224;
        byte[] pal = Make512Palette();

        uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;
        uint origP4 = rom.u32(p4);

        var undo = NewUndo();
        var result = ImageBG256ColorCore.Import255ColorBG(
            rom, indexed, pal, W, H, p0, p4, p8, is224: true, svc, undo);

        Assert.False(result.Success);
        Assert.Contains("224", result.Error);
        // No writes should have happened — P4 slot unchanged.
        Assert.Equal(origP4, rom.u32(p4));
        Assert.Empty(undo.list);
    }

    [Fact]
    public void Import255_ShortPalette_PadsP8To512Bytes()
    {
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var svc = new Bg8bppImageService();
            byte[] indexed = MakeIndexedImage(15);
            byte[] shortPal = new byte[32]; // only 16 colors
            for (int i = 0; i < shortPal.Length; i++) shortPal[i] = (byte)(i + 1);

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;

            var undo = NewUndo();
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, shortPal, W, H, p0, p4, p8, is224: false, svc, undo);
            Assert.True(result.Success, result.Error);

            uint palOff = U.toOffset(rom.u32(p8));
            // First 32 bytes are the supplied colors; bytes 32..511 are zero.
            for (int i = 0; i < 32; i++) Assert.Equal(shortPal[i], rom.Data[palOff + i]);
            for (int i = 32; i < 512; i++) Assert.Equal(0, rom.Data[palOff + i]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // =================================================================
    // Import255ColorBG — argument guards
    // =================================================================

    [Fact]
    public void Import255_NullRom_ReturnsError()
    {
        var svc = new Bg8bppImageService();
        var result = ImageBG256ColorCore.Import255ColorBG(
            null, MakeIndexedImage(15), Make512Palette(), W, H, 0x100, 0x104, 0x108, false, svc, NewUndo());
        Assert.False(result.Success);
    }

    [Fact]
    public void Import255_WrongPixelCount_ReturnsError()
    {
        ROM rom = MakeFreeSpaceRom();
        var svc = new Bg8bppImageService();
        byte[] tooSmall = new byte[W * H - 1];
        var result = ImageBG256ColorCore.Import255ColorBG(
            rom, tooSmall, Make512Palette(), W, H, 0x100, 0x104, 0x108, false, svc, NewUndo());
        Assert.False(result.Success);
    }

    // =================================================================
    // Decode255ColorBG — preview parity for P4=0 and P4=1
    // =================================================================

    [Fact]
    public void Decode255_P4Zero_ReproducesIndexedImage_ExactBytes()
    {
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom; // Decode255ColorBG uses single-arg U.isSafetyOffset
            var svc = new Bg8bppImageService();
            byte[] indexed = MakeIndexedImage(255);
            byte[] pal = Make512Palette();

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, p0, p4, p8, is224: false, svc, NewUndo());
            Assert.True(result.Success, result.Error);

            IImage decoded = ImageBG256ColorCore.Decode255ColorBG(rom, rom.u32(p0), rom.u32(p8), is224: false, svc);
            Assert.NotNull(decoded);
            Assert.Equal(W, decoded.Width);
            Assert.Equal(H, decoded.Height);
            // 8bpp decode is deterministic — pixel indices match exactly.
            Assert.Equal(indexed, decoded.GetPixelData());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Decode224_P4One_AppliesInverseRemap_ReproducesForwardMappedImage()
    {
        ROM rom = MakeFreeSpaceRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom; // Decode255ColorBG uses single-arg U.isSafetyOffset
            var svc = new Bg8bppImageService();
            byte[] indexed = MakeIndexedImage(223); // <= 224-color contract
            byte[] pal = Make512Palette();

            uint p0 = 0x000100, p4 = 0x000104, p8 = 0x000108;
            var result = ImageBG256ColorCore.Import255ColorBG(
                rom, indexed, pal, W, H, p0, p4, p8, is224: true, svc, NewUndo());
            Assert.True(result.Success, result.Error);

            IImage decoded = ImageBG256ColorCore.Decode255ColorBG(rom, rom.u32(p0), rom.u32(p8), is224: true, svc);
            Assert.NotNull(decoded);

            // The ROM stored forward-mapped indices; Decode applies the inverse
            // remap, so the decoded pixels equal forward(indexed) then inverse,
            // which equals the original indexed image on the 0..223 domain.
            byte[] forward = (byte[])indexed.Clone();
            ImageBG256ColorCore.Convert255ColorTo224Color(forward);
            byte[] expected = (byte[])forward.Clone();
            ImageBG256ColorCore.Convert224ColorTo255Color(expected);
            Assert.Equal(expected, decoded.GetPixelData());

            // And because forward/inverse round-trips on 0..223, the decoded
            // image reproduces the ORIGINAL indexed image exactly.
            Assert.Equal(indexed, decoded.GetPixelData());
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Decode255_NullRom_ReturnsNull()
    {
        var svc = new Bg8bppImageService();
        Assert.Null(ImageBG256ColorCore.Decode255ColorBG(null, 0x08000100, 0x08000200, false, svc));
    }

    [Fact]
    public void Decode255_NonPointerP0_ReturnsNull()
    {
        ROM rom = MakeFreeSpaceRom();
        var svc = new Bg8bppImageService();
        Assert.Null(ImageBG256ColorCore.Decode255ColorBG(rom, 0, 0x08000200, false, svc));
    }
}
