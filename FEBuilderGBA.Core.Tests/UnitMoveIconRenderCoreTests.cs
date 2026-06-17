// SPDX-License-Identifier: GPL-3.0-or-later
// #1177 Core tests for UnitMoveIconRenderCore — the cross-platform Unit Move
// Icon render seam (sibling of WaitIconRenderCore #991).
//
// Core.Tests does NOT reference SkiaSharp, so these use a minimal IImageService
// stub (correctly-SIZED indexed images + the SetPixelData/GetPixelData crop
// path). That validates: per-step crop RECTANGLES (32x32), palette-type
// resolution (0..4 non-null), invalid/non-pointer P0 -> null, huge index ->
// null (no uint overflow), and null-arg guards. REAL-PIXEL decode runs in the
// Avalonia test suite (which references SkiaSharp).
//
// Real-ROM tests skip when the ROM is unavailable (CI runners without a ROM).
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
    public class UnitMoveIconRenderCoreTests
    {
        readonly ITestOutputHelper _output;
        public UnitMoveIconRenderCoreTests(ITestOutputHelper output) => _output = output;

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

        [Theory]
        [InlineData("FE8U.gba")]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        public void RenderFullSheet_FirstValidIndex_NonNull(string romName)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom(romName);
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = UnitMoveIconRenderCore.RenderFullSheet(rom, 1, Svc, 0);
                Assert.NotNull(img);
                Assert.Equal(UnitMoveIconRenderCore.FRAME, img!.Width); // 32 wide
                Assert.True(img.Height > 0);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Theory]
        [InlineData("FE8U.gba")]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        public void RenderFrame_Step0_Is32x32(string romName)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom(romName);
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = UnitMoveIconRenderCore.RenderFrame(rom, 1, 0, Svc, 0);
                Assert.NotNull(img);
                Assert.Equal(32, img!.Width);
                Assert.Equal(32, img.Height);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderFrame_HigherSteps_AreAlso32x32_OrNullWhenShort()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                // Walk through several steps; each rendered frame must be 32x32.
                // A short sheet may legitimately fail the crop for a higher step.
                for (int step = 0; step <= 4; step++)
                {
                    using IImage img = UnitMoveIconRenderCore.RenderFrame(rom, 1, step, Svc, 0);
                    if (step == 0) Assert.NotNull(img);
                    if (img != null)
                    {
                        Assert.Equal(32, img.Width);
                        Assert.Equal(32, img.Height);
                    }
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
                byte[] pal = UnitMoveIconRenderCore.GetPaletteColors(rom, paletteType);
                Assert.NotNull(pal);
                Assert.True(pal!.Length > 0);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void GetPaletteColors_NullRom_ReturnsNull()
        {
            Assert.Null(UnitMoveIconRenderCore.GetPaletteColors(null, 0));
        }

        [Fact]
        public void RenderFrame_NullArgs_ReturnNull_NoThrow()
        {
            Assert.Null(UnitMoveIconRenderCore.RenderFrame(null, 0, 0, Svc, 0));
        }

        [Fact]
        public void RenderFrame_NegativeStep_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = UnitMoveIconRenderCore.RenderFrame(rom, 1, -1, Svc, 0);
                Assert.Null(img);
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
                using IImage img = UnitMoveIconRenderCore.RenderFrame(rom, 0xFFFFu, 0, Svc, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderFrame_HugeIndex_NoOverflow_ReturnsNull()
        {
            // A very large moveIconIndex whose `baseAddr + index*8` would wrap in
            // uint must NOT alias back into the table and produce a non-null
            // image. The overflow-safe ulong arithmetic in TryResolveEntry must
            // reject it.
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                using IImage img = UnitMoveIconRenderCore.RenderFrame(rom, 0xFFFFFFF0u, 0, Svc, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderMoveIcon_BadPicAddress_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;
                // A pointer into the GBA header danger zone is unsafe → null.
                using IImage img = UnitMoveIconRenderCore.RenderMoveIcon(rom, 0x08000010u, 0, Svc);
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
