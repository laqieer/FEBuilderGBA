// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapTerrainLookupCore — the Core-side extraction of
// MapTerrainBGLookupTableForm/MapTerrainFloorLookupTableForm pointer enumeration
// and ExtendsBattleBG patch detection. (#442 / #441)
//
// The tests do NOT require a real ROM — they construct synthetic ROM bytes that
// satisfy the byte patterns the WinForms originals scan for, so the parity
// between WF and Core can be asserted without disk I/O.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MapTerrainLookupCoreTests
{
    /// <summary>
    /// Helper: build a tiny synthetic FE8U ROM (just the header areas and the
    /// regions <see cref="MapTerrainLookupCore"/> reads) so the tests don't
    /// require a real ROM file.
    /// </summary>
    static ROM MakeSyntheticFe8u(bool extendsPatchInstalled, uint extendsTableOffset = 0x800000)
    {
        // FE8U is 0x1000000 bytes vanilla; we allocate a slightly larger
        // buffer so we can park the extends-table in known-safe space without
        // colliding with header / vanilla pointer slots.
        var bytes = new byte[0x1100000];

        if (extendsPatchInstalled)
        {
            // ExtendsBattleBG signature at FE8U 0x57ED0: { 0x00, 0xB5, 0x05, 0x4B, 0xC9, 0x00 }
            byte[] sig = { 0x00, 0xB5, 0x05, 0x4B, 0xC9, 0x00 };
            Array.Copy(sig, 0, bytes, 0x57ED0, sig.Length);

            // The extends-pointer-of-pointer at FE8U is 0x57EE8 (floor) /
            // 0x57EE4 (bg+4 inside the same 8-byte struct).
            // It contains a GBA pointer (addr | 0x08000000) to a table of
            // 8-byte structs. We park the table at extendsTableOffset.
            uint gbaPtr = extendsTableOffset | 0x08000000u;
            BitConverter.GetBytes(gbaPtr).CopyTo(bytes, 0x57EE8);
            BitConverter.GetBytes(gbaPtr).CopyTo(bytes, 0x57EE4);

            // Two extended entries followed by 0xFFFFFFFF terminator.
            // Each entry is 8 bytes; bytes 0..3 = floor pointer, 4..7 = bg pointer.
            uint extEntry0FloorPtr = 0x00200000u | 0x08000000u;
            uint extEntry0BgPtr = 0x00200100u | 0x08000000u;
            uint extEntry1FloorPtr = 0x00200200u | 0x08000000u;
            uint extEntry1BgPtr = 0x00200300u | 0x08000000u;

            BitConverter.GetBytes(extEntry0FloorPtr).CopyTo(bytes, (int)extendsTableOffset + 0);
            BitConverter.GetBytes(extEntry0BgPtr).CopyTo(bytes, (int)extendsTableOffset + 4);
            BitConverter.GetBytes(extEntry1FloorPtr).CopyTo(bytes, (int)extendsTableOffset + 8);
            BitConverter.GetBytes(extEntry1BgPtr).CopyTo(bytes, (int)extendsTableOffset + 12);
            // Terminator after 2 entries
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, (int)extendsTableOffset + 16);
        }

        var rom = new ROM();
        // LoadLow uses the version string passed in (not the bytes); we feed
        // "BE8E01" so RomInfo is wired to ROMFE8U for the synthetic data.
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // SearchExtendsBattleBG byte-pattern detection
    // -----------------------------------------------------------------

    [Fact]
    public void SearchExtendsBattleBG_VanillaRom_ReturnsNo()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: false);
        var result = PatchDetection.SearchExtendsBattleBG(rom);
        Assert.Equal(PatchDetection.ExtendsBattleBG_extends.NO, result);
    }

    [Fact]
    public void SearchExtendsBattleBG_PatchedRom_ReturnsExtends()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: true);
        var result = PatchDetection.SearchExtendsBattleBG(rom);
        Assert.Equal(PatchDetection.ExtendsBattleBG_extends.Extends, result);
    }

    [Fact]
    public void SearchExtendsBattleBG_NullRom_ReturnsNo()
    {
        var result = PatchDetection.SearchExtendsBattleBG((ROM?)null);
        Assert.Equal(PatchDetection.ExtendsBattleBG_extends.NO, result);
    }

    // -----------------------------------------------------------------
    // Pointer enumeration: vanilla (always 21 entries from RomInfo)
    // -----------------------------------------------------------------

    [Fact]
    public void GetPointersVanilla_Floor_ReturnsTwentyOneEntries()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: false);
        uint[] pointers = MapTerrainLookupCore.GetPointersVanilla(rom, isFloor: true);
        Assert.Equal(21, pointers.Length);
        // First entry MUST be lookup_table_battle_terrain_00_pointer
        Assert.Equal(rom.RomInfo.lookup_table_battle_terrain_00_pointer, pointers[0]);
    }

    [Fact]
    public void GetPointersVanilla_BG_ReturnsTwentyOneEntries()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: false);
        uint[] pointers = MapTerrainLookupCore.GetPointersVanilla(rom, isFloor: false);
        Assert.Equal(21, pointers.Length);
        Assert.Equal(rom.RomInfo.lookup_table_battle_bg_00_pointer, pointers[0]);
    }

    // -----------------------------------------------------------------
    // Pointer enumeration: extends patch (table-driven, >21 entries)
    // -----------------------------------------------------------------

    [Fact]
    public void GetPointersExtendsPatch_ReturnsExtendedEntries_Floor()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: true);
        // plus=0 → floor pointers (offset 0 inside each 8-byte struct).
        uint[] pointers = MapTerrainLookupCore.GetPointersExtendsPatch(rom, plus: 0);
        Assert.True(pointers.Length >= 2,
            $"Expected at least 2 entries from extends-patch table; got {pointers.Length}");
    }

    [Fact]
    public void GetPointersExtendsPatch_ReturnsExtendedEntries_BG()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: true);
        // plus=4 → bg pointers (offset 4 inside each 8-byte struct).
        uint[] pointers = MapTerrainLookupCore.GetPointersExtendsPatch(rom, plus: 4);
        Assert.True(pointers.Length >= 2,
            $"Expected at least 2 entries from extends-patch table (bg); got {pointers.Length}");
    }

    [Fact]
    public void GetPointersExtendsPatch_NoPatch_FallsBackToVanilla()
    {
        var rom = MakeSyntheticFe8u(extendsPatchInstalled: false);
        // Even if patch is not detected, the helper should still return the
        // vanilla 21-entry list rather than throwing.
        uint[] pointers = MapTerrainLookupCore.GetPointersExtendsPatch(rom, plus: 0);
        Assert.Equal(21, pointers.Length);
    }
}
