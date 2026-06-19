// SPDX-License-Identifier: GPL-3.0-or-later
// #1268 Core tests for FontAutoGenZHCore — the Avalonia ZH Font editor's
// ".ttf/.otf Auto-Generate" seam (rasterize one character into a 16x13 Chinese
// ROM glyph via the platform-neutral IFontRasterizer + write-back through
// FontGlyphZHCore.ImportGlyphZH).
//
// Core.Tests stays SkiaSharp-free: these tests inject a STUB IFontRasterizer that
// returns a deterministic packed 64-byte tile, so the rasterize -> unpack(16x13)
// -> ImportGlyphZH wiring is exercised without any real font rendering. The real
// FEBuilderGBA.SkiaSharp.SkiaFontRasterizer is covered via the Avalonia layer.
//
// Same synthetic multibyte FE8J ROM as FontGlyphZHCoreTests: a single serif glyph
// planted for '、' (moji 0x8181, codeB 0x54).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontAutoGenZHCoreTests
    {
        const uint ROM_LEN = 0x01000000;
        const uint MOJI_TEN = 0x8181;   // '、', codeB 0x54

        sealed class ImageServiceScope : IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        sealed class RomScope : IDisposable
        {
            readonly ROM _prev;
            public RomScope() { _prev = CoreState.ROM; }
            public void Dispose() { CoreState.ROM = _prev; }
        }

        // A stub rasterizer that returns a caller-supplied packed 64-byte tile and
        // advance width. Records the last call's arguments for thread-through asserts.
        sealed class StubRasterizer : IFontRasterizer
        {
            readonly byte[] _packed;
            readonly int _width;
            public FontSpec LastFont;
            public string LastCharacter = "";
            public bool LastIsItemFont;
            public int LastVerticalOffset;
            public int CallCount;

            public StubRasterizer(byte[] packed, int width) { _packed = packed; _width = width; }

            public byte[] RasterizeGlyph(FontSpec font, string character, bool isItemFont,
                int verticalOffset, out int glyphWidth)
            {
                CallCount++;
                LastFont = font;
                LastCharacter = character;
                LastIsItemFont = isItemFont;
                LastVerticalOffset = verticalOffset;
                glyphWidth = _width;
                return _packed;
            }
        }

        sealed class ThrowingRasterizer : IFontRasterizer
        {
            public byte[] RasterizeGlyph(FontSpec font, string character, bool isItemFont,
                int verticalOffset, out int glyphWidth)
                => throw new InvalidOperationException("boom");
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8J01"); // multibyte FE8J (version 8)

            PlantGlyph(rom, isItem: false, MOJI_TEN, width: 9);
            return rom;
        }

        static uint PlantGlyph(ROM rom, bool isItem, uint moji, uint width)
        {
            uint topaddress = FontGlyphZHCore.GetFontPointerZH(rom.RomInfo.version, isItem);
            uint codeB = FontGlyphZHCore.CalcCodeB(moji);
            uint addr = topaddress + codeB;
            U.write_u8(rom.Data, addr + 0, 0xD);
            U.write_u8(rom.Data, addr + 1, width);
            U.write_u8(rom.Data, addr + 2, 0xD);
            U.write_u8(rom.Data, addr + 3, 0);
            for (uint i = 0; i < 40; i++) rom.Data[addr + 4 + i] = 0x00;
            return addr;
        }

        // A deterministic packed 64-byte tile whose top 13 rows (52 bytes) carry a
        // recognizable 2bpp pattern, so the 16x13 unpack/write is non-empty.
        static byte[] MakePackedTile()
        {
            byte[] packed = new byte[64];
            for (int i = 0; i < 64; i++) packed[i] = 0xE4; // 0b11_10_01_00 -> px 0,1,2,3
            return packed;
        }

        [Fact]
        public void UnpackTo16x13_TakesTopRows_AllIndices()
        {
            byte[] packed = MakePackedTile(); // every byte = 0xE4 -> px 0,1,2,3
            byte[] idx = FontAutoGenZHCore.UnpackTo16x13(packed);
            Assert.Equal(FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H, idx.Length); // 16*13
            // Each group of 4 px is 0,1,2,3 (low-bits-first), repeated across the row.
            Assert.Equal(0, idx[0]);
            Assert.Equal(1, idx[1]);
            Assert.Equal(2, idx[2]);
            Assert.Equal(3, idx[3]);
            // A non-empty glyph (some foreground index > 0 exists).
            bool anyFg = false;
            foreach (byte b in idx) if (b > 0) { anyFg = true; break; }
            Assert.True(anyFg);
        }

        [Fact]
        public void AutoGenerateGlyphZH_WritesNonEmptyGlyph_PreservesWidth()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;

            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            byte[] before = rom.getBinaryData(addr + 4, 40);

            var raster = new StubRasterizer(MakePackedTile(), width: 11);
            var font = new FontSpec { FamilyName = "SimSun", Size = 12f };

            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom, raster, font,
                CHAR_OF(MOJI_TEN), MOJI_TEN, isItemFont: false, verticalOffset: 0);
            Assert.Equal("", err);

            // The 16x13 glyph (52 px of the 0xE4 pattern -> 40-byte 2bpp bitmap) was
            // written and is NON-EMPTY (differs from the zeroed planted bitmap).
            byte[] after = rom.getBinaryData(addr + 4, 40);
            Assert.NotEqual(before, after);
            bool anyNonZero = false;
            foreach (byte b in after) if (b != 0) { anyNonZero = true; break; }
            Assert.True(anyNonZero);

            // Serif preserves the rasterizer width verbatim (no -1 quirk).
            Assert.Equal(11u, rom.u8(addr + 1));

            // The character / font / offset were threaded through to the rasterizer.
            Assert.Equal(1, raster.CallCount);
            Assert.False(raster.LastIsItemFont);
            Assert.Equal(0, raster.LastVerticalOffset);
        }

        [Fact]
        public void AutoGenerateGlyphZH_ItemFont_SubtractsOneFromWidth()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            PlantGlyph(rom, isItem: true, MOJI_TEN, width: 9); // item slot too
            CoreState.ROM = rom;

            var raster = new StubRasterizer(MakePackedTile(), width: 8);
            var font = new FontSpec { FamilyName = "SimSun", Size = 12f };

            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom, raster, font,
                CHAR_OF(MOJI_TEN), MOJI_TEN, isItemFont: true, verticalOffset: 1);
            Assert.Equal("", err);

            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: true) + 0x54;
            // Item stores width-1 (the WF "+1" render quirk): 8 -> 7.
            Assert.Equal(7u, rom.u8(addr + 1));
            Assert.True(raster.LastIsItemFont);
            Assert.Equal(1, raster.LastVerticalOffset);
        }

        [Fact]
        public void AutoGenerateGlyphZH_RasterizerThrows_LocalizedError_NoMutation()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom, new ThrowingRasterizer(),
                new FontSpec { FamilyName = "SimSun", Size = 12f }, CHAR_OF(MOJI_TEN),
                MOJI_TEN, isItemFont: false, verticalOffset: 0);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data); // nothing written
        }

        [Fact]
        public void AutoGenerateGlyphZH_NonZHRom_ReturnsError_NoMutation()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U not multibyte
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom,
                new StubRasterizer(MakePackedTile(), 9),
                new FontSpec { FamilyName = "SimSun", Size = 12f }, "、",
                MOJI_TEN, isItemFont: false, verticalOffset: 0);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void AutoGenerateGlyphZH_NullRasterizer_ReturnsError()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom, null,
                new FontSpec { FamilyName = "SimSun", Size = 12f }, "、",
                MOJI_TEN, isItemFont: false, verticalOffset: 0);
            Assert.NotEqual("", err);
        }

        [Fact]
        public void AutoGenerateGlyphZH_EmptyCharacter_ReturnsError()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            string err = FontAutoGenZHCore.AutoGenerateGlyphZH(rom,
                new StubRasterizer(MakePackedTile(), 9),
                new FontSpec { FamilyName = "SimSun", Size = 12f }, "",
                MOJI_TEN, isItemFont: false, verticalOffset: 0);
            Assert.NotEqual("", err);
        }

        // The decoded character for the planted moji ('、').
        static string CHAR_OF(uint moji) => "、";
    }
}
