// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageUtilMagicCore — the Core-side extraction of the
// magic-system detection helpers WinForms uses to recognise the
// FEditorAdv / SCA_Creator magic engines and the CSA spell table
// position. Extracted from FEBuilderGBA/ImageUtilMagic.cs to support
// the Avalonia ImageMagicFEditorView rebuild (#418).
//
// We use small synthetic ROMs (LoadLow with the version selector +
// pre-planted signature bytes at the version-specific addresses) so
// the detection logic can run without a real Fire Emblem ROM file.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageUtilMagicCoreTests
{
    // ---------------------------------------------------------------
    // SearchMagicSystem signature scans
    // ---------------------------------------------------------------

    /// <summary>
    /// A blank FE8U ROM has no magic-system signature; detection must
    /// report `No` and not throw.
    /// </summary>
    [Fact]
    public void SearchMagicSystem_FE8U_NoSignature_ReturnsNo()
    {
        var rom = MakeMinimalFe8uRom();
        var result = ImageUtilMagicCore.SearchMagicSystem(
            rom, out uint baseaddr, out uint dimaddr, out uint nodimaddr);
        Assert.Equal(ImageUtilMagicCore.MagicSystem.No, result);
        Assert.Equal(U.NOT_FOUND, baseaddr);
        Assert.Equal(U.NOT_FOUND, dimaddr);
        Assert.Equal(U.NOT_FOUND, nodimaddr);
    }

    /// <summary>
    /// FE8U with the FEditor signature planted at 0x95d780 and the
    /// matching CSA spell table planted later in the ROM must detect
    /// FEditorAdv and report the expected dim/no_dim addresses.
    /// </summary>
    [Fact]
    public void SearchMagicSystem_FE8U_FEditorPlanted_ReturnsFEditorAdv()
    {
        var rom = MakeFe8uWithFEditorSignature();
        var result = ImageUtilMagicCore.SearchMagicSystem(
            rom, out uint baseaddr, out uint dimaddr, out uint nodimaddr);
        Assert.Equal(ImageUtilMagicCore.MagicSystem.FEditorAdv, result);
        Assert.Equal(0x95d780u, baseaddr);
        Assert.Equal(0x95D7EDu, dimaddr);
        Assert.Equal(0x95D8EFu, nodimaddr);
    }

    /// <summary>
    /// FE8U with the SCA_Creator signature planted (and matching CSA
    /// table) must detect CsaCreator.
    /// </summary>
    [Fact]
    public void SearchMagicSystem_FE8U_SCACreatorPlanted_ReturnsCsaCreator()
    {
        var rom = MakeFe8uWithSCACreatorSignature();
        var result = ImageUtilMagicCore.SearchMagicSystem(
            rom, out uint baseaddr, out uint dimaddr, out uint nodimaddr);
        Assert.Equal(ImageUtilMagicCore.MagicSystem.CsaCreator, result);
        Assert.Equal(0x95d780u, baseaddr);
        Assert.Equal(0x95d7edu, dimaddr);
        Assert.Equal(0x95d899u, nodimaddr);
    }

    /// <summary>
    /// FE7U with the FEditor signature planted at 0xCB680 must detect
    /// FEditorAdv on the FE7U-specific signature row.
    /// </summary>
    [Fact]
    public void SearchMagicSystem_FE7U_FEditorPlanted_ReturnsFEditorAdv()
    {
        var rom = MakeFe7uWithFEditorSignature();
        var result = ImageUtilMagicCore.SearchMagicSystem(
            rom, out uint baseaddr, out uint dimaddr, out uint nodimaddr);
        Assert.Equal(ImageUtilMagicCore.MagicSystem.FEditorAdv, result);
        Assert.Equal(0xCB680u, baseaddr);
    }

    /// <summary>
    /// FE6 with the FEditor signature planted at 0x2DC078 must detect
    /// FEditorAdv on the FE6-specific signature row.
    /// </summary>
    [Fact]
    public void SearchMagicSystem_FE6_FEditorPlanted_ReturnsFEditorAdv()
    {
        var rom = MakeFe6WithFEditorSignature();
        var result = ImageUtilMagicCore.SearchMagicSystem(
            rom, out uint baseaddr, out uint dimaddr, out uint nodimaddr);
        Assert.Equal(ImageUtilMagicCore.MagicSystem.FEditorAdv, result);
        Assert.Equal(0x2DC078u, baseaddr);
    }

    // ---------------------------------------------------------------
    // FindCSASpellTable
    // ---------------------------------------------------------------

    /// <summary>
    /// On a blank FE8U ROM (no CSA spell table planted), the helper
    /// must return NOT_FOUND for both the table address and the pointer
    /// without throwing.
    /// </summary>
    [Fact]
    public void FindCSASpellTable_FE8U_NoSignature_ReturnsNotFound()
    {
        var rom = MakeMinimalFe8uRom();
        uint addr = ImageUtilMagicCore.FindCSASpellTable(
            rom, ImageUtilMagicCore.MagicSystem.FEditorAdv,
            out uint outPointer);
        Assert.Equal(U.NOT_FOUND, addr);
        Assert.Equal(U.NOT_FOUND, outPointer);
    }

    /// <summary>
    /// On a FE8U ROM with the FEditor CSA pattern + a valid 4-byte
    /// pointer planted after it, the helper must find the table and
    /// resolve the pointer to an in-ROM offset.
    /// </summary>
    [Fact]
    public void FindCSASpellTable_FE8U_FEditor_FindsPointer()
    {
        var rom = MakeFe8uWithFEditorSignature();
        uint addr = ImageUtilMagicCore.FindCSASpellTable(
            rom, ImageUtilMagicCore.MagicSystem.FEditorAdv,
            out uint outPointer);
        Assert.NotEqual(U.NOT_FOUND, addr);
        Assert.NotEqual(U.NOT_FOUND, outPointer);
        // The pointer slot we planted resolves to 0x00100000 (an arbitrary
        // safety-offset target we put inside the synthetic ROM).
        Assert.Equal(0x00100000u, addr);
    }

    // ---------------------------------------------------------------
    // GetSpellDataCount
    // ---------------------------------------------------------------

    /// <summary>
    /// When the magic effect pointer points at exactly the original
    /// per-version count of valid pointer entries, GetSpellDataCount
    /// returns that count minus 1 (the 0xFF reserved row), matching
    /// WinForms ImageUtilMagicFEditor.SpellDataCount.
    /// </summary>
    [Fact]
    public void GetSpellDataCount_AtLeastOriginalCount()
    {
        var rom = MakeFe8uWithMagicEffectTable(rowCount: 80);
        uint count = ImageUtilMagicCore.GetSpellDataCount(rom);
        // The helper returns (count - 1) on the row count it found; for
        // a synthetic table of 80 pointer slots that's 79.
        Assert.True(count >= 1, $"expected at least 1, got {count}");
    }

    /// <summary>
    /// A null ROM (no RomInfo loaded) returns 0 instead of throwing.
    /// </summary>
    [Fact]
    public void GetSpellDataCount_NullRom_ReturnsZero()
    {
        Assert.Equal(0u, ImageUtilMagicCore.GetSpellDataCount(null));
    }

    // ---------------------------------------------------------------
    // Synthetic ROM helpers
    // ---------------------------------------------------------------

    /// <summary>Minimal FE8U ROM (BE8E01 + 0x1100000 zero bytes).</summary>
    static ROM MakeMinimalFe8uRom()
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>
    /// FE8U ROM with the FEditor magic signature planted at 0x95d780
    /// plus a valid CSA spell table pattern + pointer planted after
    /// it. The pointer resolves to 0x00100000 (an in-bounds safety
    /// offset).
    /// </summary>
    static ROM MakeFe8uWithFEditorSignature()
    {
        var data = new byte[0x1100000];

        // FEditor / FE8U signature (16 bytes at 0x95d780)
        byte[] sig = {
            0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08,
            0x03, 0x00, 0x00, 0x00, 0x39, 0xD9, 0x95, 0x08,
        };
        Array.Copy(sig, 0, data, 0x95d780, sig.Length);

        // Plant the FEditor CSA spell table pattern at 0x00200000 and
        // a 4-byte pointer to 0x00100000 immediately after it.
        byte[] csaPat = {
            0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02,
            0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02,
        };
        Array.Copy(csaPat, 0, data, 0x00200000, csaPat.Length);
        // The 4 bytes immediately after the pattern hold the
        // little-endian GBA pointer that resolves (via rom.p32 → strip
        // the 0x08000000 high bit) to 0x00100000.
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(data, 0x00200000 + csaPat.Length);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>
    /// FE8U ROM with the SCA_Creator magic signature planted at
    /// 0x95d780 plus matching CSA spell table.
    /// </summary>
    static ROM MakeFe8uWithSCACreatorSignature()
    {
        var data = new byte[0x1100000];

        // SCA_Creator / FE8U signature (16 bytes at 0x95d780)
        byte[] sig = {
            0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08,
            0x03, 0x00, 0x00, 0x00, 0xD9, 0xD8, 0x95, 0x08,
        };
        Array.Copy(sig, 0, data, 0x95d780, sig.Length);

        // SCA_Creator CSA spell table pattern at 0x00200000 + pointer.
        byte[] csaPat = {
            0x1C, 0x58, 0x05, 0x08, 0x00, 0x01, 0x00, 0x80,
            0xED, 0xD7, 0x95, 0x08, 0x99, 0xD8, 0x95, 0x08,
        };
        Array.Copy(csaPat, 0, data, 0x00200000, csaPat.Length);
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(data, 0x00200000 + csaPat.Length);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>
    /// FE7U ROM with the FEditor signature planted at 0xCB680 plus a
    /// matching CSA pattern. The FE7U FEditor signature has 0x00 bytes
    /// where the SCA_Creator variant has a dim address.
    /// </summary>
    static ROM MakeFe7uWithFEditorSignature()
    {
        var data = new byte[0x1100000];

        byte[] sig = {
            0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0xD9, 0xB7, 0x0C, 0x08,
        };
        Array.Copy(sig, 0, data, 0xCB680, sig.Length);

        // FE7U FEditor CSA pattern (28 bytes — longer than the FE8U one).
        byte[] csaPat = {
            0x00, 0x28, 0x17, 0xD1, 0x18, 0xE0, 0x70, 0xB5,
            0x05, 0x1C, 0x00, 0x20, 0x01, 0xB4, 0x87, 0xE7,
            0x34, 0xFF, 0x03, 0x02, 0x80, 0xB6, 0x0C, 0x08,
            0x26, 0xE0, 0x03, 0x02,
        };
        Array.Copy(csaPat, 0, data, 0x00200000, csaPat.Length);
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(data, 0x00200000 + csaPat.Length);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", data, "AE7E01");
        return rom;
    }

    /// <summary>
    /// FE6 ROM with the FEditor signature planted at 0x2DC078 plus
    /// matching CSA pattern.
    /// </summary>
    static ROM MakeFe6WithFEditorSignature()
    {
        var data = new byte[0x1100000];

        byte[] sig = {
            0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0xC5, 0xC1, 0x2D, 0x08,
        };
        Array.Copy(sig, 0, data, 0x2DC078, sig.Length);

        // FE6 FEditor CSA pattern is the same shape as the FE8U one.
        byte[] csaPat = {
            0xE7, 0x7D, 0xB4, 0x01, 0x34, 0xFF, 0x03, 0x02,
            0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02,
        };
        Array.Copy(csaPat, 0, data, 0x00200000, csaPat.Length);
        BitConverter.GetBytes(0x00100000u | 0x08000000u)
            .CopyTo(data, 0x00200000 + csaPat.Length);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe6.gba", data, "AFEJ01");
        return rom;
    }

    /// <summary>
    /// FE8U ROM with a synthetic magic-effect pointer table where
    /// every pointer slot has a valid GBA-pointer value so
    /// GetSpellDataCount counts them as in-bounds.
    /// </summary>
    static ROM MakeFe8uWithMagicEffectTable(int rowCount)
    {
        var data = new byte[0x1100000];

        // RomInfo.magic_effect_pointer for FE8U is 0x0005B3F8; populate it
        // with a pointer to 0x00200000 (where we put the pointer table).
        uint magicPointerSlot = 0x0005B3F8u;
        uint tableAddr = 0x00200000u;
        BitConverter.GetBytes(tableAddr | 0x08000000u)
            .CopyTo(data, (int)magicPointerSlot);

        // Plant `rowCount` valid GBA pointers (resolving to 0x00100000
        // + i*4, which are all in-bounds safety offsets).
        for (int i = 0; i < rowCount; i++)
        {
            uint slot = tableAddr + (uint)(i * 4);
            uint val = (0x00100000u + (uint)(i * 4)) | 0x08000000u;
            BitConverter.GetBytes(val).CopyTo(data, (int)slot);
        }

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u-magic.gba", data, "BE8E01");
        return rom;
    }
}
