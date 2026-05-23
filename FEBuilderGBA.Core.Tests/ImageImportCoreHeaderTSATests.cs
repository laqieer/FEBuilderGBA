// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for EncodeHeaderTSA + Import3PointerHeaderTSA (#429).
//
// These verify the byte-exact port of WinForms `ImageUtil.TSAToHeaderTSA`
// header layout (2-byte header + bottom-to-top row packing with margin
// accounting) and the raw-TSA / raw-palette import path that
// ImageBG / ImageCG editors require.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageImportCoreHeaderTSATests
{
    /// <summary>
    /// For the WF 256x160 BG layout with default margin=2:
    ///   masterHeaderX = (256/8) - 2 - 1 = 29 = 0x1D
    ///   masterHeaderY = (160/8) - 1      = 19 = 0x13
    /// </summary>
    [Fact]
    public void EncodeHeaderTSA_256x160_Margin2_HasExpectedHeaderBytes()
    {
        byte[] rawTsa = new byte[32 * 20 * 2]; // 32*20 tiles, 2 bytes each
        byte[] result = ImageImportCore.EncodeHeaderTSA(rawTsa, 256, 160, 2);
        Assert.NotNull(result);
        Assert.True(result.Length >= 2);
        Assert.Equal(0x1D, result[0]);
        Assert.Equal(0x13, result[1]);
    }

    /// <summary>
    /// The header output size matches the WF size formula:
    ///   size = 2 + (masterHeaderX + 1) * (masterHeaderY + 1) * 2
    /// For 256x160 margin=2 that's 2 + 30*20*2 = 1202 bytes.
    /// </summary>
    [Fact]
    public void EncodeHeaderTSA_OutputSizeMatchesWFFormula()
    {
        byte[] rawTsa = new byte[32 * 20 * 2];
        byte[] result = ImageImportCore.EncodeHeaderTSA(rawTsa, 256, 160, 2);
        int expectedSize = 2 + (29 + 1) * (19 + 1) * 2;
        Assert.Equal(expectedSize, result.Length);
    }

    /// <summary>
    /// Encode → DecodeHeaderTSA should produce a renderable image when fed
    /// a real (zeroed) image+palette set. The decoder reads the 2-byte
    /// header and uses the same bottom-to-top fill order as the WF encoder.
    /// </summary>
    [Fact]
    public void EncodeHeaderTSA_RoundTrip_ProducesDecodableImage()
    {
        // Skip if the test environment has no image backend wired.
        if (CoreState.ImageService == null)
            return;

        var rawTsa = new byte[32 * 20 * 2];
        for (int i = 0; i < rawTsa.Length; i += 2)
        {
            rawTsa[i] = (byte)(i / 2 & 0xFF);
            rawTsa[i + 1] = (byte)((i / 2 >> 8) & 0xFF);
        }
        byte[] headerTsa = ImageImportCore.EncodeHeaderTSA(rawTsa, 256, 160, 2);

        byte[] tileData = new byte[256 * 32]; // 256 tiles, 32 bytes each
        byte[] palette = new byte[32];        // 16 colors x 2 bytes

        var image = ImageUtilCore.DecodeHeaderTSA(tileData, headerTsa, palette, 32, 20, true);
        Assert.NotNull(image);
    }

    /// <summary>
    /// Import3PointerHeaderTSA writes three pointers and three data blobs
    /// into ROM, and the TSA blob is RAW (not LZ77). We verify by re-reading
    /// the first two bytes at the TSA location and checking they match the
    /// expected header (0x1D, 0x13 for 256x160 margin=2).
    /// </summary>
    [Fact]
    public void Import3PointerHeaderTSA_WritesRawHeaderTSA_NotCompressed()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("test.gba", bytes, "BE8E01");

        // U.isSafetyOffset reads CoreState.ROM.Data.Length — set it.
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Reserve 3 pointer slots in ROM at known addresses.
            uint imgPtrSlot = 0x00400000u;
            uint tsaPtrSlot = 0x00400004u;
            uint palPtrSlot = 0x00400008u;

            var pixels = new byte[256 * 160];
            var palette = new byte[32]; // 16 colors x 2 bytes

            var result = ImageImportCore.Import3PointerHeaderTSA(rom, pixels, palette,
                256, 160, imgPtrSlot, tsaPtrSlot, palPtrSlot, 0, 2);

            Assert.True(result.Success, $"Import failed: {result.Error}");

            // Read the TSA pointer back and check the first 2 bytes — they
            // MUST be the header (0x1D, 0x13). If TSA were LZ77-compressed
            // these would be the LZ77 magic byte 0x10 instead.
            uint tsaPtr = rom.p32(tsaPtrSlot);
            Assert.True(U.isSafetyOffset(tsaPtr));
            Assert.Equal((uint)0x1D, rom.u8(tsaPtr + 0));
            Assert.Equal((uint)0x13, rom.u8(tsaPtr + 1));

            // Palette is raw — first two bytes should be 0x00, 0x00 (we wrote
            // a zeroed palette). LZ77 compression would have produced 0x10
            // as the first byte.
            uint palPtr = rom.p32(palPtrSlot);
            Assert.True(U.isSafetyOffset(palPtr));
            Assert.Equal((uint)0x00, rom.u8(palPtr + 0));
            Assert.Equal((uint)0x00, rom.u8(palPtr + 1));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Import3PointerHeaderTSA rejects null arguments cleanly.
    /// </summary>
    [Fact]
    public void Import3PointerHeaderTSA_NullRom_ReturnsFailure()
    {
        var pixels = new byte[256 * 160];
        var palette = new byte[32];
        var result = ImageImportCore.Import3PointerHeaderTSA(null!, pixels, palette,
            256, 160, 0, 4, 8);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Import3PointerHeaderTSA rejects null pixel/palette args.
    /// </summary>
    [Fact]
    public void Import3PointerHeaderTSA_NullPixels_ReturnsFailure()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("test.gba", bytes, "BE8E01");

        var palette = new byte[32];
        var result = ImageImportCore.Import3PointerHeaderTSA(rom, null!, palette,
            256, 160, 0x00400000u, 0x00400004u, 0x00400008u);
        Assert.False(result.Success);
    }
}
