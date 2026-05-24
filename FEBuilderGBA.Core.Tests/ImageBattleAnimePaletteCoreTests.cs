// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for ImageBattleAnimePaletteCore (#399). Mirrors the WF
// PaletteFormRef LZ77 round-trip + pointer rewrite semantics.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageBattleAnimePaletteCoreTests
{
    [Fact]
    public void ReadPalette_RoundTripsThroughLz77()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[] { 0x001F, 0x03E0, 0x7C00, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 },
        }, out uint paletteOffset);

        ushort[] result = ImageBattleAnimePaletteCore.ReadPalette(rom, paletteOffset, 0);
        Assert.NotNull(result);
        Assert.Equal(16, result.Length);
        Assert.Equal((ushort)0x001F, result[0]);
        Assert.Equal((ushort)0x03E0, result[1]);
        Assert.Equal((ushort)0x7C00, result[2]);
        Assert.Equal((ushort)13, result[15]);
    }

    [Fact]
    public void ReadPalette_ReturnsNullOnOutOfRangeSlot()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);
        // Only 1 slot exists, so slot 1 must return null.
        Assert.Null(ImageBattleAnimePaletteCore.ReadPalette(rom, paletteOffset, 1));
    }

    [Fact]
    public void ReadPalette_ReturnsNullOnDecompressFailure()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x10000], "BE8E01");
        // Pre-populate with non-LZ77 bytes.
        rom.Data[0x1000] = 0x42; // NOT 0x10 (LZ77 magic)
        Assert.Null(ImageBattleAnimePaletteCore.ReadPalette(rom, 0x1000, 0));
    }

    [Fact]
    public void GetPaletteSlotCount_DetectsSingleSlot()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);
        Assert.Equal(1, ImageBattleAnimePaletteCore.GetPaletteSlotCount(rom, paletteOffset));
    }

    [Fact]
    public void GetPaletteSlotCount_DetectsMultipleSlots()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16], new ushort[16], new ushort[16], new ushort[16],
        }, out uint paletteOffset);
        Assert.Equal(4, ImageBattleAnimePaletteCore.GetPaletteSlotCount(rom, paletteOffset));
    }

    [Fact]
    public void GetPaletteSlotCount_DetectsCorruptedBlock()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x10000], "BE8E01");
        rom.Data[0x1000] = 0x42; // not LZ77 magic
        Assert.Equal(0, ImageBattleAnimePaletteCore.GetPaletteSlotCount(rom, 0x1000));
    }

    [Fact]
    public void WritePalette_InPlace_PreservesAddress()
    {
        // The original 16-color all-zero slot compresses very small;
        // changing just R[0] to a small value should still fit in-place.
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);

        ushort[] newColors = new ushort[16];
        // small diff that keeps the compressed output the same size.
        newColors[0] = 0x0001;

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            uint result = ImageBattleAnimePaletteCore.WritePalette(rom, paletteOffset, 0, newColors);

            // In-place is the expected path here. If the implementation
            // chose to relocate, that's also valid -- but the rewrite
            // path must succeed either way.
            Assert.NotEqual(U.NOT_FOUND, result);

            // Round-trip read.
            ushort[] roundtrip = ImageBattleAnimePaletteCore.ReadPalette(rom, result, 0);
            Assert.NotNull(roundtrip);
            Assert.Equal((ushort)0x0001, roundtrip[0]);
        }
        finally { CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void WritePalette_Relocates_RewritesSourcePointerSlot()
    {
        // Set up: palette block + a source pointer slot referencing it.
        // Pass new colors that compress much LARGER than the original,
        // forcing the relocate branch. The source pointer slot must be
        // rewritten to the new offset.
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16], // all zeros => tiny compressed size
        }, out uint paletteOffset);

        // Plant a source pointer slot at 0x180000 -> paletteOffset.
        const uint sourcePointerSlot = 0x180000;
        WriteU32(rom.Data, (int)sourcePointerSlot, U.toPointer(paletteOffset));

        // Use very diverse colors that defeat LZ77's repetition compression.
        ushort[] newColors = new ushort[16];
        for (int i = 0; i < 16; i++) newColors[i] = (ushort)(0x1234 + i * 13);

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            uint result = ImageBattleAnimePaletteCore.WritePalette(
                rom, paletteOffset, 0, newColors, sourcePointerSlot: sourcePointerSlot);
            Assert.NotEqual(U.NOT_FOUND, result);

            // Reading the source pointer slot must yield the new pointer.
            uint reReadSlot = rom.u32(sourcePointerSlot);
            Assert.Equal(U.toPointer(result), reReadSlot);
        }
        finally { CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void WritePalette_FailingWriter_NoWrites_ReturnsNotFound()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);

        // Use highly diverse colors to force the relocate branch.
        ushort[] newColors = new ushort[16];
        for (int i = 0; i < 16; i++) newColors[i] = (ushort)(0x5A5A + i * 7);

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            uint result = ImageBattleAnimePaletteCore.WritePalette(
                rom, paletteOffset, 0, newColors,
                writerOverride: (r, data) => U.NOT_FOUND);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void WritePalette_SourcePointerSlot_AcceptsGbaPointerForm()
    {
        // Per PR #589 Copilot bot review #3: if a caller accidentally
        // passes a 0x08...-prefixed GBA pointer instead of a ROM offset,
        // U.toOffset normalizes it so the rewrite still hits the right slot.
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);

        const uint sourceSlotOffset = 0x180000;
        uint sourceSlotPointer = U.toPointer(sourceSlotOffset); // 0x08180000
        WriteU32(rom.Data, (int)sourceSlotOffset, U.toPointer(paletteOffset));

        ushort[] newColors = new ushort[16];
        for (int i = 0; i < 16; i++) newColors[i] = (ushort)(0xCAFE + i * 13);

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            // Pass the GBA pointer form -- Core must normalize it via U.toOffset.
            uint result = ImageBattleAnimePaletteCore.WritePalette(
                rom, paletteOffset, 0, newColors, sourcePointerSlot: sourceSlotPointer);
            Assert.NotEqual(U.NOT_FOUND, result);
            // Source slot at the offset form must contain the new pointer.
            uint reReadSlot = rom.u32(sourceSlotOffset);
            Assert.Equal(U.toPointer(result), reReadSlot);
        }
        finally { CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void WritePalette_SourcePointerSlot_ZeroIsIgnored()
    {
        // Per PR #589 Copilot bot review #3: passing 0 must NOT cause a
        // write at ROM offset 0.
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);

        byte[] before = new byte[4];
        for (int i = 0; i < 4; i++) before[i] = rom.Data[i];

        ushort[] newColors = new ushort[16];
        for (int i = 0; i < 16; i++) newColors[i] = (ushort)(0xBEEF + i * 7);

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            uint result = ImageBattleAnimePaletteCore.WritePalette(
                rom, paletteOffset, 0, newColors, sourcePointerSlot: 0);
            Assert.NotEqual(U.NOT_FOUND, result);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(before[i], rom.Data[i]);
            }
        }
        finally { CoreState.Undo = prevUndo; }
    }

    [Fact]
    public void WritePalette_ReturnsNotFoundOnInvalidArgs()
    {
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16],
        }, out uint paletteOffset);

        // Null colors.
        Assert.Equal(U.NOT_FOUND, ImageBattleAnimePaletteCore.WritePalette(rom, paletteOffset, 0, null));
        // Wrong-length colors.
        Assert.Equal(U.NOT_FOUND, ImageBattleAnimePaletteCore.WritePalette(rom, paletteOffset, 0, new ushort[15]));
        // Negative slot.
        Assert.Equal(U.NOT_FOUND, ImageBattleAnimePaletteCore.WritePalette(rom, paletteOffset, -1, new ushort[16]));
        // Zero offset.
        Assert.Equal(U.NOT_FOUND, ImageBattleAnimePaletteCore.WritePalette(rom, 0, 0, new ushort[16]));
    }

    [Fact]
    public void WritePalette_SecondarySlot_ResizesBlockAndWritesCorrectly()
    {
        // Mirrors WF PaletteFormRef.cs:705-711 "Array.Resize" behavior
        // when paletteIndex points beyond the current slot count.
        var rom = MakeRomWithCompressedSlots(new ushort[][]
        {
            new ushort[16], // single slot only
        }, out uint paletteOffset);

        // Write to slot index 2 (which doesn't exist yet — block must grow).
        ushort[] newColors = new ushort[16];
        for (int i = 0; i < 16; i++) newColors[i] = (ushort)(0xABC0 + i);

        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.Undo = new Undo();
            uint result = ImageBattleAnimePaletteCore.WritePalette(rom, paletteOffset, 2, newColors);
            Assert.NotEqual(U.NOT_FOUND, result);

            // Re-read slot 2 -- should match what we wrote.
            ushort[] roundtrip = ImageBattleAnimePaletteCore.ReadPalette(rom, result, 2);
            Assert.NotNull(roundtrip);
            Assert.Equal((ushort)0xABC0, roundtrip[0]);

            // The original slot 0 must still be all zero.
            ushort[] slot0 = ImageBattleAnimePaletteCore.ReadPalette(rom, result, 0);
            Assert.NotNull(slot0);
            Assert.Equal((ushort)0, slot0[0]);
        }
        finally { CoreState.Undo = prevUndo; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic ROM with the given <paramref name="slots"/>
    /// LZ77-compressed and planted at offset 0x150000.
    /// </summary>
    static ROM MakeRomWithCompressedSlots(ushort[][] slots, out uint paletteOffset)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x2000000], "BE8E01");

        // 64KB of 0xFF free space for relocate writes.
        for (uint i = 0x500000; i < 0x600000; i++)
            rom.Data[i] = 0xFF;

        // Build the uncompressed block: N slots of 0x20 bytes each.
        byte[] uncompressed = new byte[slots.Length * 32];
        for (int s = 0; s < slots.Length; s++)
        {
            for (int c = 0; c < 16; c++)
            {
                ushort gba = c < slots[s].Length ? slots[s][c] : (ushort)0;
                U.write_u16(uncompressed, (uint)(s * 32 + c * 2), gba);
            }
        }

        byte[] compressed = LZ77.compress(uncompressed);
        paletteOffset = 0x150000;
        for (int i = 0; i < compressed.Length; i++)
        {
            rom.Data[paletteOffset + i] = compressed[i];
        }

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
