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
