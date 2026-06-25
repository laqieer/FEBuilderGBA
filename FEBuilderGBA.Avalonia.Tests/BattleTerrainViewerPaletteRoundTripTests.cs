// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the standalone PAL Import/Export round-trip on
// BattleTerrainViewerViewModel (#1417). The BattleTerrain palette at +16 is
// stored RAW (0x20 bytes = one 16-color bank), NOT LZ77-compressed — matching
// WinForms ImageBattleTerrainForm (Draw reads palette raw; ImportButton_Click
// writes palette with LZ77=false; MakeAllDataLength registers +16 as
// DataTypeEnum.PAL size 0x20). The previous Avalonia code wrongly
// LZ77-decompressed on Export and LZ77-compressed on Import, corrupting the
// ROM palette and producing a broken export file.
using System;
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Marked [Collection("SharedState")] because the import path mutates
/// CoreState.ROM / CoreState.Undo. Without serialization, xUnit's parallel
/// runner could race a sibling test's CoreState swap.
/// </summary>
[Collection("SharedState")]
public class BattleTerrainViewerPaletteRoundTripTests
{
    // -----------------------------------------------------------------
    // Export: reads the RAW 0x20 palette bytes — NOT an LZ77 decompress.
    // -----------------------------------------------------------------

    [Fact]
    public void ExportPaletteBytes_ReturnsRaw32Bytes_ByteIdentical()
    {
        var (rom, entryAddr, palOffset) = MakeFe8uRomWithBattleTerrain();
        byte[] knownPalette = MakeKnownPalette();
        Array.Copy(knownPalette, 0, rom.Data, palOffset, 0x20);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);

            byte[] exported = vm.ExportPaletteBytes();
            Assert.NotNull(exported);
            Assert.Equal(0x20, exported!.Length);
            Assert.Equal(knownPalette, exported); // byte-identical RAW read
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExportPaletteBytes_ReturnsNull_WhenPalettePointerInvalid()
    {
        var (rom, entryAddr, _) = MakeFe8uRomWithBattleTerrain();
        // Clobber the +16 palette pointer with a non-pointer value.
        WriteU32(rom.Data, (int)(entryAddr + 16), 0x00000000u);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);
            Assert.Null(vm.ExportPaletteBytes());
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Import: writes RAW palette + repoints +16 — NOT an LZ77 compress.
    // -----------------------------------------------------------------

    [Fact]
    public void ImportPaletteBytes_WritesRaw_AndRepoints_RoundTripsByteIdentical()
    {
        var (rom, entryAddr, _) = MakeFe8uRomWithBattleTerrain();
        byte[] newPalette = MakeKnownPalette();

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);

            Assert.True(vm.ImportPaletteBytes(newPalette, out string err), $"import must succeed; err={err}");

            // +16 now repointed to a fresh region.
            uint newPtr = rom.u32(entryAddr + 16);
            Assert.True(U.isPointer(newPtr));
            uint newOff = U.toOffset(newPtr);

            // The written region is the RAW palette, byte-identical (NOT LZ77).
            byte[] written = new byte[0x20];
            Array.Copy(rom.Data, newOff, written, 0, 0x20);
            Assert.Equal(newPalette, written);

            // It is NOT an LZ77 blob: first byte of an LZ77 header is 0x10.
            // (Our palette[0] is deliberately != 0x10.)
            Assert.NotEqual(0x10, rom.Data[newOff]);

            // Round-trip: Export reads back the SAME bytes raw.
            byte[] exported = vm.ExportPaletteBytes();
            Assert.NotNull(exported);
            Assert.Equal(newPalette, exported);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot plan-review item 1: an oversized palette (e.g. a 256-color .act
    /// or a multi-bank raw palette) must be sliced to the FIRST 16 colors so
    /// the region written at +16 is always exactly 0x20 bytes — never a
    /// 512-byte blob.
    /// </summary>
    [Fact]
    public void ImportPaletteBytes_OversizedInput_WritesExactly32Bytes()
    {
        var (rom, entryAddr, _) = MakeFe8uRomWithBattleTerrain();
        // 256-color raw palette = 512 bytes; first 16 colors are our known bank.
        byte[] big = new byte[512];
        byte[] known = MakeKnownPalette();
        Array.Copy(known, 0, big, 0, 0x20);
        // Make the tail distinct so a buggy full-write would be detectable.
        for (int i = 0x20; i < big.Length; i++) big[i] = 0xAB;

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);

            Assert.True(vm.ImportPaletteBytes(big, out string err), $"oversized import must succeed; err={err}");

            uint newOff = U.toOffset(rom.u32(entryAddr + 16));
            // The first 0x20 bytes equal the first 16 colors.
            byte[] written = new byte[0x20];
            Array.Copy(rom.Data, newOff, written, 0, 0x20);
            Assert.Equal(known, written);
            // The byte AFTER the 0x20 region must NOT be the oversized tail
            // value 0xAB — i.e. the writer stopped at exactly 0x20 bytes.
            Assert.NotEqual(0xAB, rom.Data[newOff + 0x20]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ImportPaletteBytes_RejectsTooSmall_NoMutation()
    {
        var (rom, entryAddr, _) = MakeFe8uRomWithBattleTerrain();
        byte[] snapshot = (byte[])rom.Data.Clone();

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);

            Assert.False(vm.ImportPaletteBytes(new byte[16], out string err));
            Assert.Contains("32", err);
            Assert.True(BytesEqual(snapshot, rom.Data), "ROM must be unchanged on a too-small palette");
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// The import is undoable under an ambient undo scope — the +16 pointer is
    /// restored after RunUndo (parity with the WinForms undo path).
    /// </summary>
    [Fact]
    public void ImportPaletteBytes_Undoable_RestoresPointer()
    {
        var (rom, entryAddr, _) = MakeFe8uRomWithBattleTerrain();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new BattleTerrainViewerViewModel();
            vm.LoadBattleTerrain(entryAddr);
            uint prePtr = rom.u32(entryAddr + 16);

            var undoData = CoreState.Undo.NewUndoData("test battle terrain pal import");
            using (ROM.BeginUndoScope(undoData))
            {
                Assert.True(vm.ImportPaletteBytes(MakeKnownPalette(), out _));
            }
            CoreState.Undo.Push(undoData);

            Assert.NotEqual(prePtr, rom.u32(entryAddr + 16)); // moved

            CoreState.Undo.RunUndo();
            Assert.Equal(prePtr, rom.u32(entryAddr + 16)); // restored
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with one BattleTerrain entry: image pointer
    /// at +12 (LZ77, unused by these palette tests) and a RAW palette pointer
    /// at +16 → palOffset. Returns (rom, entryAddr, palOffset).
    /// </summary>
    static (ROM rom, uint entryAddr, uint palOffset) MakeFe8uRomWithBattleTerrain()
    {
        var rom = new ROM();
        rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");

        uint entryAddr = 0x00800000u;
        uint imgOffset = 0x00810000u;
        uint palOffset = 0x00820000u;

        // +12 image pointer (must be a pointer so the entry is considered valid).
        WriteU32(rom.Data, (int)(entryAddr + 12), imgOffset | 0x08000000u);
        // +16 palette pointer (RAW 0x20 bytes live at palOffset).
        WriteU32(rom.Data, (int)(entryAddr + 16), palOffset | 0x08000000u);
        return (rom, entryAddr, palOffset);
    }

    /// <summary>A recognizable 16-color (0x20-byte) raw palette whose first
    /// byte is deliberately NOT the LZ77 magic 0x10.</summary>
    static byte[] MakeKnownPalette()
    {
        byte[] pal = new byte[0x20];
        for (int i = 0; i < pal.Length; i++) pal[i] = (byte)(0x21 + i);
        return pal;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
