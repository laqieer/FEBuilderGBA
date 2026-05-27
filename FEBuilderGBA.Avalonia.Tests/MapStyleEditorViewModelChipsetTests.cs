// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Chipset Tab plumbing on MapStyleEditorViewModel (#671):
// TryLoadChipsetTSA decoding, semantic bounds, WriteChipsetConfig
// round-trip through LZ77 + WritePlistData, and the WF-parity Paste()
// path that applies all 4 W values + terrain in one shot.
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MapStyleEditorViewModelChipsetTests
{
    /// <summary>
    /// Synthetic CONFIG buffer: every chipset slot's TSA is encoded as
    /// EncodeTsaWord(chipset & 0x1F, chipset >> 5 % 32, chipset % 16, chipset % 4)
    /// for chipset 0..255 (limited so the buffer doesn't blow past
    /// CHIPSET_SEP_BYTE) — gives every read a distinct, predictable value.
    /// Terrain bytes are seeded as (chipset + 1) % 256 so the test can
    /// detect even/odd index slicing bugs.
    /// </summary>
    static byte[] BuildSyntheticConfig()
    {
        const int bufSize = 0x2400;
        byte[] buf = new byte[bufSize];
        for (int c = 0; c < 256; c++)
        {
            ushort w = MapStyleEditorViewModel.EncodeTsaWord(c & 0x1F, (c >> 5) & 0x1F, c % 16, c % 4);
            int tsaBase = c * 8;
            for (int sub = 0; sub < 4; sub++)
            {
                ushort word = (ushort)(w + sub); // distinct per sub
                buf[tsaBase + sub * 2 + 0] = (byte)(word & 0xFF);
                buf[tsaBase + sub * 2 + 1] = (byte)((word >> 8) & 0xFF);
            }
            buf[MapEditorTilesetCore.CHIPSET_SEP_BYTE + c] = (byte)((c + 1) & 0xFF);
        }
        return buf;
    }

    /// <summary>
    /// Plant a ROM with a CONFIG buffer at a known offset, point
    /// CONFIG PLIST slot 7 at it, register the matching map_obj_pointer
    /// entry, and write a map_setting whose obj_plist matches so LoadEntry
    /// can resolve the CONFIG plist via the map_setting +7 byte. Returns
    /// (rom, addr, configPlist).
    /// </summary>
    static (ROM rom, uint entryAddr, byte configPlist) MakeFe8uRomWithConfig(byte[] configUz)
    {
        var rom = new ROM();
        rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");

        byte[] compressed = LZ77.compress(configUz);

        // 1. Plant the compressed CONFIG buffer at 0x00C00000.
        uint configDataAddr = 0x00C00000u;
        for (int i = 0; i < compressed.Length; i++) rom.Data[configDataAddr + i] = compressed[i];

        // 2. CONFIG PLIST table at 0x00880000, plist 7 -> configDataAddr.
        uint cfgTableAddr = 0x00880000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);
        WriteU32(rom.Data, (int)(cfgTableAddr + 7 * 4u), configDataAddr | 0x08000000u);

        // 3. map_obj_pointer table at 0x00890000, slot 1 = a dummy pointer
        //    that's safe (points at planted bytes).
        uint objTableAddr = 0x00890000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, objTableAddr | 0x08000000u);
        WriteU32(rom.Data, (int)(objTableAddr + 1 * 4u), 0x08C00000u);

        // 4. Plant a map_setting at 0x008A0000 whose obj_plist == 1 and
        //    config_plist == 7. Layout: pointer-valued first dword for
        //    isMapSettingValid, u16 obj_plist at +4 (low byte = 1, high
        //    byte = 0), palette_plist at +6 = 0, config_plist at +7 = 7.
        uint mapTableAddr = 0x008A0000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableAddr | 0x08000000u);
        WriteU32(rom.Data, (int)mapTableAddr, 0x08123456u);
        rom.Data[mapTableAddr + 4] = 0x01; // obj_plist low byte
        rom.Data[mapTableAddr + 5] = 0x00; // obj_plist high byte
        rom.Data[mapTableAddr + 6] = 0x00; // palette_plist
        rom.Data[mapTableAddr + 7] = 0x07; // config_plist
        rom.Data[mapTableAddr + 11] = 0x00; // mapchange plist = 0 (none)
        // Terminator at next slot.
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)(mapTableAddr + dataSize), 0x00000000u);

        // map_obj_pointer entry address for the LoadEntry call.
        uint entryAddr = objTableAddr + 1 * 4u;
        return (rom, entryAddr, 7);
    }

    static void WriteU32(byte[] data, uint offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU32(byte[] data, int offset, uint value) =>
        WriteU32(data, (uint)offset, value);

    [Fact]
    public void TryLoadChipsetTSA_DecodesFromConfigBuffer()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.CanEditChipsetConfig);

            // Chipset 5 -> 4 distinct sub-word values per the planting rule.
            Assert.True(vm.TryLoadChipsetTSA(5));
            ushort expectedW0 = MapStyleEditorViewModel.EncodeTsaWord(5 & 0x1F, (5 >> 5) & 0x1F, 5 % 16, 5 % 4);
            Assert.Equal(expectedW0, vm.GetSlotW(0));
            Assert.Equal((ushort)(expectedW0 + 1), vm.GetSlotW(2));
            Assert.Equal((ushort)(expectedW0 + 2), vm.GetSlotW(4));
            Assert.Equal((ushort)(expectedW0 + 3), vm.GetSlotW(6));
            // Terrain byte for chipset 5 = (5 + 1) % 256 = 6.
            Assert.Equal(6, vm.CurrentTerrain);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void TryLoadChipsetTSA_RejectsIndex1024_PreservesCache()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            // Successful load for chipset 0 must succeed first.
            Assert.True(vm.TryLoadChipsetTSA(0));
            // 1024 is out of semantic range — must fail AND clear slot state.
            Assert.False(vm.TryLoadChipsetTSA(1024));
            // Cache survives so the user can try a different chipset.
            Assert.True(vm.CanEditChipsetConfig);
            // Re-loading a valid index works.
            Assert.True(vm.TryLoadChipsetTSA(2));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void WriteChipsetConfig_RoundTripsThroughLZ77()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, configPlist) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.TryLoadChipsetTSA(7));

            // Mutate slot 0's W and the terrain.
            vm.SetSlotWByLogicalIndex(0, 0x1234);
            vm.CurrentTerrain = 0x55;

            var undoData = CoreState.Undo.NewUndoData("test chipset write");
            bool ok;
            string err;
            using (ROM.BeginUndoScope(undoData))
                ok = vm.WriteChipsetConfig(out err);
            Assert.True(ok, err);
            CoreState.Undo.Push(undoData);

            // Re-decompress from the new ChipsetConfigAddress and verify
            // the edit landed.
            byte[] newConfigUz = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            Assert.NotNull(newConfigUz);
            int tsaBase = 7 * 8;
            ushort actualW0 = (ushort)(newConfigUz[tsaBase] | (newConfigUz[tsaBase + 1] << 8));
            Assert.Equal((ushort)0x1234, actualW0);
            Assert.Equal((byte)0x55, newConfigUz[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 7]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void WriteChipsetConfig_RawWEdit_UpdatesConfig()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.TryLoadChipsetTSA(0));

            // Raw W edit to sub 3 (last slot — byte offset 6 in the TSA record).
            vm.SetSlotWByLogicalIndex(3, 0xABCD);

            var undoData = CoreState.Undo.NewUndoData("raw W write");
            bool ok;
            using (ROM.BeginUndoScope(undoData))
                ok = vm.WriteChipsetConfig(out _);
            Assert.True(ok);
            CoreState.Undo.Push(undoData);

            byte[] newConfigUz = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            // sub 3 → byte offset 6/7 in the 8-byte chipset-0 TSA block.
            Assert.Equal((byte)0xCD, newConfigUz[6]);
            Assert.Equal((byte)0xAB, newConfigUz[7]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Per v5 plan #3: writes for chipset 1 must land at byte
    /// CHIPSET_SEP_BYTE+1 (NOT +0 from a wrong even/odd split), and
    /// chipset 2 at +2. Confirms the byte-offset reduction matches WF.
    /// </summary>
    [Theory]
    [InlineData(1, (byte)0x77)]
    [InlineData(2, (byte)0x88)]
    public void WriteChipsetConfig_TerrainOddEven_WritesCorrectByte(int chipsetNo, byte terrain)
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.TryLoadChipsetTSA(chipsetNo));
            vm.CurrentTerrain = terrain;

            var undoData = CoreState.Undo.NewUndoData("terrain odd/even");
            bool ok;
            using (ROM.BeginUndoScope(undoData))
                ok = vm.WriteChipsetConfig(out _);
            Assert.True(ok);
            CoreState.Undo.Push(undoData);

            byte[] newConfigUz = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            Assert.Equal(terrain, newConfigUz[MapEditorTilesetCore.CHIPSET_SEP_BYTE + chipsetNo]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Per v5 plan #3: assert that the last slot's bytes land at offsets
    /// chipsetNo*8 + 6 and +7 (i.e. sub-index 3, not a confused suffix=6
    /// path that would write outside the 8-byte record).
    /// </summary>
    [Fact]
    public void WriteChipsetConfig_LastSlotWritesBytes6And7()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.TryLoadChipsetTSA(10));

            vm.SetSlotWByLogicalIndex(3, 0xBEEF);
            var undoData = CoreState.Undo.NewUndoData("last slot");
            using (ROM.BeginUndoScope(undoData))
                Assert.True(vm.WriteChipsetConfig(out _));
            CoreState.Undo.Push(undoData);

            byte[] newConfigUz = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            int tsaBase = 10 * 8;
            Assert.Equal((byte)0xEF, newConfigUz[tsaBase + 6]);
            Assert.Equal((byte)0xBE, newConfigUz[tsaBase + 7]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void WriteChipsetConfig_FailsOn_PlistZero_OrPlistFF()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.TryLoadChipsetTSA(0));

            // Force the VM's CONFIG plist to 0 — write must refuse.
            vm.CurrentConfigPlist = 0;
            Assert.False(vm.WriteChipsetConfig(out string err0));
            Assert.NotEqual("", err0);

            vm.CurrentConfigPlist = 0xFF;
            Assert.False(vm.WriteChipsetConfig(out string errFF));
            Assert.NotEqual("", errFF);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot v3 inline review on PR #691: a Copy Tile (captures all 4 W +
    /// terrain) followed by Copy Type (terrain-only intent) followed by
    /// Paste must apply ONLY the terrain — not the stale W values from
    /// the earlier Copy Tile. <see cref="MapStyleEditorViewModel.CopyTerrain"/>
    /// clears <c>_copiedChipset</c> so Paste downgrades to terrain-only.
    /// </summary>
    [Fact]
    public void CopyTerrain_AfterCopyChipset_PasteAppliesOnlyTerrain()
    {
        var vm = new MapStyleEditorViewModel();
        // Seed and Copy Tile (captures W + terrain).
        vm.SetSlotWByLogicalIndex(0, 0xAAAA);
        vm.SetSlotWByLogicalIndex(1, 0xBBBB);
        vm.SetSlotWByLogicalIndex(2, 0xCCCC);
        vm.SetSlotWByLogicalIndex(3, 0xDDDD);
        vm.CurrentTerrain = 0x42;
        vm.CopyChipset();

        // Now Copy Type with a new terrain — must clear the tile clipboard.
        vm.CurrentTerrain = 0x99;
        vm.CopyTerrain();

        // Change everything in the VM so we can see what Paste applies.
        vm.SetSlotWByLogicalIndex(0, 0x1111);
        vm.SetSlotWByLogicalIndex(1, 0x2222);
        vm.SetSlotWByLogicalIndex(2, 0x3333);
        vm.SetSlotWByLogicalIndex(3, 0x4444);
        vm.CurrentTerrain = 0x00;

        Assert.True(vm.Paste());
        // W values must be untouched (the earlier CopyChipset clipboard
        // was cleared by CopyTerrain).
        Assert.Equal((ushort)0x1111, vm.GetSlotW(0));
        Assert.Equal((ushort)0x2222, vm.GetSlotW(2));
        Assert.Equal((ushort)0x3333, vm.GetSlotW(4));
        Assert.Equal((ushort)0x4444, vm.GetSlotW(6));
        // Terrain must be the one captured by Copy Type.
        Assert.Equal(0x99, vm.CurrentTerrain);
    }

    [Fact]
    public void Paste_ReturnsFalseWhenClipboardEmpty()
    {
        var vm = new MapStyleEditorViewModel();
        Assert.False(vm.Paste());
    }

    /// <summary>
    /// PR #691 Copilot CLI review item 1: CopyChipset (Copy Tile) must
    /// also capture the terrain so a subsequent Paste restores BOTH the
    /// 4 W values AND the terrain — matching WF
    /// <c>CopyTileButton_Click</c> which serializes terrain alongside
    /// the W fields. A user who Alt+T then Alt+V should not silently
    /// keep the destination's old terrain.
    /// </summary>
    [Fact]
    public void CopyChipset_AlsoCapturesTerrain_PasteRestoresBoth()
    {
        var vm = new MapStyleEditorViewModel();
        vm.SetSlotWByLogicalIndex(0, 0x1111);
        vm.SetSlotWByLogicalIndex(1, 0x2222);
        vm.SetSlotWByLogicalIndex(2, 0x3333);
        vm.SetSlotWByLogicalIndex(3, 0x4444);
        vm.CurrentTerrain = 0x55;

        // Copy Tile (Alt+T) ONLY — no separate Copy Type call.
        vm.CopyChipset();

        // Mutate everything so we can prove paste rewrites it.
        vm.SetSlotWByLogicalIndex(0, 0xAAAA);
        vm.CurrentTerrain = 0xFF;

        Assert.True(vm.Paste());
        Assert.Equal((ushort)0x1111, vm.GetSlotW(0));
        Assert.Equal((ushort)0x2222, vm.GetSlotW(2));
        Assert.Equal((ushort)0x3333, vm.GetSlotW(4));
        Assert.Equal((ushort)0x4444, vm.GetSlotW(6));
        // Terrain must come back too — this is the WF-parity assertion.
        Assert.Equal(0x55, vm.CurrentTerrain);
    }

    // -----------------------------------------------------------------
    // #704 — TryWriteConfigBuffer (MapChip Import path).
    // -----------------------------------------------------------------

    /// <summary>
    /// WF parity: MapStyleEditorForm rejects .MAPCHIP_CONFIG inputs
    /// shorter than 9216 bytes (0x2000 TSA + 0x400 terrain). The Avalonia
    /// VM enforces the same minimum.
    /// </summary>
    [Fact]
    public void TryWriteConfigBuffer_RejectsTooSmall()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            byte[] tooSmall = new byte[8000];
            Assert.False(vm.TryWriteConfigBuffer(tooSmall, out string err));
            Assert.Contains("8000", err);
            Assert.Contains("9216", err);

            // Null input is also rejected with the size error path.
            Assert.False(vm.TryWriteConfigBuffer(null!, out string nullErr));
            Assert.Contains("9216", nullErr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Round-trip: write a 9216-byte buffer with a distinct fingerprint,
    /// then re-decompress the new PLIST entry and verify every byte
    /// matches the input. This proves the LZ77 compress/decompress
    /// roundtrip preserves the raw bytes — the prerequisite for
    /// preserving palette bits.
    /// </summary>
    [Fact]
    public void TryWriteConfigBuffer_RoundTripsThroughLZ77()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.CanEditChipsetConfig);

            // Build a 9216-byte input with a recognizable per-byte fingerprint.
            byte[] input = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + MapEditorTilesetCore.CHIPSET_COUNT];
            for (int i = 0; i < input.Length; i++)
                input[i] = (byte)((i * 31 + 7) & 0xFF);

            var undoData = CoreState.Undo.NewUndoData("test config import");
            bool ok;
            string err;
            using (ROM.BeginUndoScope(undoData))
                ok = vm.TryWriteConfigBuffer(input, out err);
            Assert.True(ok, err);
            CoreState.Undo.Push(undoData);

            // Re-decompress from the new ChipsetConfigAddress and verify
            // the bytes survive the LZ77 round-trip.
            byte[] roundTripped = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            Assert.NotNull(roundTripped);
            Assert.True(roundTripped.Length >= input.Length,
                $"Round-tripped buffer too short: {roundTripped.Length} < {input.Length}");
            for (int i = 0; i < input.Length; i++)
                Assert.Equal(input[i], roundTripped[i]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// #704 core acceptance: palette-index bits (bits 12-15 of a TSA u16)
    /// must survive the import round-trip. Plant a buffer where every
    /// chipset's slot-0 TSA word encodes palette index 15 (0xF000) and
    /// verify those bits land in the decompressed output untouched.
    /// </summary>
    [Fact]
    public void TryWriteConfigBuffer_PreservesPaletteIndexBits()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            // Build a buffer where every chipset slot 0 has palette index 15.
            byte[] input = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + MapEditorTilesetCore.CHIPSET_COUNT];
            // Palette index 15 in bits 12-15 = 0xF000. Plus a known tile id 0x123.
            ushort w = (ushort)(0xF000 | 0x0123);
            for (int c = 0; c < MapEditorTilesetCore.CHIPSET_COUNT; c++)
            {
                int tsaBase = c * 8;
                input[tsaBase + 0] = (byte)(w & 0xFF);
                input[tsaBase + 1] = (byte)((w >> 8) & 0xFF);
            }

            var undoData = CoreState.Undo.NewUndoData("preserve palette bits");
            bool ok;
            using (ROM.BeginUndoScope(undoData))
                ok = vm.TryWriteConfigBuffer(input, out _);
            Assert.True(ok);
            CoreState.Undo.Push(undoData);

            byte[] roundTripped = LZ77.decompress(rom.Data, vm.ChipsetConfigAddress);
            Assert.NotNull(roundTripped);

            // Sample a few chipsets to confirm the palette-index bits survived.
            int[] sampleChipsets = { 0, 1, 50, 500, 1023 };
            foreach (int c in sampleChipsets)
            {
                int off = c * 8;
                ushort readBack = (ushort)(roundTripped[off] | (roundTripped[off + 1] << 8));
                Assert.Equal(w, readBack);
                // Explicitly assert the high nibble (palette index) is 0xF.
                Assert.Equal(0xF, (readBack >> 12) & 0xF);
            }
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Copilot bot v1 inline review: the VM must clone the caller-supplied
    /// buffer before assigning it to the internal cache so a subsequent
    /// caller-side mutation cannot reach into the VM's state. Without the
    /// defensive clone, mutating <c>input</c> after a successful write
    /// would silently corrupt the cached CONFIG buffer used by the next
    /// chipset read.
    /// </summary>
    [Fact]
    public void TryWriteConfigBuffer_StoresDefensiveClone_NotCallerBuffer()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            byte[] input = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + MapEditorTilesetCore.CHIPSET_COUNT];
            // Seed chipset 0 TSA slot 0 with a known value.
            ushort original = (ushort)(0xF000 | 0x0123);
            input[0] = (byte)(original & 0xFF);
            input[1] = (byte)((original >> 8) & 0xFF);

            var undoData = CoreState.Undo.NewUndoData("defensive clone");
            using (ROM.BeginUndoScope(undoData))
                Assert.True(vm.TryWriteConfigBuffer(input, out _));
            CoreState.Undo.Push(undoData);

            // Mutate the caller's buffer AFTER the write — the VM's cache
            // must not change. Read back via TryLoadChipsetTSA so we go
            // through the public API (no reflection needed).
            input[0] = 0xCD;
            input[1] = 0xAB;

            Assert.True(vm.TryLoadChipsetTSA(0));
            Assert.Equal(original, vm.GetSlotW(0));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// When the resolved CONFIG plist is 0 or 0xFF (no usable slot), the
    /// VM must refuse the import without touching ROM.
    /// </summary>
    [Fact]
    public void TryWriteConfigBuffer_FailsOnInvalidConfigPlist()
    {
        byte[] configUz = BuildSyntheticConfig();
        var (rom, entryAddr, _) = MakeFe8uRomWithConfig(configUz);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);
            Assert.True(vm.CanEditChipsetConfig);

            byte[] input = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + MapEditorTilesetCore.CHIPSET_COUNT];

            vm.CurrentConfigPlist = 0;
            Assert.False(vm.TryWriteConfigBuffer(input, out string err0));
            Assert.NotEqual("", err0);

            vm.CurrentConfigPlist = 0xFF;
            Assert.False(vm.TryWriteConfigBuffer(input, out string errFF));
            Assert.NotEqual("", errFF);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Per v5 plan #1: WF parity Paste applies ALL four W values and
    /// the terrain, not just one slot. Verify each slot W and the
    /// terrain land in the VM after a paste. <see cref="MapStyleEditorViewModel.CopyChipset"/>
    /// captures both halves on its own (per PR #691 review item 1), so
    /// a single Copy Tile is enough.
    /// </summary>
    [Fact]
    public void Paste_AppliesAllFourWAndTerrain_NotJustOneSlot()
    {
        var vm = new MapStyleEditorViewModel();
        // Seed each slot with a distinct value.
        vm.SetSlotWByLogicalIndex(0, 0xAAAA);
        vm.SetSlotWByLogicalIndex(1, 0xBBBB);
        vm.SetSlotWByLogicalIndex(2, 0xCCCC);
        vm.SetSlotWByLogicalIndex(3, 0xDDDD);
        vm.CurrentTerrain = 0x42;
        vm.CopyChipset();

        // Mutate everything so we can prove paste rewrites it.
        vm.SetSlotWByLogicalIndex(0, 0x1111);
        vm.SetSlotWByLogicalIndex(1, 0x2222);
        vm.SetSlotWByLogicalIndex(2, 0x3333);
        vm.SetSlotWByLogicalIndex(3, 0x4444);
        vm.CurrentTerrain = 0x99;

        Assert.True(vm.Paste());
        Assert.Equal((ushort)0xAAAA, vm.GetSlotW(0));
        Assert.Equal((ushort)0xBBBB, vm.GetSlotW(2));
        Assert.Equal((ushort)0xCCCC, vm.GetSlotW(4));
        Assert.Equal((ushort)0xDDDD, vm.GetSlotW(6));
        Assert.Equal(0x42, vm.CurrentTerrain);
    }
}
