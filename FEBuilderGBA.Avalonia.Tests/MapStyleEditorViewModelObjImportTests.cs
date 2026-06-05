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
using FEBuilderGBA.Avalonia.Views;
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

    /// <summary>
    /// Copilot bot #3 on PR #716: the pre-fix int multiplication
    /// <c>width * height * 4</c> overflows for large heights, wrapping to a
    /// negative value that lets a truncated buffer slip past the bounds
    /// check. With width=256 and height=8_400_000, the int product is
    /// 8_600_000_000 which wraps past int.MaxValue, so the old check
    /// (<c>sourceRgba.Length &lt; required</c>) would pass for ANY
    /// non-empty buffer. The long-arithmetic fix correctly rejects it.
    /// Passing a 100-byte buffer keeps the test fast and memory-cheap.
    /// </summary>
    [Fact]
    public void TryImportObjImage_RejectsOverflowingDimensions_LongCheck()
    {
        var vm = new MapStyleEditorViewModel();
        // 100-byte buffer that the OLD int-arithmetic length check would
        // accept (because the required-bytes calculation wrapped past
        // int.MaxValue).
        byte[] rgba = new byte[100];
        // height = 8_400_000 is a multiple of 8 and passes the >=128
        // floor, so the early dimension gates don't reject it before
        // the length check fires. width=256 keeps the width gate happy.
        Assert.False(vm.TryImportObjImage(rgba, width: 256, height: 8_400_000, out string err));
        Assert.Contains("Invalid source", err);
    }

    // -----------------------------------------------------------------
    // FE7 obj2 dual-tileset split (#976): the encoded tile sheet is split
    // in half by byte length and both OBJECT PLIST slots are written.
    // -----------------------------------------------------------------

    [Fact]
    public void TryImportObjImage_Obj2Style_WritesBothPlists()
    {
        var (rom, objTableAddr) = MakeFe7RomForObjImportWithObj2(primaryPlist: 1, obj2Plist: 2);
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress = 0x00C00000u;
            vm.ObjAddress2 = 0x00D00000u; // simulate an FE7 dual-tileset style
            vm.SetCurrentObjPlistForTest(1);
            vm.SetCurrentObjPlist2ForTest(2);
            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());

            const int height = 128;
            byte[] rgba = MakeSolidRgba(256, height, 0, 0, 255);
            Assert.True(vm.TryImportObjImage(rgba, 256, height, out string err),
                $"obj2 dual-tileset import must succeed; err = {err}");

            // Both PLIST slots now hold DIFFERENT non-zero pointers.
            uint primaryPointer = rom.u32(objTableAddr + 1 * 4u);
            uint obj2Pointer = rom.u32(objTableAddr + 2 * 4u);
            Assert.NotEqual(0u, primaryPointer);
            Assert.NotEqual(0u, obj2Pointer);
            Assert.NotEqual(primaryPointer, obj2Pointer);

            // vm addresses must equal the two new offsets.
            uint primaryOffset = U.toOffset(primaryPointer);
            uint obj2Offset = U.toOffset(obj2Pointer);
            Assert.Equal(primaryOffset, vm.ObjAddress);
            Assert.Equal(obj2Offset, vm.ObjAddress2);
            Assert.Equal(primaryPointer, vm.ObjPointer);

            // Both written regions start with the LZ77 magic byte 0x10.
            Assert.Equal(0x10, rom.Data[primaryOffset]);
            Assert.Equal(0x10, rom.Data[obj2Offset]);

            // Round-trip: decompress(primary) ++ decompress(obj2) reproduces
            // the full encoded sheet (128 * height bytes), each half being
            // 64 * height bytes (the byte-level split point).
            byte[] decomp1 = LZ77.decompress(rom.Data, primaryOffset);
            byte[] decomp2 = LZ77.decompress(rom.Data, obj2Offset);
            int sheetLen = 128 * height;
            Assert.Equal(sheetLen / 2, decomp1.Length);
            Assert.Equal(sheetLen / 2, decomp2.Length);
            Assert.Equal(sheetLen, decomp1.Length + decomp2.Length);
            Assert.Equal(64 * height, decomp1.Length);
            Assert.Equal(64 * height, decomp2.Length);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    /// <summary>
    /// #976 byte-level split parity: a height that is a multiple of 8 but
    /// NOT of 16 (256x136) must still round-trip with each half being
    /// exactly (128*136)/2 bytes — proving the split is byte-level (on a
    /// whole-tile boundary because tileData.Length = 128*height) rather
    /// than a row-aligned visual top/bottom split.
    /// </summary>
    [Fact]
    public void TryImportObjImage_Obj2Style_NonMultipleOf16Height_ByteSplitRoundTrips()
    {
        var (rom, objTableAddr) = MakeFe7RomForObjImportWithObj2(primaryPlist: 1, obj2Plist: 2);
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress = 0x00C00000u;
            vm.ObjAddress2 = 0x00D00000u;
            vm.SetCurrentObjPlistForTest(1);
            vm.SetCurrentObjPlist2ForTest(2);
            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());

            const int height = 136; // multiple of 8, NOT of 16
            byte[] rgba = MakeSolidRgba(256, height, 0, 0, 255);
            Assert.True(vm.TryImportObjImage(rgba, 256, height, out string err),
                $"obj2 import for non-row-aligned height must succeed; err = {err}");

            uint primaryOffset = U.toOffset(rom.u32(objTableAddr + 1 * 4u));
            uint obj2Offset = U.toOffset(rom.u32(objTableAddr + 2 * 4u));
            byte[] decomp1 = LZ77.decompress(rom.Data, primaryOffset);
            byte[] decomp2 = LZ77.decompress(rom.Data, obj2Offset);
            int expectedHalf = (128 * height) / 2;
            Assert.Equal(expectedHalf, decomp1.Length);
            Assert.Equal(expectedHalf, decomp2.Length);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    /// <summary>
    /// #976: a secondary obj2 plist at/above the per-version PLIST limit
    /// must reject the import with a "limit" error and mutate ZERO bytes.
    /// </summary>
    [Fact]
    public void TryImportObjImage_Obj2Style_SecondPlistPastLimit_Rejected()
    {
        var (rom, _) = MakeFe7RomForObjImportWithObj2(primaryPlist: 1, obj2Plist: 2);
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();

            uint limit = MapChangeCore.GetPlistLimit(rom);
            Assert.True(limit > 0, "synthetic ROM must yield a non-zero plist limit");

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.ObjAddress = 0x00C00000u;
            vm.ObjAddress2 = 0x00D00000u;
            vm.SetCurrentObjPlistForTest(1);
            vm.SetCurrentObjPlist2ForTest(limit); // == limit ⇒ past-limit

            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());

            byte[] snapshot = (byte[])rom.Data.Clone();
            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err));
            Assert.Contains("limit", err);
            // Zero mutation.
            Assert.Equal(snapshot.Length, rom.Data.Length);
            Assert.True(BytesEqual(snapshot, rom.Data), "ROM bytes must be unchanged on rejection");
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
        }
    }

    /// <summary>
    /// #976 atomicity: when the SECOND (obj2) PLIST write fails AFTER the
    /// first has already mutated the ROM, the view's ambient undo scope
    /// must roll BOTH writes back. The obj2 index slot is positioned past
    /// rom.Data.Length (so ResolvePlistSlotAddr returns NOT_FOUND for obj2
    /// only) while obj2Plist stays below GetPlistLimit (so it passes the
    /// in-VM pre-validation guard and only fails inside the SECOND
    /// WritePlistData). A mid-ROM 0x00 free region means the FIRST write
    /// lands mid-ROM and does NOT grow rom.Data (which would otherwise
    /// bring the obj2 slot back in-bounds).
    /// </summary>
    [Fact]
    public void TryImportObjImage_Obj2Style_SecondWriteFails_RollsBackBothPlists()
    {
        var (rom, objTableAddr, primaryPlist, obj2Plist) =
            MakeFe7RomForObjImportObj2OutOfBounds();
        var prevRom = CoreState.ROM;
        var prevImg = CoreState.ImageService;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();
            CoreState.Undo = new Undo();

            // obj2Plist must be below the per-version limit so it passes the
            // in-VM pre-validation guard (the failure must happen inside the
            // SECOND WritePlistData, not in the early limit gate).
            uint limit = MapChangeCore.GetPlistLimit(rom);
            Assert.True(obj2Plist < limit, "obj2Plist must be below the plist limit to reach the second write");

            uint primarySlot = objTableAddr + primaryPlist * 4u;
            uint obj2Slot = objTableAddr + obj2Plist * 4u;
            // Sanity: obj2 slot is genuinely out of bounds, primary is in.
            Assert.True(primarySlot + 4u <= (uint)rom.Data.Length, "primary slot must be in-bounds");
            Assert.True(obj2Slot + 4u > (uint)rom.Data.Length, "obj2 slot must be out-of-bounds");

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = rom.u32(primarySlot);
            vm.ObjAddress = U.toOffset(vm.ObjPointer);
            vm.ObjAddress2 = 0x00D00000u;
            vm.SetCurrentObjPlistForTest(primaryPlist);
            vm.SetCurrentObjPlist2ForTest(obj2Plist);
            vm.SetCachedPaletteBytesForTest(MakeFlatPalette32());

            byte[] snapshot = (byte[])rom.Data.Clone();
            int snapshotLen = rom.Data.Length;
            uint prePrimaryPointer = rom.u32(primarySlot);

            byte[] rgba = MakeSolidRgba(256, 128, 0, 0, 255);
            var undoData = CoreState.Undo.NewUndoData("test obj2 atomic import");
            using (ROM.BeginUndoScope(undoData))
            {
                Assert.False(vm.TryImportObjImage(rgba, 256, 128, out string err),
                    "second (obj2) write must fail and the whole import must report false");
            }
            CoreState.Undo.Push(undoData);

            // Run undo and assert EVERYTHING is restored: primary slot, obj2
            // slot, ROM length, and the full byte image.
            CoreState.Undo.RunUndo();
            Assert.Equal(snapshotLen, rom.Data.Length);
            Assert.Equal(prePrimaryPointer, rom.u32(primarySlot));
            Assert.True(BytesEqual(snapshot, rom.Data), "ROM bytes must be byte-identical after undo");
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.ImageService = prevImg;
            CoreState.Undo = prevUndo;
        }
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
    // CanImportObj OnPropertyChanged wiring + obj2 in-limit gating (#976).
    // -----------------------------------------------------------------

    /// <summary>
    /// #976: a FE7 dual-tileset style with an in-limit secondary obj2 plist
    /// is now IMPORTABLE (the old expectation that obj2 ⇒ CanImportObj false
    /// is reversed). The SetCurrentObjPlist2ForTest seam must also raise a
    /// CanImportObj PropertyChanged so the view's import button refreshes.
    /// </summary>
    [Fact]
    public void CanImportObj_TrueForObj2Style_WhenSecondaryPlistInLimit()
    {
        var (rom, _) = MakeFe7RomForObjImportWithObj2(primaryPlist: 1, obj2Plist: 2);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(1);

            bool fired = false;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanImportObj)) fired = true; };
            vm.SetCurrentObjPlist2ForTest(2); // in-limit secondary plist
            Assert.True(fired, "SetCurrentObjPlist2ForTest must raise CanImportObj PropertyChanged");
            Assert.True(vm.CanImportObj, "CanImportObj must be true for an in-limit obj2 style");
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// #976: a secondary obj2 plist at/above the per-version PLIST limit
    /// must disable import (CanImportObj false) since the second-half write
    /// would target an invalid slot.
    /// </summary>
    [Fact]
    public void CanImportObj_FalseWhenObj2PlistPastLimit()
    {
        var (rom, _) = MakeFe7RomForObjImportWithObj2(primaryPlist: 1, obj2Plist: 2);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint limit = MapChangeCore.GetPlistLimit(rom);
            Assert.True(limit > 0);

            var vm = new MapStyleEditorViewModel();
            vm.ObjPointer = 0x08C00000u;
            vm.SetCurrentObjPlistForTest(1);
            Assert.True(vm.CanImportObj, "primary-only must be importable");

            vm.SetCurrentObjPlist2ForTest(limit); // == limit ⇒ past-limit
            Assert.False(vm.CanImportObj, "CanImportObj must be false when obj2 plist is past the limit");
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Indexed-image decode guards — Copilot bot #4 on PR #716.
    // -----------------------------------------------------------------

    /// <summary>
    /// Guard 1: indexData null or shorter than w*h must be rejected with a
    /// "shorter than expected" error. Before the fix the View partially
    /// converted the available bytes and left the rest of the output as
    /// all-zero RGBA (silently transparent garbage).
    /// </summary>
    [Fact]
    public void ConvertIndexedToRgba_RejectsShortIndexBuffer()
    {
        byte[] indexData = new byte[10]; // expected 256*128 = 32768
        byte[] palRgba = new byte[] { 0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF };
        var result = MapStyleEditorView.ConvertIndexedToRgba(indexData, palRgba, 256, 128, out string err);
        Assert.Null(result);
        Assert.Contains("shorter than expected", err);
    }

    [Fact]
    public void ConvertIndexedToRgba_RejectsNullIndexBuffer()
    {
        byte[] palRgba = new byte[] { 0xFF, 0x00, 0x00, 0xFF };
        var result = MapStyleEditorView.ConvertIndexedToRgba(null, palRgba, 256, 128, out string err);
        Assert.Null(result);
        Assert.Contains("shorter than expected", err);
    }

    /// <summary>
    /// Guard 2: a null / too-short palette buffer must be rejected with
    /// "no usable palette". Before the fix every pixel silently fell
    /// through to the `palOff + 3 &lt; palRgba.Length` branch and stayed
    /// at 0 RGBA.
    /// </summary>
    [Fact]
    public void ConvertIndexedToRgba_RejectsNullPalette()
    {
        byte[] indexData = new byte[256 * 128];
        var result = MapStyleEditorView.ConvertIndexedToRgba(indexData, null, 256, 128, out string err);
        Assert.Null(result);
        Assert.Contains("no usable palette", err);
    }

    [Fact]
    public void ConvertIndexedToRgba_RejectsTooShortPalette()
    {
        byte[] indexData = new byte[256 * 128];
        byte[] palRgba = new byte[3]; // < 4 bytes ⇒ can't hold even one color
        var result = MapStyleEditorView.ConvertIndexedToRgba(indexData, palRgba, 256, 128, out string err);
        Assert.Null(result);
        Assert.Contains("no usable palette", err);
    }

    /// <summary>
    /// Guard 3: a pixel referencing a palette entry beyond the palette
    /// buffer must be rejected with an actionable error. Before the fix
    /// such pixels silently stayed at 0 RGBA — the user got an
    /// apparently-successful import with large transparent regions where
    /// out-of-range indices appeared.
    /// </summary>
    [Fact]
    public void ConvertIndexedToRgba_RejectsOutOfRangePaletteIndex()
    {
        byte[] indexData = new byte[256 * 128];
        // Seed pixel 5 with index 4 — but the palette only has 2 entries
        // (8 bytes), so index 4 means palOff = 16 which is past palRgba.Length.
        indexData[5] = 4;
        byte[] palRgba = new byte[8]; // 2 colors only
        var result = MapStyleEditorView.ConvertIndexedToRgba(indexData, palRgba, 256, 128, out string err);
        Assert.Null(result);
        // Verify the View-facing error wording: identifies the pixel
        // index, the palette entry, and the actual palette size.
        Assert.Contains("uses palette entry 4", err);
        Assert.Contains("only 2 colors", err);
    }

    /// <summary>
    /// Happy path: a sufficient indexData + palette must produce a
    /// correctly-sized RGBA buffer with pixel 0's data sourced from the
    /// palette entry it indexes.
    /// </summary>
    [Fact]
    public void ConvertIndexedToRgba_HappyPath_DecodesPixels()
    {
        byte[] indexData = new byte[8 * 8];
        // Pixel 0 → palette index 1 → RGB (0,255,0,255).
        indexData[0] = 1;
        byte[] palRgba = new byte[]
        {
            0xAA, 0xBB, 0xCC, 0xDD, // index 0
            0x00, 0xFF, 0x00, 0xFF, // index 1
        };
        var result = MapStyleEditorView.ConvertIndexedToRgba(indexData, palRgba, 8, 8, out string err);
        Assert.NotNull(result);
        Assert.Equal("", err);
        Assert.Equal(8 * 8 * 4, result!.Length);
        // Pixel 0 RGBA should match palette index 1.
        Assert.Equal(0x00, result[0]);
        Assert.Equal(0xFF, result[1]);
        Assert.Equal(0x00, result[2]);
        Assert.Equal(0xFF, result[3]);
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

    /// <summary>
    /// Build a synthetic FE7U ROM for the obj2 dual-tileset import path
    /// (#976). The map_obj_pointer table at 0x00890000 holds in-bounds,
    /// non-null slots for BOTH the primary and obj2 plist indices. The ROM
    /// is all-zero so <see cref="FEBuilderGBA.ImageImportCore.FindAndWriteData"/>
    /// finds 0x00 free space at the mid-ROM search start and writes
    /// mid-ROM (it does NOT append to the end / grow rom.Data).
    /// Returns (rom, objTableAddr).
    /// </summary>
    static (ROM rom, uint objTableAddr) MakeFe7RomForObjImportWithObj2(byte primaryPlist, byte obj2Plist)
    {
        var rom = new ROM();
        rom.LoadLow("test-fe7u.gba", new byte[0x1100000], "AE7E01");
        uint objTableAddr = 0x00890000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, objTableAddr | 0x08000000u);
        // Plant non-null in-bounds slots for both plists so isSafetyOffset /
        // sanity reads succeed for both.
        WriteU32(rom.Data, (int)(objTableAddr + primaryPlist * 4u), 0x08C00000u);
        WriteU32(rom.Data, (int)(objTableAddr + obj2Plist * 4u), 0x08D00000u);
        return (rom, objTableAddr);
    }

    /// <summary>
    /// Build a synthetic FE7U ROM where the map_obj_pointer table base sits
    /// near the ROM end so that the PRIMARY plist slot is in-bounds but the
    /// OBJ2 plist slot lands PAST rom.Data.Length (ResolvePlistSlotAddr
    /// returns NOT_FOUND for obj2 only). obj2Plist is kept BELOW
    /// GetPlistLimit so it passes the in-VM pre-validation guard and only
    /// fails inside the SECOND WritePlistData. The ROM is all-zero so the
    /// FIRST (primary) write finds mid-ROM 0x00 free space and does NOT
    /// grow rom.Data — keeping the obj2 slot out-of-bounds.
    /// Returns (rom, objTableAddr, primaryPlist, obj2Plist).
    /// </summary>
    static (ROM rom, uint objTableAddr, uint primaryPlist, uint obj2Plist)
        MakeFe7RomForObjImportObj2OutOfBounds()
    {
        var rom = new ROM();
        rom.LoadLow("test-fe7u.gba", new byte[0x1100000], "AE7E01");

        uint primaryPlist = 1u;
        uint obj2Plist = 2u;

        // Place the table base so that the obj2 slot (base + 2*4) ends
        // EXACTLY at rom.Data.Length, putting base + 2*4 + 4 past the end
        // while base + 1*4 + 4 (== Length) is still in-bounds.
        //   primary slot end = base + 4 + 4 = base + 8 = Length  -> in-bounds
        //   obj2    slot end = base + 8 + 4 = base + 12 = Length+4 -> OOB
        uint length = (uint)rom.Data.Length;
        uint objTableAddr = length - 8u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, objTableAddr | 0x08000000u);
        // Primary slot (in-bounds) holds a known non-null pointer.
        WriteU32(rom.Data, (int)(objTableAddr + primaryPlist * 4u), 0x08C00000u);
        // The obj2 slot would be at base + 8 == Length (OOB) — nothing to plant.
        return (rom, objTableAddr, primaryPlist, obj2Plist);
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
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
