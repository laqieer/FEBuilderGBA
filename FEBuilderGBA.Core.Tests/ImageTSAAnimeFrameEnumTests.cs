// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageTSAAnimeFrameEnumCore.EnumerateFrames (#1457).
//
// Regression guard: the Avalonia TSA Animation editor (v1) used to list only the
// first 12-byte entry (frame 0) per category and discard the FRAMECOUNT column,
// so frames 1..N-1 were invisible/uneditable. EnumerateFrames mirrors WinForms
// ImageTSAAnimeForm's ReInitPointer(pointer, count) — one row per FRAMECOUNT frame
// at base + i*12.
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageTSAAnimeFrameEnumTests
{
    // A category pointer slot well inside ROM, and a frame-list base it points to.
    const uint POINTER_SLOT = 0x00200000u; // holds GBA ptr to FRAME_BASE
    const uint FRAME_BASE = 0x00300000u;   // start of the 12-byte frame entries

    static ROM MakeRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("test.gba", bytes, "BE8E01");
        return rom;
    }

    // Build a one-category dict whose FRAMECOUNT (col 0) is the given hex string.
    static Dictionary<uint, string[]> OneCategory(string frameCountHex, string name)
        => new() { [U.toPointer(POINTER_SLOT)] = new[] { frameCountHex, name } };

    static ROM MakeRomWithCategory()
    {
        var rom = MakeRom();
        // Point the category slot at FRAME_BASE (GBA pointer).
        rom.write_p32(POINTER_SLOT, FRAME_BASE);
        return rom;
    }

    [Fact]
    public void EnumeratesAllFrames_NotJustFrameZero()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeRomWithCategory();
            CoreState.ROM = rom;

            // FRAMECOUNT = 0x14 (20) — the count is hex in the config file.
            var dict = OneCategory("014", "lightning");
            var rows = ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict);

            Assert.Equal(20, rows.Count);

            // Frame 0 present at the base ...
            Assert.Equal(FRAME_BASE, rows[0].addr);
            // ... AND every later frame at base + i*12 (the old bug stopped at frame 0).
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(FRAME_BASE + (uint)i * 12u, rows[i].addr);
                Assert.Equal((uint)i, rows[i].tag);
                // Row name carries the frame index so the user can tell frames apart.
                Assert.Contains(" " + i, rows[i].name);
                Assert.Contains("lightning", rows[i].name);
            }
            // Frames 1..N-1 are reachable — distinct addresses, no duplicates.
            Assert.NotEqual(rows[0].addr, rows[1].addr);
            Assert.NotEqual(rows[1].addr, rows[19].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void NoTwentyEntryCap()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeRomWithCategory();
            CoreState.ROM = rom;

            // FRAMECOUNT = 0x1A (26) — the old code capped lists at 20.
            var dict = OneCategory("01A", "seal");
            var rows = ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict);

            Assert.Equal(26, rows.Count);
            Assert.Equal(FRAME_BASE + 25u * 12u, rows[25].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void BoundsGuard_TruncatesAtRomEnd()
    {
        var prev = CoreState.ROM;
        try
        {
            // Small ROM so a large FRAMECOUNT would overrun.
            var bytes = new byte[FRAME_BASE + 12 * 3 + 4]; // room for exactly 3 frames
            var rom = new ROM();
            rom.LoadLow("small.gba", bytes, "BE8E01");
            rom.write_p32(POINTER_SLOT, FRAME_BASE);
            CoreState.ROM = rom;

            var dict = OneCategory("014", "lightning"); // asks for 20
            var rows = ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict);

            // Only the frames that fit are returned (no out-of-range addresses).
            Assert.Equal(3, rows.Count);
            Assert.Equal(FRAME_BASE + 2u * 12u, rows[2].addr);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void EmptyOrNullInput_ReturnsEmpty()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeRom();
            CoreState.ROM = rom;

            Assert.Empty(ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, null));
            Assert.Empty(ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, new Dictionary<uint, string[]>()));
            Assert.Empty(ImageTSAAnimeFrameEnumCore.EnumerateFrames(null, OneCategory("014", "x")));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ZeroFrameCount_SkipsCategory()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeRomWithCategory();
            CoreState.ROM = rom;

            var dict = OneCategory("000", "empty");
            Assert.Empty(ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void PointerSlotInLastBytes_DoesNotThrow()
    {
        var prev = CoreState.ROM;
        try
        {
            // ROM whose only category pointer slot sits in the final 2 bytes, so a
            // 4-byte p32 read would overrun. The helper must skip it, not throw
            // (the "never throws" contract). #1457 review finding.
            uint romLen = 0x00100000u;
            var bytes = new byte[romLen];
            var rom = new ROM();
            rom.LoadLow("edge.gba", bytes, "BE8E01");
            CoreState.ROM = rom;

            uint badSlot = romLen - 2; // ptrOff + 4 > romLen
            var dict = new Dictionary<uint, string[]>
            {
                [U.toPointer(badSlot)] = new[] { "014", "edge" },
            };

            var ex = Record.Exception(() => ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict));
            Assert.Null(ex);
            // The malformed category is skipped → empty result.
            Assert.Empty(ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void MultipleCategories_SequentialTagsAcrossCategories()
    {
        var prev = CoreState.ROM;
        try
        {
            var rom = MakeRom();
            // Two categories, each pointing at its own frame base.
            uint slotA = 0x00200000u, baseA = 0x00300000u;
            uint slotB = 0x00210000u, baseB = 0x00310000u;
            rom.write_p32(slotA, baseA);
            rom.write_p32(slotB, baseB);
            CoreState.ROM = rom;

            var dict = new Dictionary<uint, string[]>
            {
                [U.toPointer(slotA)] = new[] { "003", "alpha" }, // 3 frames
                [U.toPointer(slotB)] = new[] { "002", "beta" },  // 2 frames
            };

            var rows = ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, dict);
            Assert.Equal(5, rows.Count);
            // Sequential tag across categories (matches old list-index semantics).
            for (int i = 0; i < rows.Count; i++) Assert.Equal((uint)i, rows[i].tag);
        }
        finally { CoreState.ROM = prev; }
    }
}
