// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for FE7 obj2 secondary tileset append in MapStyleEditorViewModel
// (#689). Verify the chip preview cache _cachedObjData is the concatenation
// of (primary LZ77 obj) + (secondary LZ77 obj2) when obj2_plist != 0, and
// degrades gracefully (keeps primary-only cache) when obj2 decompression
// fails or obj2_plist == 0.
using System.Reflection;
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MapStyleEditorViewModelObj2Tests
{
    static void WriteU32(byte[] data, uint offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU32(byte[] data, int offset, uint value) =>
        WriteU32(data, (uint)offset, value);

    static byte[] GetCachedObjData(MapStyleEditorViewModel vm)
    {
        var field = typeof(MapStyleEditorViewModel).GetField(
            "_cachedObjData",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (byte[])field!.GetValue(vm)!;
    }

    /// <summary>
    /// Build a 64-byte deterministic raw tile buffer; LZ77 compresses it.
    /// </summary>
    static byte[] MakeRawObj(byte seed, int length = 64)
    {
        byte[] raw = new byte[length];
        for (int i = 0; i < length; i++) raw[i] = (byte)((seed + i) & 0xFF);
        return raw;
    }

    /// <summary>
    /// Synthesize a FE7U ROM with:
    ///  - map_obj_pointer table at 0x00890000
    ///  - primary obj LZ77 data at 0x00C00000 (plist 1)
    ///  - optional secondary obj2 LZ77 data at 0x00D00000 (plist 2)
    ///    OR optional corrupt non-LZ77 bytes at 0x00D00000 (plist 2)
    ///  - map_setting entry at 0x008A0000 with obj_plist u16 = (obj2Plist << 8) | 1
    /// Returns (rom, primaryObjEntryAddr, rawPrimary, rawSecondary-or-null).
    /// </summary>
    static (ROM rom, uint entryAddr, byte[] primaryUz, byte[]? secondaryUz)
        MakeFe7uRomWithObj2(byte obj2Plist, bool corruptObj2)
    {
        var rom = new ROM();
        rom.LoadLow("test-fe7u.gba", new byte[0x1100000], "AE7E01");

        byte[] primaryRaw = MakeRawObj(0x10);
        byte[] secondaryRaw = MakeRawObj(0x80);
        byte[] primaryCompressed = LZ77.compress(primaryRaw);
        byte[] secondaryCompressed = LZ77.compress(secondaryRaw);

        // Primary obj at 0x00C00000.
        uint primaryAddr = 0x00C00000u;
        for (int i = 0; i < primaryCompressed.Length; i++)
            rom.Data[primaryAddr + i] = primaryCompressed[i];

        // Secondary obj2 at 0x00D00000 (only used if obj2Plist > 0).
        uint secondaryAddr = 0x00D00000u;
        if (obj2Plist > 0)
        {
            if (corruptObj2)
            {
                // Plant a non-LZ77 header (LZ77 expects 0x10 in byte 0). Use
                // 0xFF so decompress throws.
                for (int i = 0; i < 64; i++)
                    rom.Data[secondaryAddr + i] = 0xFF;
            }
            else
            {
                for (int i = 0; i < secondaryCompressed.Length; i++)
                    rom.Data[secondaryAddr + i] = secondaryCompressed[i];
            }
        }

        // map_obj_pointer table at 0x00890000.
        // slot 1 -> primaryAddr | 0x08000000 (primary)
        // slot 2 -> secondaryAddr | 0x08000000 (secondary or corrupt)
        uint objTableAddr = 0x00890000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, objTableAddr | 0x08000000u);
        WriteU32(rom.Data, (int)(objTableAddr + 1 * 4u), primaryAddr | 0x08000000u);
        WriteU32(rom.Data, (int)(objTableAddr + 2 * 4u), secondaryAddr | 0x08000000u);

        // map_setting pointer at 0x008A0000. FE7U map_setting_datasize == 152.
        // Layout:
        //   +0: pointer-valued dword (makes IsMapSettingValid true)
        //   +4: u16 obj_plist (low byte = 1 = primary plist; high byte = obj2Plist)
        //   +6: u8 palette_plist (0)
        //   +7: u8 config_plist (0)
        uint mapTableAddr = 0x008A0000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableAddr | 0x08000000u);
        WriteU32(rom.Data, (int)mapTableAddr, 0x08123456u);
        ushort objPlistWord = (ushort)((obj2Plist << 8) | 0x01);
        rom.Data[mapTableAddr + 4] = (byte)(objPlistWord & 0xFF);
        rom.Data[mapTableAddr + 5] = (byte)((objPlistWord >> 8) & 0xFF);
        rom.Data[mapTableAddr + 6] = 0x00; // palette_plist
        rom.Data[mapTableAddr + 7] = 0x00; // config_plist
        // Terminator: next slot's first dword zeroed + weather byte > 0xE
        // ensures IsMapSettingValid returns false for slot 1.
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)(mapTableAddr + dataSize), 0x00000000u);
        rom.Data[mapTableAddr + dataSize + 12] = 0xFF; // weather byte > 0xE

        // The map_obj_pointer table entry address for plist 1 (primary).
        uint entryAddr = objTableAddr + 1 * 4u;
        return (rom, entryAddr, primaryRaw, obj2Plist > 0 && !corruptObj2 ? secondaryRaw : null);
    }

    [Fact]
    public void LoadEntry_FE7Style_AppendsObj2ToCachedObjData()
    {
        var (rom, entryAddr, primaryUz, secondaryUz) =
            MakeFe7uRomWithObj2(obj2Plist: 2, corruptObj2: false);
        Assert.NotNull(secondaryUz);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            // ObjAddress2 must resolve to the secondary obj entry.
            Assert.NotEqual(0u, vm.ObjAddress2);

            byte[] cache = GetCachedObjData(vm);
            Assert.NotNull(cache);
            Assert.Equal(primaryUz.Length + secondaryUz!.Length, cache.Length);

            // Primary bytes verbatim at [0..primaryUz.Length).
            for (int i = 0; i < primaryUz.Length; i++)
                Assert.Equal(primaryUz[i], cache[i]);
            // Secondary bytes verbatim at [primaryUz.Length .. end).
            for (int i = 0; i < secondaryUz.Length; i++)
                Assert.Equal(secondaryUz[i], cache[primaryUz.Length + i]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void LoadEntry_NoObj2_LeavesPrimaryOnly()
    {
        var (rom, entryAddr, primaryUz, _) =
            MakeFe7uRomWithObj2(obj2Plist: 0, corruptObj2: false);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();
            vm.LoadEntry(entryAddr);

            // No secondary plist -> ObjAddress2 stays 0.
            Assert.Equal(0u, vm.ObjAddress2);

            byte[] cache = GetCachedObjData(vm);
            Assert.NotNull(cache);
            Assert.Equal(primaryUz.Length, cache.Length);
            for (int i = 0; i < primaryUz.Length; i++)
                Assert.Equal(primaryUz[i], cache[i]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void LoadEntry_Obj2DecompressFails_KeepsPrimaryCache()
    {
        // obj2_plist = 2 but the table entry points at non-LZ77 bytes.
        var (rom, entryAddr, primaryUz, _) =
            MakeFe7uRomWithObj2(obj2Plist: 2, corruptObj2: true);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapStyleEditorViewModel();

            // Must not throw — try/catch in LoadEntry swallows the failure.
            vm.LoadEntry(entryAddr);

            // ObjAddress2 still resolves to the entry address (which contains
            // corrupt bytes). The cache must remain primary-only.
            byte[] cache = GetCachedObjData(vm);
            Assert.NotNull(cache);
            Assert.Equal(primaryUz.Length, cache.Length);
            for (int i = 0; i < primaryUz.Length; i++)
                Assert.Equal(primaryUz[i], cache[i]);
        }
        finally { CoreState.ROM = prevRom; }
    }
}
