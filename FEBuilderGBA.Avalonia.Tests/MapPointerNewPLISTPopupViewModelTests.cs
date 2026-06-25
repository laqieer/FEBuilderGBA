using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless behavioral tests for <see cref="MapPointerNewPLISTPopupViewModel"/>
/// (issue #1433 — New-PLIST popup usage info + Extend state + overwrite gate).
/// Uses a synthetic FE8U ROM with planted PLIST bytes so each branch has a
/// hand-built expectation; the VM methods take the ROM explicitly.
/// </summary>
[Collection("SharedState")]
public class MapPointerNewPLISTPopupViewModelTests
{
    [Fact]
    public void UpdatePlistInfo_UsedEventPlist_FlipsIsAlreadyUse_PopulatesLink()
    {
        ROM rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
            mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
            objLow: 11, objHigh: 12);

        var vm = new MapPointerNewPLISTPopupViewModel();
        vm.Initialize();
        vm.UpdatePlistInfo(rom, 3);

        Assert.True(vm.IsAlreadyUse);
        Assert.False(string.IsNullOrEmpty(vm.LinkPlistInfo));
        Assert.Equal(3u, vm.PlistId);
    }

    [Fact]
    public void UpdatePlistInfo_UnusedEventPlist_ClearsIsAlreadyUse()
    {
        ROM rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
            mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
            objLow: 11, objHigh: 12);

        var vm = new MapPointerNewPLISTPopupViewModel();
        vm.Initialize();
        vm.UpdatePlistInfo(rom, 3);     // used → true
        Assert.True(vm.IsAlreadyUse);
        vm.UpdatePlistInfo(rom, 60);    // unused → false
        Assert.False(vm.IsAlreadyUse);
        Assert.False(string.IsNullOrEmpty(vm.LinkPlistInfo));
    }

    [Fact]
    public void UpdatePlistInfo_PlistZero_IsAlreadyUse()
    {
        ROM rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
            mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
            objLow: 11, objHigh: 12);

        var vm = new MapPointerNewPLISTPopupViewModel();
        vm.Initialize();
        vm.UpdatePlistInfo(rom, 0);

        Assert.True(vm.IsAlreadyUse); // reserved → OK must confirm
    }

    [Fact]
    public void InitUI_Split_HidesExtendButton_SetsMaxToCountMinus1_ShowsNote()
    {
        ROM rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
            mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
            objLow: 11, objHigh: 12);

        var vm = new MapPointerNewPLISTPopupViewModel();
        vm.Initialize();
        vm.InitUI(rom);

        // Extend always hidden (split/extend editor flow not wired).
        Assert.False(vm.ExtendVisible);
        // Split layout → count 256 → max 255.
        Assert.Equal(255u, vm.PlistMaximum);
        // Already-split → note shown, plain explanation hidden.
        Assert.True(vm.AlreadyExtendsVisible);
        Assert.False(vm.ExplanationVisible);
    }

    [Fact]
    public void InitUI_NonSplit_ShowsExplanation_NoExtendNote_MaxVanilla()
    {
        ROM rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
            mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
            objLow: 11, objHigh: 12);

        var vm = new MapPointerNewPLISTPopupViewModel();
        vm.Initialize();
        vm.InitUI(rom);

        Assert.False(vm.ExtendVisible);
        Assert.False(vm.AlreadyExtendsVisible);
        Assert.True(vm.ExplanationVisible);
        // FE8U vanilla default 0xEC → max 0xEB.
        Assert.Equal(0xEBu, vm.PlistMaximum);
    }

    [Fact]
    public void DefaultSearchType_IsEvent()
    {
        var vm = new MapPointerNewPLISTPopupViewModel();
        Assert.Equal(MapChangeCore.PlistType.EVENT, vm.SearchType);
    }

    [Fact]
    public void OverwriteConfirmMessage_IsNonEmpty()
    {
        // The OK handler shows this when IsAlreadyUse; assert the message exists.
        Assert.False(string.IsNullOrEmpty(MapPointerPlistUsageCore.OverwriteConfirmMessage()));
    }

    // --- synthetic builders (mirror MapPointerPlistUsageCoreTests) ---

    static ROM MakeFe8uRom()
    {
        var rom = new ROM();
        rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
        return rom;
    }

    static ROM MakeSplitFe8uRomWithMap(
        uint config, uint evt, uint mapchange, uint mappointer,
        uint anime1, uint anime2, uint palette, uint palette2,
        uint objLow, uint objHigh)
    {
        var rom = MakeFe8uRom();
        PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
            palette, palette2, objLow, objHigh);
        WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,        0x08800000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,    0x08801000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,    0x08801000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,           0x08802000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,           0x08802000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,   0x08803000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,     0x08804000u);
        WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,         0x08805000u);
        return rom;
    }

    static ROM MakeNonSplitFe8uRomWithMap(
        uint config, uint evt, uint mapchange, uint mappointer,
        uint anime1, uint anime2, uint palette, uint palette2,
        uint objLow, uint objHigh)
    {
        var rom = MakeFe8uRom();
        PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
            palette, palette2, objLow, objHigh);
        uint shared = 0x08800000u;
        WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,      shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,  shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,  shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,         shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,         shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,   shared);
        WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,       shared);
        return rom;
    }

    static void PlantMap(ROM rom,
        uint config, uint evt, uint mapchange, uint mappointer,
        uint anime1, uint anime2, uint palette, uint palette2,
        uint objLow, uint objHigh)
    {
        uint mapTableBase = 0x00700000u;
        uint dataSize = rom.RomInfo.map_setting_datasize;
        WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);

        int rec = (int)mapTableBase;
        WriteU32(rom.Data, rec + 0, 0x08123456u);
        ushort obj = (ushort)((objLow & 0xFF) | ((objHigh & 0xFF) << 8));
        rom.Data[rec + 4] = (byte)(obj & 0xFF);
        rom.Data[rec + 5] = (byte)((obj >> 8) & 0xFF);
        rom.Data[rec + 6] = (byte)palette;
        rom.Data[rec + 7] = (byte)config;
        rom.Data[rec + 8] = (byte)mappointer;
        rom.Data[rec + 9] = (byte)anime1;
        rom.Data[rec + 10] = (byte)anime2;
        rom.Data[rec + 11] = (byte)mapchange;
        rom.Data[rec + (int)rom.RomInfo.map_setting_event_plist_pos] = (byte)evt;
        if (palette2 != 0)
        {
            rom.Data[rec + 146] = (byte)palette2;
        }

        int term = (int)(mapTableBase + dataSize);
        WriteU32(rom.Data, term + 0, 0x00000000u);
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
