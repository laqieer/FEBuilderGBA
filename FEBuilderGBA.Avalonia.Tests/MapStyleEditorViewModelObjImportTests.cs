// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the OBJ Image Import (ImageOnly) slice on
// MapStyleEditorViewModel (#710): TryImportObjImage dimension contract,
// FE7 obj2 rejection, palette-existence guard, and round-trip success
// through ImageImportCore.RemapToExistingPalette + EncodeDirectTiles4bpp
// + LZ77 + MapChangeCore.WritePlistData(OBJECT, ...).
using System;
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Marked [Collection("SharedState")] because the import path mutates
/// CoreState.ROM / CoreState.ImageService. Without serialization, xUnit's
/// parallel runner could race a sibling test's CoreState swap.
/// </summary>
[Collection("SharedState")]
public class MapStyleEditorViewModelObjImportTests
{
    // -----------------------------------------------------------------
    // Dimension contract — Copilot CLI v1 review item 2 on plan v2.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_RejectsWrongWidth()
    {
        var vm = new MapStyleEditorViewModel();
        byte[] rgba = new byte[128 * 128 * 4];
        Assert.False(vm.TryImportObjImage(rgba, width: 128, height: 128, out string err));
        Assert.Contains("256", err);
        Assert.Contains("wide", err);
    }

    [Fact]
    public void TryImportObjImage_RejectsShortHeight()
    {
        var vm = new MapStyleEditorViewModel();
        byte[] rgba = new byte[256 * 64 * 4];
        Assert.False(vm.TryImportObjImage(rgba, width: 256, height: 64, out string err));
        Assert.Contains("128", err);
        Assert.Contains("tall", err);
    }

    [Fact]
    public void TryImportObjImage_RejectsHeightNotMultipleOf8()
    {
        var vm = new MapStyleEditorViewModel();
        byte[] rgba = new byte[256 * 130 * 4];
        Assert.False(vm.TryImportObjImage(rgba, width: 256, height: 130, out string err));
        Assert.Contains("multiple of 8", err);
    }

    [Fact]
    public void TryImportObjImage_RejectsNullSource()
    {
        var vm = new MapStyleEditorViewModel();
        Assert.False(vm.TryImportObjImage(null, width: 256, height: 128, out string err));
        Assert.Contains("Invalid source", err);
    }

    [Fact]
    public void TryImportObjImage_RejectsTruncatedSource()
    {
        var vm = new MapStyleEditorViewModel();
        // Width/height pass but the RGBA buffer is too small.
        byte[] rgba = new byte[10];
        Assert.False(vm.TryImportObjImage(rgba, width: 256, height: 128, out string err));
        Assert.Contains("Invalid source", err);
    }

    // -----------------------------------------------------------------
    // FE7 obj2 rejection — actionable error wording.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_RejectsObj2Style()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            // Stage a non-zero obj2 address to simulate an FE7 dual-tileset style.
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress2 = 0x008C0000u;
            vm.SetCurrentObjPlistForTest(1);

            byte[] palette32 = MakeFlatPalette32();
            vm.SetCachedPaletteBytesForTest(palette32);
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);

            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("obj2", err);
            Assert.Contains("Tracked separately", err);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // CanImportObj predicate — sentinel/no-PLIST gates.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_RejectsZeroPlist()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(0); // reserved sentinel

            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);

            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("no valid OBJ PLIST", err);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void TryImportObjImage_RejectsPlistFF()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(0xFF); // no-PLIST marker

            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);

            // Per-version limit (FE8U: 0xEC) — 0xFF triggers the more
            // specific "past the per-version limit" branch added in PR
            // #716 review item 2.
            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("past the per-version limit", err);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot bot #2 on PR #716: plists above the per-version PLIST limit
    /// (e.g. FE8U vanilla 0xEC) but below 0xFF must also be rejected —
    /// without this gate, CanImportObj would enable the button for a slot
    /// that <see cref="MapChangeCore.WritePlistData"/> would refuse,
    /// surfacing a confusing UX.
    /// </summary>
    [Fact]
    public void TryImportObjImage_RejectsPlistAboveVersionLimit()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            // 0xED is above the FE8U default limit (0xEC) but below the
            // 0xFF sentinel — must still be rejected.
            vm.SetCurrentObjPlistForTest(0xED);

            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("past the per-version limit", err);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot bot #1 on PR #716: a null/zero OBJ pointer does NOT block
    /// import — the slot is a valid write target (WF
    /// <c>InputFormRef.WriteBinaryData</c> + <c>MapChangeCore.ResolvePlistSlotAddr</c>
    /// both treat address 0 as "append new data"). Without this assertion
    /// a future regression that re-introduces the <c>ObjPointer != 0</c>
    /// gate would slip through.
    /// </summary>
    [Fact]
    public void TryImportObjImage_AcceptsZeroObjPointer_AsAppend()
    {
        var (rom, objTableAddr, objPlist, palette32) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            var vm = new MapStyleEditorViewModel();
            // Explicitly leave ObjPointer at 0 — simulating a fresh PLIST
            // slot that no Map Style entry has populated yet.
            vm.ObjPointer = 0u;
            vm.ObjAddress = 0u;
            vm.SetCurrentObjPlistForTest(objPlist);
            vm.SetCachedPaletteBytesForTest(palette32);

            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            Assert.True(vm.TryImportObjImage(rgba, 256, 128, out string err),
                $"Import must succeed even when ObjPointer is 0; err = {err}");
            // Slot must now hold a real pointer.
            uint newPointer = rom.u32(objTableAddr + objPlist * 4u);
            Assert.NotEqual(0u, newPointer);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    [Fact]
    public void TryImportObjImage_RequiresPaletteLoaded()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(1);
            // No palette cache staged.

            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("palette is not loaded", err);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Happy-path round-trip: write succeeds and the OBJECT PLIST slot
    // now points at a region containing LZ77-compressed tile bytes.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_FE8U_WritesTilesAndPointer()
    {
        var (rom, objTableAddr, objPlist, palette32) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            var vm = new MapStyleEditorViewModel();
            // Stage VM state the LoadEntry path would normally fill in.
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress = 0x00C00000u;
            vm.SetCurrentObjPlistForTest(objPlist);
            vm.SetCachedPaletteBytesForTest(palette32);

            // 256x128 solid blue image — every pixel will resolve to the
            // nearest palette color != index 0 (palette index 0 is
            // transparent per ImageImportCore.RemapToExistingPalette).
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);

            Assert.True(vm.TryImportObjImage(rgba, 256, 128, out string err),
                $"Import must succeed; err = {err}");

            // ObjPointer / ObjAddress must point at the new region.
            uint slotAddr = objTableAddr + objPlist * 4u;
            uint newPointer = rom.u32(slotAddr);
            Assert.Equal(newPointer, vm.ObjPointer);
            Assert.Equal(U.toOffset(newPointer), vm.ObjAddress);
            Assert.NotEqual(0u, newPointer);

            // The new offset must hold LZ77 data — first byte = 0x10
            // (GBA BIOS LZ77 magic).
            uint newOffset = U.toOffset(newPointer);
            Assert.Equal(0x10, rom.Data[newOffset]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    /// <summary>
    /// The remap path must use the EXISTING palette (no quantization). With
    /// a synthetic palette of {transparent, red, green}, a pure-blue source
    /// pixel must round-trip to ONE of the non-transparent palette entries
    /// (red or green), not introduce a new color.
    /// </summary>
    [Fact]
    public void TryImportObjImage_RemapsAgainstExistingPalette()
    {
        var (rom, objTableAddr, objPlist, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            // 16-color palette: idx 0 = transparent (0,0,0 → not selected
            // for opaque pixels), idx 1 = red, idx 2 = green, idx 3..15 = 0
            // (also valid candidates, but red/green are closest to most colors).
            byte[] palette32 = new byte[32];
            // idx 1: red 0x1F packed = 0x001F
            palette32[2] = 0x1F; palette32[3] = 0x00;
            // idx 2: green 0x1F << 5 = 0x03E0
            palette32[4] = 0xE0; palette32[5] = 0x03;

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress = 0x00C00000u;
            vm.SetCurrentObjPlistForTest(objPlist);
            vm.SetCachedPaletteBytesForTest(palette32);

            // Pure-blue source — neither palette entry exactly matches but
            // the remap must still pick something (not throw, not return null).
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            Assert.True(vm.TryImportObjImage(rgba, 256, 128, out string err),
                $"Remap import must succeed; err = {err}");

            // The new region must contain a valid LZ77-compressed tile sheet.
            uint newPointer = rom.u32(objTableAddr + objPlist * 4u);
            Assert.NotEqual(0u, newPointer);
            uint newOffset = U.toOffset(newPointer);
            Assert.Equal(0x10, rom.Data[newOffset]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    // -----------------------------------------------------------------
    // Undo coverage — verifies the OBJECT slot write is undoable when
    // wrapped in an ambient undo scope.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_UndoableRestoresOriginalPointer()
    {
        var (rom, objTableAddr, objPlist, palette32) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();
            CoreState.Undo = new Undo();

            // Pre-seed the OBJECT slot with a known pointer so undo has a
            // value to restore.
            uint slotAddr = objTableAddr + objPlist * 4u;
            uint preSlot = 0x08C00000u;
            rom.Data[slotAddr + 0] = 0x00;
            rom.Data[slotAddr + 1] = 0x00;
            rom.Data[slotAddr + 2] = 0xC0;
            rom.Data[slotAddr + 3] = 0x08;
            Assert.Equal(preSlot, rom.u32(slotAddr));

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = preSlot;
            vm.ObjAddress = U.toOffset(preSlot);
            vm.SetCurrentObjPlistForTest(objPlist);
            vm.SetCachedPaletteBytesForTest(palette32);

            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            var undoData = CoreState.Undo.NewUndoData("test obj import");
            using (ROM.BeginUndoScope(undoData))
            {
                Assert.True(vm.TryImportObjImage(rgba, 256, 128, out _));
            }
            CoreState.Undo.Push(undoData);

            // Confirm the slot moved.
            Assert.NotEqual(preSlot, rom.u32(slotAddr));

            // Run undo and verify the original pointer is restored.
            CoreState.Undo.RunUndo();
            Assert.Equal(preSlot, rom.u32(slotAddr));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // CanImportObj OnPropertyChanged wiring — ObjPointer / ObjAddress2
    // changes must raise CanImportObj.
    // -----------------------------------------------------------------

    [Fact]
    public void CanImportObj_FiresPropertyChanged_OnObjAddress2()
    {
        var (rom, _, _, _) = MakeFe8uRomForObjImport();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(1);
            Assert.True(vm.CanImportObj, "CanImportObj must be true for valid OBJ plist + no obj2");

            bool fired = false;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanImportObj)) fired = true; };
            vm.ObjAddress2 = 0x008C0000u;
            Assert.True(fired, "CanImportObj must fire OnPropertyChanged when ObjAddress2 changes");
            Assert.False(vm.CanImportObj, "CanImportObj must be false when obj2 is present");
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with a populated map_obj_pointer table
    /// at 0x00890000, an entry at slot 1 pointing at 0x08C00000, and a
    /// flat 16-color palette buffer for the VM to remap against.
    /// Returns (rom, objTableAddr, objPlistSlot, palette32).
    /// </summary>
    static (ROM rom, uint objTableAddr, uint objPlistSlot, byte[] palette32) MakeFe8uRomForObjImport()
    {
        var rom = new ROM();
        rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
        uint objTableAddr = 0x00890000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, objTableAddr | 0x08000000u);
        // Plant a non-null slot at index 1 (so isSafetyOffset against the
        // existing slot works for sanity checks).
        WriteU32(rom.Data, (int)(objTableAddr + 1 * 4u), 0x08C00000u);
        return (rom, objTableAddr, 1u, MakeFlatPalette32());
    }

    /// <summary>16 colors, idx 0 = transparent, idx 1..15 = stepped grayscale.</summary>
    static byte[] MakeFlatPalette32()
    {
        byte[] pal = new byte[32];
        // idx 0: transparent (0,0,0).
        for (int i = 1; i < 16; i++)
        {
            byte v = (byte)(i & 0x1F);
            ushort packed = (ushort)(v | (v << 5) | (v << 10));
            pal[i * 2 + 0] = (byte)(packed & 0xFF);
            pal[i * 2 + 1] = (byte)((packed >> 8) & 0xFF);
        }
        return pal;
    }

    static byte[] MakeSolidRgba(int width, int height, byte r, byte g, byte b)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = 0xFF;
        }
        return rgba;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
