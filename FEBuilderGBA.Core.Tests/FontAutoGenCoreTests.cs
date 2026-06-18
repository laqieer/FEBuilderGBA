// SPDX-License-Identifier: GPL-3.0-or-later
// #1232 Core tests for FontAutoGenCore — the Avalonia Font editor's
// ".ttf/.otf Auto-Generate" seam (rasterize one character into a ROM glyph via
// the platform-neutral IFontRasterizer + write-back through
// FontGlyphRenderCore.ImportGlyph).
//
// Core.Tests stays SkiaSharp-free: these tests inject a STUB IFontRasterizer
// that returns a deterministic packed 64-byte tile, so the rasterize -> unpack
// -> ImportGlyph wiring is exercised without any real font rendering. The real
// FEBuilderGBA.SkiaSharp.SkiaFontRasterizer is covered via the Avalonia layer.
//
// Same synthetic FE8U (LAT1) ROM as FontGlyphRenderCoreTests: a single 'A'
// (0x41) serif glyph planted in free space.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontAutoGenCoreTests
    {
        const uint ROM_LEN = 0x01000000;   // 16 MiB (font_serif_address in-bounds)
        const uint GLYPH_OFF = 0x00700000; // free space for the planted glyph struct
        const uint MOJI_A = 0x41;          // 'A'

        // Installs the shared StubImageService so RenderGlyphBytes can decode a
        // glyph back to RGBA for the pixel-for-pixel round-trip assertion.
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

        // A stub rasterizer that returns a caller-supplied packed 64-byte tile and
        // a caller-supplied advance width. Records the last call's arguments so the
        // tests can assert the character / font / offset were threaded through.
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

        // A rasterizer that always throws, to prove faults become localized errors.
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
            Array.Fill(data, (byte)0xFF); // 0xFF = free-space marker for the append path
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");

            PlantGlyph(rom, MOJI_A, GLYPH_OFF, width: 9, fillByte: 0x00);
            return rom;
        }

        static void PlantGlyph(ROM rom, uint moji, uint glyphOff, uint width, byte fillByte)
        {
            uint topaddress = rom.RomInfo.font_serif_address;
            uint bucket = topaddress + (moji << 2); // LAT1: hash by moji2
            U.write_u32(rom.Data, glyphOff + 0, 0);
            U.write_u8(rom.Data, glyphOff + 4, moji);
            U.write_u8(rom.Data, glyphOff + 5, width);
            U.write_u8(rom.Data, glyphOff + 6, 0);
            U.write_u8(rom.Data, glyphOff + 7, 0);
            for (uint i = 0; i < 64; i++) rom.Data[glyphOff + 8 + i] = fillByte;
            U.write_u32(rom.Data, bucket, U.toPointer(glyphOff));
        }

        // A deterministic packed 64-byte tile: pixel index = (x % 4), packed
        // 4 px/byte low-bits-first => every byte = 0b11_10_01_00 = 0xE4. Its
        // rightmost non-zero pixel is x=15 (index 3), so the derived width is 16 —
        // but we pass an explicit width that must win.
        static byte[] MakePackedTile()
        {
            byte[] idx = new byte[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    idx[y * 16 + x] = (byte)(x & 0x03);
            return FontGlyphRenderCore.PackGlyphBytes(idx);
        }

        static FontSpec FileFontSpec() => new FontSpec
        {
            FamilyName = "TestFamily",
            Size = 14f,
            FontFilePath = "/some/font.ttf",
        };

        // ---- undo replay helpers (mirrors FontGlyphRenderCoreTests) ----

        static Undo.UndoData NewUd(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "font-autogen-test",
            list = new System.Collections.Generic.List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        static void RollbackReplay(ROM rom, Undo.UndoData ud)
        {
            for (int i = ud.list.Count - 1; i >= 0; i--)
            {
                var up = ud.list[i];
                Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
            }
        }

        // ===================== Tests =====================

        [Fact]
        public void AutoGenerateGlyph_ExistingGlyph_WritesRasterizedBytes()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] packed = MakePackedTile();
                var stub = new StubRasterizer(packed, width: 7);

                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                    "A", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.Equal("", err);

                // The rasterized 64 bytes landed in the existing glyph slot at +8.
                Assert.Equal(packed, rom.getBinaryData(GLYPH_OFF + 8, 64));
                // The rasterizer's advance width was written verbatim.
                Assert.Equal(7u, rom.u8(GLYPH_OFF + 5));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_ThreadsCharFontOffsetToRasterizer()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var stub = new StubRasterizer(MakePackedTile(), width: 5);
                FontSpec spec = FileFontSpec();

                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, spec,
                    "A", MOJI_A, isItemFont: true, verticalOffset: 3);
                Assert.Equal("", err);

                Assert.Equal(1, stub.CallCount);
                Assert.Equal("A", stub.LastCharacter);
                Assert.True(stub.LastIsItemFont);
                Assert.Equal(3, stub.LastVerticalOffset);
                Assert.Equal("/some/font.ttf", stub.LastFont.FontFilePath);
                Assert.Equal(14f, stub.LastFont.Size);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_NewGlyph_AppendsAndChainLinks()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                const uint MOJI_B = 0x42; // 'B' — no glyph yet (empty bucket)
                uint bucket = rom.RomInfo.font_serif_address + (MOJI_B << 2);
                Assert.Equal(0u, rom.u32(bucket));

                var stub = new StubRasterizer(MakePackedTile(), width: 8);
                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                    "B", MOJI_B, isItemFont: false, verticalOffset: 0);
                Assert.Equal("", err);

                uint ptr = rom.u32(bucket);
                Assert.True(U.isPointer(ptr));
                var list = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false);
                Assert.Contains(list, g => g.Moji == MOJI_B);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_ExistingGlyph_UndoRestoresByteIdentical()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                var stub = new StubRasterizer(MakePackedTile(), width: 7);
                var ud = NewUd(rom);
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.Equal("", FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                        "A", MOJI_A, isItemFont: false, verticalOffset: 0));
                }
                Assert.NotEqual(before, rom.Data); // generation mutated the ROM
                Assert.NotEmpty(ud.list);          // and recorded undo positions

                RollbackReplay(rom, ud);
                Assert.Equal(before, rom.Data);    // undo restored it byte-identical
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_NewGlyph_AppendUndoRestoresByteIdentical()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                const uint MOJI_B = 0x42;
                var stub = new StubRasterizer(MakePackedTile(), width: 8);
                var ud = NewUd(rom);
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.Equal("", FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                        "B", MOJI_B, isItemFont: false, verticalOffset: 0));
                }
                Assert.NotEqual(before, rom.Data);
                Assert.NotEmpty(ud.list);

                RollbackReplay(rom, ud);
                Assert.Equal(before, rom.Data); // append + bucket repoint both undone
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_RasterizerThrows_ReturnsError_NoMutation()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();

                string err = FontAutoGenCore.AutoGenerateGlyph(rom, new ThrowingRasterizer(),
                    FileFontSpec(), "A", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.NotEqual("", err);          // localized error, no throw
                Assert.Equal(snap, rom.Data);      // ZERO mutation
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_NullRasterizer_ReturnsError()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                string err = FontAutoGenCore.AutoGenerateGlyph(rom, null, FileFontSpec(),
                    "A", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.NotEqual("", err);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_NullRom_ReturnsError_NoThrow()
        {
            var stub = new StubRasterizer(MakePackedTile(), width: 7);
            var ex = Record.Exception(() =>
                FontAutoGenCore.AutoGenerateGlyph(null, stub, FileFontSpec(),
                    "A", MOJI_A, isItemFont: false, verticalOffset: 0));
            Assert.Null(ex);
        }

        [Fact]
        public void AutoGenerateGlyph_EmptyCharacter_ReturnsError()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                var stub = new StubRasterizer(MakePackedTile(), width: 7);
                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                    "", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.NotEqual("", err);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void AutoGenerateGlyph_ShortRasterOutput_ReturnsError_NoMutation()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();

                // A rasterizer that returns a too-short buffer (< 64 bytes).
                var stub = new StubRasterizer(new byte[10], width: 7);
                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                    "A", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.NotEqual("", err);
                Assert.Equal(snap, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // ---- unpack round-trip ----

        [Fact]
        public void UnpackGlyphBytes_RoundTripsPackGlyphBytes()
        {
            byte[] idx = new byte[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    idx[y * 16 + x] = (byte)((x + y) & 0x03);

            byte[] packed = FontGlyphRenderCore.PackGlyphBytes(idx);
            byte[] unpacked = FontAutoGenCore.UnpackGlyphBytes(packed);
            Assert.Equal(idx, unpacked);
        }

        // The real proof the 2bpp bit-order / nibble convention is right: feed the
        // stub rasterizer a packed tile built from a DISTINCT, ASYMMETRIC index
        // pattern (so a transposed/row-reversed/bit-swapped unpack would change the
        // result), run AutoGenerateGlyph, then RENDER the resulting ROM glyph and
        // map each pixel back to its palette index — it must reproduce the original
        // pattern pixel-for-pixel. (rasterize -> unpack -> ImportGlyph -> render.)
        [Fact]
        public void AutoGenerateGlyph_AsymmetricPattern_RoundTripsPixelForPixel()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Asymmetric, all-4-colors pattern: index varies with BOTH x and y
                // and is NOT symmetric under transpose or row/column reversal.
                byte[] original = new byte[16 * 16];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        original[y * 16 + x] = (byte)((x + 2 * y) & 0x03);
                // Plant a couple of unique single-pixel marks so a flip is unmissable.
                original[0 * 16 + 1] = 3;   // top row, x=1
                original[15 * 16 + 0] = 2;  // bottom-left corner
                original[1 * 16 + 15] = 1;  // near top-right

                byte[] packed = FontGlyphRenderCore.PackGlyphBytes(original);
                Assert.NotNull(packed);

                var stub = new StubRasterizer(packed, width: 16);
                string err = FontAutoGenCore.AutoGenerateGlyph(rom, stub, FileFontSpec(),
                    "A", MOJI_A, isItemFont: false, verticalOffset: 0);
                Assert.Equal("", err);

                // Render the ROM glyph and map each RGBA pixel back to its index.
                using IImage img = FontGlyphRenderCore.RenderGlyph(rom, GLYPH_OFF, isItemFont: false);
                Assert.NotNull(img);
                byte[] rgba = img!.GetPixelData();

                byte[] roundTripped = new byte[16 * 16];
                for (int p = 0; p < 16 * 16; p++)
                {
                    byte r = rgba[p * 4 + 0];
                    byte a = rgba[p * 4 + 3];
                    roundTripped[p] = RgbaToFontIndex(r, a);
                }
                Assert.Equal(original, roundTripped);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // Inverse of FontGlyphRenderCore's render palette (serif/text font):
        // index 0 = transparent; 1 = Gray (R 0xA8); 2 = White (R 0xF8); 3 = Black
        // (R 0x28). Identify by alpha first, then by the red channel.
        static byte RgbaToFontIndex(byte r, byte a)
        {
            if (a == 0) return 0;
            if (r == 0xF8) return 2;
            if (r == 0x28) return 3;
            return 1; // 0xA8 (Gray)
        }
    }
}
