// SPDX-License-Identifier: GPL-3.0-or-later
// #1032 Core tests for ClassOPDemoFontRenderCore — the cross-platform Class OP
// Demo N1 JP-name font-glyph render seam ported from WF
// OPClassFontForm.DrawFontByID + DrawFont.
//
// The synthetic ROM plants:
//   * op_class_font_pointer slot -> a table base
//   * at the base, N glyph-image POINTERS, each -> an LZ77-compressed 4x4-tile
//     (16 tiles x 32 bytes = 512-byte 4bpp glyph), then a 0 (non-pointer
//     terminator) at slot N
//   * op_class_font_palette_pointer slot -> a 16-color palette
//
// Core.Tests has a shared StubImageService (BattleAnimeDetailTests.cs) whose
// CreateImage + GBAColorToRGBA are real enough to validate that
// RenderGlyphById produces a non-null 32x32 IImage, plus all the null/blank
// guard paths.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ClassOPDemoFontRenderCoreTests
    {
        // High offsets (>= 0x200, < ROM length) so isSafetyOffset passes.
        const uint TableBase = 0x00500000u;
        const uint Glyph0Off = 0x00510000u;
        const uint Glyph1Off = 0x00520000u;
        const uint PaletteOff = 0x00530000u;

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

        // Build a synthetic FE8U ROM with a 2-glyph OP-class-font table.
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("x.gba", data, "BE8E01");

            // A 4x4-tile (32x32) 4bpp glyph = 16 tiles x 32 bytes = 512 bytes.
            byte[] rawGlyph = new byte[16 * 32];
            for (int i = 0; i < rawGlyph.Length; i++)
                rawGlyph[i] = (byte)0x11; // nibble 1 left+right => visible color index 1
            byte[] comp = LZ77.compress(rawGlyph);
            Array.Copy(comp, 0, rom.Data, Glyph0Off, comp.Length);
            Array.Copy(comp, 0, rom.Data, Glyph1Off, comp.Length);

            // Table base: slot0 -> Glyph0, slot1 -> Glyph1, slot2 -> 0 terminator.
            U.write_u32(rom.Data, TableBase + 0, U.toPointer(Glyph0Off));
            U.write_u32(rom.Data, TableBase + 4, U.toPointer(Glyph1Off));
            U.write_u32(rom.Data, TableBase + 8, 0); // non-pointer terminator

            // op_class_font_pointer slot -> table base.
            U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, U.toPointer(TableBase));

            // 16-color palette (32 bytes). index 0 transparent, index 1 = green.
            for (uint i = 0; i < 32; i++) rom.Data[PaletteOff + i] = 0;
            U.write_u16(rom.Data, PaletteOff + 1 * 2, 0x03E0); // index 1 = green
            U.write_u32(rom.Data, rom.RomInfo.op_class_font_palette_pointer, U.toPointer(PaletteOff));

            return rom;
        }

        [Fact]
        public void RenderGlyphById_Index0_Returns32x32NonNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 0);
                Assert.NotNull(img);
                Assert.Equal(32, img!.Width);
                Assert.Equal(32, img.Height);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_Index1_Returns32x32NonNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 1);
                Assert.NotNull(img);
                Assert.Equal(32, img!.Width);
                Assert.Equal(32, img.Height);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_TerminatorIndex_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Index 2 == the 0 terminator slot — past the contiguous run.
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 2);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_MaxUint_NoWraparound_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // A huge id whose base + id*4 would wrap in uint must NOT alias
                // back into the table — the bounded DataCount scan rejects it.
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, uint.MaxValue);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_NonPointerSlot0_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Wipe slot0 to a non-pointer (0) so the contiguous run is empty
                // (glyphCount == 0), and id 0 is therefore out of range.
                U.write_u32(rom.Data, TableBase + 0, 0);
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_ZeroPalettePointer_ReturnsNull_NoThrow()
        {
            // #1059 review: a 0 palette-pointer slot (non-FE8 ROMs) must return null,
            // NOT silently read palette bytes from offset 0. Also proves the guard
            // doesn't throw when the resolved palette offset is unsafe.
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                U.write_u32(rom.Data, rom.RomInfo.op_class_font_palette_pointer, 0);
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphById_NullRom_ReturnsNull()
        {
            using var svc = new ImageServiceScope();
            Assert.Null(ClassOPDemoFontRenderCore.RenderGlyphById(null, 0));
        }

        [Fact]
        public void RenderGlyphById_NullImageService_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.ImageService = null;
                Assert.Null(ClassOPDemoFontRenderCore.RenderGlyphById(rom, 0));
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderGlyphById_UnsafeTablePointer_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Point op_class_font_pointer at 0 (unsafe table base).
                U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, 0);
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphById(rom, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphImage_NonPointer_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // 0 is not a pointer -> WF BlankDummy -> null preview.
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphImage(rom, 0);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyphImage_ValidPointer_Returns32x32NonNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                using IImage img = ClassOPDemoFontRenderCore.RenderGlyphImage(rom, U.toPointer(Glyph0Off));
                Assert.NotNull(img);
                Assert.Equal(32, img!.Width);
                Assert.Equal(32, img.Height);
            }
            finally { CoreState.ROM = prevRom; }
        }
    }
}
