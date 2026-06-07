// SPDX-License-Identifier: GPL-3.0-or-later
// #991 Core tests for WaitIconRenderCore — the cross-platform Unit Wait Icon
// render seam extracted VERBATIM from PreviewIconHelper.LoadClassWaitIcon.
//
// Core.Tests does NOT reference SkiaSharp, so these tests use a minimal
// IImageService stub (SyntheticImageService) that returns correctly-SIZED
// indexed images and supports the crop SetPixelData/GetPixelData path. That
// is enough to validate: per-animType crop RECTANGLES (sizes for steps 0/1/2),
// palette-type resolution (0..4 non-null / FE6 lightrune/sepia blank),
// invalid/non-pointer P4 → blank, and short-strip → blank. REAL-PIXEL decode
// + the PreviewIconHelper oracle pixel-equality run in the Avalonia test suite
// (which references SkiaSharp).
//
// Real-ROM tests skip when the ROM is unavailable (CI runners / dev machines
// without the commercial ROM).
using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WaitIconRenderCoreTests
    {
        readonly ITestOutputHelper _output;
        public WaitIconRenderCoreTests(ITestOutputHelper output) => _output = output;

        static string? FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        ROM? LoadRom(string romName)
        {
            string? path = FindRom(romName);
            if (path == null) { _output.WriteLine($"SKIP: {romName} not found"); return null; }
            var rom = new ROM();
            rom.Load(path, out _);
            return rom;
        }

        static readonly IImageService Svc = new SyntheticImageService();

        // Locate a wait-icon index of the given animType in the real ROM.
        static uint? FindIndexWithAnimType(ROM rom, byte want)
        {
            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return null;
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 8;
                if (addr + 8 > (uint)rom.Data.Length) break;
                uint sprite = rom.u32(addr + 4);
                if (!U.isPointer(sprite)) break;
                if ((byte)rom.u8(addr + 2) == want) return i;
            }
            return null;
        }

        [Theory]
        [InlineData((byte)0, 16, 16)]
        [InlineData((byte)1, 16, 24)]
        [InlineData((byte)2, 32, 32)]
        public void RenderFrame_AnimType_ProducesExpectedSize_Steps012(byte animType, int w, int h)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;

                uint? idx = FindIndexWithAnimType(rom, animType);
                if (idx == null) { _output.WriteLine($"SKIP: no wait icon with animType {animType}"); return; }

                for (int step = 0; step <= 2; step++)
                {
                    using IImage img = WaitIconRenderCore.RenderFrame(rom, idx.Value, step, Svc, 0);
                    // A short strip can legitimately fail the crop for higher
                    // steps; assert size only when an image was produced. Step 0
                    // must always render.
                    if (step == 0)
                        Assert.NotNull(img);
                    if (img != null)
                    {
                        Assert.Equal(w, img.Width);
                        Assert.Equal(h, img.Height);
                    }
                }
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderFrame_Step0_Self_GeometryEquals_RenderClassWaitIcon()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;

                // RenderClassWaitIcon MUST equal RenderFrame(step:0, self) — this
                // is the delegation contract PreviewIconHelper.LoadClassWaitIcon
                // relies on. With the synthetic decoder both share identical
                // (blank) pixels, so this asserts the crop GEOMETRY is identical.
                for (uint idx = 1; idx < 40; idx++)
                {
                    using IImage a = WaitIconRenderCore.RenderClassWaitIcon(rom, idx, Svc, 0);
                    using IImage b = WaitIconRenderCore.RenderFrame(rom, idx, 0, Svc, 0);
                    if (a == null && b == null) continue;
                    Assert.NotNull(a);
                    Assert.NotNull(b);
                    Assert.Equal(a!.Width, b!.Width);
                    Assert.Equal(a.Height, b.Height);
                    Assert.Equal(a.GetPixelData(), b.GetPixelData());
                }
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GetPaletteColors_Types0to4_NonNull_FE8U(int paletteType)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                byte[] pal = WaitIconRenderCore.GetPaletteColors(rom, paletteType);
                Assert.NotNull(pal);
                Assert.True(pal!.Length > 0);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Theory]
        [InlineData(5)] // lightrune
        [InlineData(6)] // sepia
        public void GetPaletteColors_LightruneSepia_BlankOnFE6(int paletteType)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE6.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                // FE6 has no lightrune/sepia palette address (0) → null guard.
                byte[] pal = WaitIconRenderCore.GetPaletteColors(rom, paletteType);
                Assert.Null(pal);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderFrame_OutOfRangeIndex_ReturnsNull_NoThrow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                // A huge index lands out of the table bounds → blank/null.
                using IImage img = WaitIconRenderCore.RenderFrame(rom, 0xFFFFu, 0, Svc, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderFullSheet_FirstValidIndex_NonNull_FE8U()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = WaitIconRenderCore.RenderFullSheet(rom, 1, Svc, 0);
                Assert.NotNull(img);
                Assert.True(img!.Width is 16 or 32);
                Assert.True(img.Height > 0);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void GetPaletteColors_NullRom_ReturnsNull()
        {
            Assert.Null(WaitIconRenderCore.GetPaletteColors(null, 0));
        }

        [Fact]
        public void RenderFrame_NullArgs_ReturnNull_NoThrow()
        {
            Assert.Null(WaitIconRenderCore.RenderFrame(null, 0, 0, Svc, 0));
        }

        [Fact]
        public void RenderFrame_HugeIndex_NoOverflow_ReturnsNull()
        {
            // #993 Copilot review: a very large waitIconIndex whose
            // `baseAddr + index*8` would wrap in uint must NOT alias back into
            // the table and produce a non-null image. The overflow-safe ulong
            // arithmetic in TryResolveEntry must reject it.
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = WaitIconRenderCore.RenderFrame(rom, 0xFFFFFFF0u, 0, Svc, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // Minimal IImageService stub: correctly-sized indexed images + the
        // SetPixelData/GetPixelData round-trip the crop path needs.
        // -----------------------------------------------------------------
        sealed class SyntheticImageService : IImageService
        {
            public IImage CreateImage(int width, int height) => new Img(width, height, null);
            public IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
                => new Img(width, height, gbaPalette);
            public IImage LoadImage(string filePath) => new Img(16, 16, null);
            public IImage LoadImageFromBytes(byte[] pngData) => new Img(16, 16, null);
            public void GBAColorToRGBA(ushort c, out byte r, out byte g, out byte b)
            { r = (byte)((c & 0x1F) << 3); g = (byte)(((c >> 5) & 0x1F) << 3); b = (byte)(((c >> 10) & 0x1F) << 3); }
            public ushort RGBAToGBAColor(byte r, byte g, byte b)
                => (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
            public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => new Img(w, h, p);
            public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new Img(w, h, p);
            public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new Img(w, h, p);
            public byte[] Encode4bppTiles(IImage image) => Array.Empty<byte>();
            public byte[] Encode8bppTiles(IImage image) => Array.Empty<byte>();
            public byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount) => new byte[colorCount * 4];
            public byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount) => new byte[colorCount * 2];
        }

        sealed class Img : IImage
        {
            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed => true;
            byte[] _pixels;
            byte[] _palette;
            public Img(int w, int h, byte[] pal) { Width = w; Height = h; _pixels = new byte[w * h]; _palette = pal ?? new byte[32]; }
            public byte[] GetPixelData() => (byte[])_pixels.Clone();
            public void SetPixelData(byte[] data) { _pixels = (byte[])data.Clone(); }
            public byte[] GetPaletteGBA() => (byte[])_palette.Clone();
            public void SetPaletteGBA(byte[] palette) { _palette = (byte[])palette.Clone(); }
            public byte[] GetPaletteRGBA() => new byte[(_palette.Length / 2) * 4];
            public void Save(string filePath) { File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); }
            public byte[] EncodePng() => new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            public void Dispose() { }
        }
    }
}
