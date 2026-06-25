// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the TSA Animation Editor v2 "Import PNG" coupled-trio import (#1421).
//
// Regression guard: the buggy Avalonia path wrote ONLY a raw TSA blob to the
// per-entry P8 slot, leaving the shared header IMAGE (headerBase+16) and
// PALETTE (headerBase+4) stale and the new TSA indices pointing at unwritten
// tiles. The fix routes through ImageImportCore.Import3PointerHeaderTSA over the
// three coupled slots, mirroring WinForms ImageTSAAnime2Form's layout.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageTSAAnime2ImportTests
{
    // WF ImageTSAAnime2Form layout, relative to the shared header base
    // (dataAddr = p32(pointer)):
    //   palette pointer  @ headerBase + 4   (raw 0x20)
    //   image   pointer  @ headerBase + 16  (LZ77)
    //   per-entry list    @ headerBase + 20  (12 bytes/entry)
    //   entry-0 TSA ptr  @ headerBase + 20 + 8 (raw header-wrapped TSA)
    const uint HEADER_BASE = 0x00400000u;
    const uint PAL_SLOT = HEADER_BASE + 4;
    const uint IMG_SLOT = HEADER_BASE + 16;
    const uint TSA_SLOT = HEADER_BASE + 20 + 8;

    static ROM MakeRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("test.gba", bytes, "BE8E01");
        return rom;
    }

    [Fact]
    public void V2Import_WritesAllThreeCoupledPointers()
    {
        var prevRom = CoreState.ROM;
        var rom = MakeRom();
        try
        {
            CoreState.ROM = rom;

            // Seed the three slots with sentinel (non-pointer) values so we can
            // prove the import updated each one.
            rom.write_u32(PAL_SLOT, 0u);
            rom.write_u32(IMG_SLOT, 0u);
            rom.write_u32(TSA_SLOT, 0u);

            var pixels = new byte[256 * 160];
            var palette = new byte[32]; // 16 colors x 2 bytes

            var result = ImageImportCore.Import3PointerHeaderTSA(rom, pixels, palette,
                256, 160, IMG_SLOT, TSA_SLOT, PAL_SLOT, 0, 2);

            Assert.True(result.Success, $"Import failed: {result.Error}");

            // ALL THREE pointers must now resolve to in-ROM offsets (the bug left
            // image + palette untouched at 0).
            uint imgPtr = rom.p32(IMG_SLOT);
            uint palPtr = rom.p32(PAL_SLOT);
            uint tsaPtr = rom.p32(TSA_SLOT);
            Assert.True(U.isSafetyOffset(imgPtr), "image pointer not written");
            Assert.True(U.isSafetyOffset(palPtr), "palette pointer not written");
            Assert.True(U.isSafetyOffset(tsaPtr), "TSA pointer not written");

            // Image is LZ77 (magic 0x10); palette is raw (we wrote zeros);
            // TSA is raw header-wrapped (0x1D,0x13 for 256x160 margin 2).
            Assert.Equal((uint)0x10, rom.u8(imgPtr));
            Assert.Equal((uint)0x00, rom.u8(palPtr));
            Assert.Equal((uint)0x1D, rom.u8(tsaPtr + 0));
            Assert.Equal((uint)0x13, rom.u8(tsaPtr + 1));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void V2Import_TsaIsHeaderWrapped_NotRawDeduped()
    {
        var prevRom = CoreState.ROM;
        var rom = MakeRom();
        try
        {
            CoreState.ROM = rom;

            var pixels = new byte[256 * 160];
            var palette = new byte[32];

            var result = ImageImportCore.Import3PointerHeaderTSA(rom, pixels, palette,
                256, 160, IMG_SLOT, TSA_SLOT, PAL_SLOT, 0, 2);
            Assert.True(result.Success, $"Import failed: {result.Error}");

            uint tsaPtr = rom.p32(TSA_SLOT);
            // WF header byte length = 2 + (mhx+1)*(mhy+1)*2 = 2 + 30*20*2 = 1202.
            int mhx = (int)rom.u8(tsaPtr + 0);
            int mhy = (int)rom.u8(tsaPtr + 1);
            int len = 2 + (mhx + 1) * (mhy + 1) * 2;
            Assert.Equal(0x1D, mhx);
            Assert.Equal(0x13, mhy);
            Assert.Equal(1202, len);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// WinForms parity (Copilot CLI review on PR #1484): the v2 preview must
    /// render with skipTile0:false, because WF ByteToImage16TileInner skips ONLY
    /// 0xFFFF and EncodeTSA assigns the first unique tile the index 0. A
    /// header-TSA cell referencing tile 0 must therefore draw NONBLANK; with the
    /// (wrong) skipTile0:true default it would be left transparent.
    /// </summary>
    [Fact]
    public void DecodeHeaderTSA_Tile0Cell_RendersNonblank_WithSkipTile0False()
    {
        var prevSvc = CoreState.ImageService;
        try
        {
            CoreState.ImageService = new StubImageService();

            // 1x1-tile header-TSA whose single cell references tile index 0
            // (palette bank 0, no flip). header = {mhx=0, mhy=0}, then 1 cell.
            byte[] headerTsa = { 0x00, 0x00, 0x00, 0x00 };

            // One opaque 4bpp tile (index 0): all pixels = palette index 1.
            byte[] tileData = new byte[32];
            for (int i = 0; i < 32; i++) tileData[i] = 0x11; // both nibbles = 1

            // Palette: color 0 = transparent (0), color 1 = opaque white-ish.
            byte[] palette = new byte[32];
            palette[2] = 0xFF; palette[3] = 0x7F; // index 1 = 0x7FFF (white)

            var blank = ImageUtilCore.DecodeHeaderTSA(tileData, headerTsa, palette,
                1, 1, is4bpp: true, tsaAddend: 0, paletteShift: 0, skipTile0: true);
            var parity = ImageUtilCore.DecodeHeaderTSA(tileData, headerTsa, palette,
                1, 1, is4bpp: true, tsaAddend: 0, paletteShift: 0, skipTile0: false);

            Assert.NotNull(blank);
            Assert.NotNull(parity);

            // skipTile0:false (parity) draws tile 0 -> some opaque (alpha>0) pixels.
            Assert.True(HasOpaquePixel(parity.GetPixelData()),
                "skipTile0:false must draw the tile-0 cell (WinForms parity)");
            // skipTile0:true (the bug) leaves the tile-0 cell fully transparent.
            Assert.False(HasOpaquePixel(blank.GetPixelData()),
                "skipTile0:true wrongly blanks the tile-0 cell");
        }
        finally { CoreState.ImageService = prevSvc; }
    }

    static bool HasOpaquePixel(byte[] rgba)
    {
        if (rgba == null) return false;
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 0) return true;
        return false;
    }

    [Fact]
    public void V2Import_FailsCleanly_OnDegenerateMargin()
    {
        var prevRom = CoreState.ROM;
        var rom = MakeRom();
        try
        {
            CoreState.ROM = rom;
            var pixels = new byte[256 * 160];
            var palette = new byte[32];

            // margin out of range -> EncodeHeaderTSA degenerate -> import refuses.
            var result = ImageImportCore.Import3PointerHeaderTSA(rom, pixels, palette,
                256, 160, IMG_SLOT, TSA_SLOT, PAL_SLOT, 0, 9999);
            Assert.False(result.Success);
            // Slots must remain unwritten on rejection (validate-before-mutate).
            Assert.Equal(0u, rom.u32(IMG_SLOT));
            Assert.Equal(0u, rom.u32(PAL_SLOT));
            Assert.Equal(0u, rom.u32(TSA_SLOT));
        }
        finally { CoreState.ROM = prevRom; }
    }
}
