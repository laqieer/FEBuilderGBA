using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        bool _isLoaded;
        uint _dataSize;

        uint _cpPointer;
        uint _objectTypePLIST;
        uint _palettePLIST, _chipsetConfigPLIST, _mapPointerPLIST;
        uint _tileAnimation1PLIST, _tileAnimation2PLIST, _mapChangePLIST;
        uint _fogLevel, _battlePreparation, _chapterTitleImage;
        uint _unknownB15, _unknownB16, _unknownB17;
        uint _weather, _battleBGLookup;
        uint _playerPhaseBGM, _enemyPhaseBGM, _npcPhaseBGM;
        uint _hardBoost, _unknownB24;
        uint _breakableWallHP, _unknownB26;
        uint _unknownB27, _unknownB28, _unknownB29, _unknownB30, _unknownB31;
        uint _playerPhaseBGMW, _enemyPhaseBGMW, _npcPhaseBGMW;
        uint _unknownW38, _unknownW40, _unknownW42;
        uint _unknownW44, _unknownW46;
        uint _clearConditionText;
        uint _upperArmyText, _lowerArmyText;
        uint _enemyBannerFlag;
        uint _chapterTitleText;
        uint _eventIdPLIST, _worldMapAutoEvent;
        uint _worldMapPlaceName;
        uint _chapterNumber;
        uint _worldMapX, _worldMapY, _worldMapPointX, _worldMapPointY;
        uint _victoryBGMEnemyCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }

        public uint CpPointer { get => _cpPointer; set => SetField(ref _cpPointer, value); }
        public uint ObjectTypePLIST { get => _objectTypePLIST; set => SetField(ref _objectTypePLIST, value); }
        public uint PalettePLIST { get => _palettePLIST; set => SetField(ref _palettePLIST, value); }
        public uint ChipsetConfigPLIST { get => _chipsetConfigPLIST; set => SetField(ref _chipsetConfigPLIST, value); }
        public uint MapPointerPLIST { get => _mapPointerPLIST; set => SetField(ref _mapPointerPLIST, value); }
        public uint TileAnimation1PLIST { get => _tileAnimation1PLIST; set => SetField(ref _tileAnimation1PLIST, value); }
        public uint TileAnimation2PLIST { get => _tileAnimation2PLIST; set => SetField(ref _tileAnimation2PLIST, value); }
        public uint MapChangePLIST { get => _mapChangePLIST; set => SetField(ref _mapChangePLIST, value); }
        public uint FogLevel { get => _fogLevel; set => SetField(ref _fogLevel, value); }
        public uint BattlePreparation { get => _battlePreparation; set => SetField(ref _battlePreparation, value); }
        public uint ChapterTitleImage { get => _chapterTitleImage; set => SetField(ref _chapterTitleImage, value); }
        public uint UnknownB15 { get => _unknownB15; set => SetField(ref _unknownB15, value); }
        public uint UnknownB16 { get => _unknownB16; set => SetField(ref _unknownB16, value); }
        public uint UnknownB17 { get => _unknownB17; set => SetField(ref _unknownB17, value); }
        public uint Weather { get => _weather; set => SetField(ref _weather, value); }
        public uint BattleBGLookup { get => _battleBGLookup; set => SetField(ref _battleBGLookup, value); }
        public uint PlayerPhaseBGM { get => _playerPhaseBGM; set => SetField(ref _playerPhaseBGM, value); }
        public uint EnemyPhaseBGM { get => _enemyPhaseBGM; set => SetField(ref _enemyPhaseBGM, value); }
        public uint NpcPhaseBGM { get => _npcPhaseBGM; set => SetField(ref _npcPhaseBGM, value); }
        public uint HardBoost { get => _hardBoost; set => SetField(ref _hardBoost, value); }
        public uint UnknownB24 { get => _unknownB24; set => SetField(ref _unknownB24, value); }
        public uint BreakableWallHP { get => _breakableWallHP; set => SetField(ref _breakableWallHP, value); }
        public uint UnknownB26 { get => _unknownB26; set => SetField(ref _unknownB26, value); }
        public uint UnknownB27 { get => _unknownB27; set => SetField(ref _unknownB27, value); }
        public uint UnknownB28 { get => _unknownB28; set => SetField(ref _unknownB28, value); }
        public uint UnknownB29 { get => _unknownB29; set => SetField(ref _unknownB29, value); }
        public uint UnknownB30 { get => _unknownB30; set => SetField(ref _unknownB30, value); }
        public uint UnknownB31 { get => _unknownB31; set => SetField(ref _unknownB31, value); }
        public uint PlayerPhaseBGMW { get => _playerPhaseBGMW; set => SetField(ref _playerPhaseBGMW, value); }
        public uint EnemyPhaseBGMW { get => _enemyPhaseBGMW; set => SetField(ref _enemyPhaseBGMW, value); }
        public uint NpcPhaseBGMW { get => _npcPhaseBGMW; set => SetField(ref _npcPhaseBGMW, value); }
        public uint UnknownW38 { get => _unknownW38; set => SetField(ref _unknownW38, value); }
        public uint UnknownW40 { get => _unknownW40; set => SetField(ref _unknownW40, value); }
        public uint UnknownW42 { get => _unknownW42; set => SetField(ref _unknownW42, value); }
        public uint UnknownW44 { get => _unknownW44; set => SetField(ref _unknownW44, value); }
        public uint UnknownW46 { get => _unknownW46; set => SetField(ref _unknownW46, value); }
        public uint ClearConditionText { get => _clearConditionText; set => SetField(ref _clearConditionText, value); }
        public uint UpperArmyText { get => _upperArmyText; set => SetField(ref _upperArmyText, value); }
        public uint LowerArmyText { get => _lowerArmyText; set => SetField(ref _lowerArmyText, value); }
        public uint EnemyBannerFlag { get => _enemyBannerFlag; set => SetField(ref _enemyBannerFlag, value); }
        public uint ChapterTitleText { get => _chapterTitleText; set => SetField(ref _chapterTitleText, value); }
        public uint EventIdPLIST { get => _eventIdPLIST; set => SetField(ref _eventIdPLIST, value); }
        public uint WorldMapAutoEvent { get => _worldMapAutoEvent; set => SetField(ref _worldMapAutoEvent, value); }
        public uint WorldMapPlaceName { get => _worldMapPlaceName; set => SetField(ref _worldMapPlaceName, value); }
        public uint ChapterNumber { get => _chapterNumber; set => SetField(ref _chapterNumber, value); }
        public uint WorldMapX { get => _worldMapX; set => SetField(ref _worldMapX, value); }
        public uint WorldMapY { get => _worldMapY; set => SetField(ref _worldMapY, value); }
        public uint WorldMapPointX { get => _worldMapPointX; set => SetField(ref _worldMapPointX, value); }
        public uint WorldMapPointY { get => _worldMapPointY; set => SetField(ref _worldMapPointY, value); }
        public uint VictoryBGMEnemyCount { get => _victoryBGMEnemyCount; set => SetField(ref _victoryBGMEnemyCount, value); }

        public List<AddrResult> LoadMapSettingList()
        {
            try { return MapSettingCore.MakeMapIDList(); }
            catch { return new List<AddrResult>(); }
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) dataSize = 68;
            if (addr + dataSize > (uint)rom.Data.Length) return;
            CurrentAddr = addr; DataSize = dataSize;

            CpPointer = rom.u32(addr + 0);
            ObjectTypePLIST = rom.u16(addr + 4);
            PalettePLIST = rom.u8(addr + 6);
            ChipsetConfigPLIST = rom.u8(addr + 7);
            MapPointerPLIST = rom.u8(addr + 8);
            TileAnimation1PLIST = rom.u8(addr + 9);
            TileAnimation2PLIST = rom.u8(addr + 10);
            MapChangePLIST = rom.u8(addr + 11);
            FogLevel = rom.u8(addr + 12);
            BattlePreparation = rom.u8(addr + 13);
            ChapterTitleImage = rom.u8(addr + 14);
            UnknownB15 = rom.u8(addr + 15);
            UnknownB16 = rom.u8(addr + 16);
            UnknownB17 = rom.u8(addr + 17);
            Weather = rom.u8(addr + 18);
            BattleBGLookup = rom.u8(addr + 19);
            PlayerPhaseBGM = rom.u8(addr + 20);
            EnemyPhaseBGM = rom.u8(addr + 21);
            NpcPhaseBGM = rom.u8(addr + 22);
            HardBoost = rom.u8(addr + 23);
            UnknownB24 = rom.u8(addr + 24);
            BreakableWallHP = rom.u8(addr + 25);
            UnknownB26 = rom.u8(addr + 26);
            UnknownB27 = rom.u8(addr + 27);
            UnknownB28 = rom.u8(addr + 28);
            UnknownB29 = rom.u8(addr + 29);
            UnknownB30 = rom.u8(addr + 30);
            UnknownB31 = rom.u8(addr + 31);
            PlayerPhaseBGMW = rom.u16(addr + 32);
            EnemyPhaseBGMW = rom.u16(addr + 34);
            NpcPhaseBGMW = rom.u16(addr + 36);
            UnknownW38 = rom.u16(addr + 38);
            UnknownW40 = rom.u16(addr + 40);
            UnknownW42 = rom.u16(addr + 42);
            UnknownW44 = rom.u16(addr + 44);
            UnknownW46 = rom.u16(addr + 46);
            ClearConditionText = rom.u16(addr + 48);
            UpperArmyText = rom.u16(addr + 50);
            LowerArmyText = rom.u16(addr + 52);
            EnemyBannerFlag = rom.u16(addr + 54);
            ChapterTitleText = rom.u16(addr + 56);
            if (dataSize > 58) EventIdPLIST = rom.u8(addr + 58);
            if (dataSize > 59) WorldMapAutoEvent = rom.u8(addr + 59);
            if (dataSize > 61) WorldMapPlaceName = rom.u16(addr + 60);
            if (dataSize > 62) ChapterNumber = rom.u8(addr + 62);
            if (dataSize > 63) WorldMapX = rom.u8(addr + 63);
            if (dataSize > 64) WorldMapY = rom.u8(addr + 64);
            if (dataSize > 65) WorldMapPointX = rom.u8(addr + 65);
            if (dataSize > 66) WorldMapPointY = rom.u8(addr + 66);
            if (dataSize > 67) VictoryBGMEnemyCount = rom.u8(addr + 67);

            try
            {
                uint nameTextPos = rom.RomInfo.map_setting_name_text_pos;
                if (nameTextPos > 0 && dataSize > nameTextPos + 1)
                    Name = NameResolver.GetTextById(rom.u16(addr + nameTextPos));
                else
                    Name = $"Map 0x{addr:X}";
            }
            catch { Name = "???"; }
            IsLoaded = true;
        }

        public void WriteMapSetting()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr; uint dataSize = DataSize;
            if (dataSize == 0) return;
            Undo undo = CoreState.Undo;
            Undo.UndoData undoData = null;
            if (undo != null) { undoData = undo.NewUndoData("MapSettingFE6"); undoData.list.Add(new Undo.UndoPostion(addr, dataSize)); }

            rom.write_u32(addr + 0, CpPointer);
            rom.write_u16(addr + 4, ObjectTypePLIST);
            rom.write_u8(addr + 6, PalettePLIST); rom.write_u8(addr + 7, ChipsetConfigPLIST);
            rom.write_u8(addr + 8, MapPointerPLIST); rom.write_u8(addr + 9, TileAnimation1PLIST);
            rom.write_u8(addr + 10, TileAnimation2PLIST); rom.write_u8(addr + 11, MapChangePLIST);
            rom.write_u8(addr + 12, FogLevel); rom.write_u8(addr + 13, BattlePreparation);
            rom.write_u8(addr + 14, ChapterTitleImage); rom.write_u8(addr + 15, UnknownB15);
            rom.write_u8(addr + 16, UnknownB16); rom.write_u8(addr + 17, UnknownB17);
            rom.write_u8(addr + 18, Weather); rom.write_u8(addr + 19, BattleBGLookup);
            rom.write_u8(addr + 20, PlayerPhaseBGM); rom.write_u8(addr + 21, EnemyPhaseBGM);
            rom.write_u8(addr + 22, NpcPhaseBGM); rom.write_u8(addr + 23, HardBoost);
            rom.write_u8(addr + 24, UnknownB24); rom.write_u8(addr + 25, BreakableWallHP);
            rom.write_u8(addr + 26, UnknownB26); rom.write_u8(addr + 27, UnknownB27);
            rom.write_u8(addr + 28, UnknownB28); rom.write_u8(addr + 29, UnknownB29);
            rom.write_u8(addr + 30, UnknownB30); rom.write_u8(addr + 31, UnknownB31);
            rom.write_u16(addr + 32, PlayerPhaseBGMW); rom.write_u16(addr + 34, EnemyPhaseBGMW);
            rom.write_u16(addr + 36, NpcPhaseBGMW); rom.write_u16(addr + 38, UnknownW38);
            rom.write_u16(addr + 40, UnknownW40); rom.write_u16(addr + 42, UnknownW42);
            rom.write_u16(addr + 44, UnknownW44); rom.write_u16(addr + 46, UnknownW46);
            rom.write_u16(addr + 48, ClearConditionText); rom.write_u16(addr + 50, UpperArmyText);
            rom.write_u16(addr + 52, LowerArmyText); rom.write_u16(addr + 54, EnemyBannerFlag);
            rom.write_u16(addr + 56, ChapterTitleText);
            if (dataSize > 58) rom.write_u8(addr + 58, EventIdPLIST);
            if (dataSize > 59) rom.write_u8(addr + 59, WorldMapAutoEvent);
            if (dataSize > 61) rom.write_u16(addr + 60, WorldMapPlaceName);
            if (dataSize > 62) rom.write_u8(addr + 62, ChapterNumber);
            if (dataSize > 63) rom.write_u8(addr + 63, WorldMapX);
            if (dataSize > 64) rom.write_u8(addr + 64, WorldMapY);
            if (dataSize > 65) rom.write_u8(addr + 65, WorldMapPointX);
            if (dataSize > 66) rom.write_u8(addr + 66, WorldMapPointY);
            if (dataSize > 67) rom.write_u8(addr + 67, VictoryBGMEnemyCount);
            if (undo != null && undoData != null) undo.Push(undoData);
        }

        public int GetListCount() => LoadMapSettingList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}", ["dataSize"] = $"{DataSize}",
                ["CpPointer"] = $"0x{CpPointer:X08}", ["ObjectTypePLIST"] = $"0x{ObjectTypePLIST:X04}",
                ["PalettePLIST"] = $"0x{PalettePLIST:X02}", ["ChipsetConfigPLIST"] = $"0x{ChipsetConfigPLIST:X02}",
                ["MapPointerPLIST"] = $"0x{MapPointerPLIST:X02}", ["TileAnimation1PLIST"] = $"0x{TileAnimation1PLIST:X02}",
                ["TileAnimation2PLIST"] = $"0x{TileAnimation2PLIST:X02}", ["MapChangePLIST"] = $"0x{MapChangePLIST:X02}",
                ["FogLevel"] = $"0x{FogLevel:X02}", ["BattlePreparation"] = $"0x{BattlePreparation:X02}",
                ["ChapterTitleImage"] = $"0x{ChapterTitleImage:X02}", ["UnknownB15"] = $"0x{UnknownB15:X02}",
                ["UnknownB16"] = $"0x{UnknownB16:X02}", ["UnknownB17"] = $"0x{UnknownB17:X02}",
                ["Weather"] = $"0x{Weather:X02}", ["BattleBGLookup"] = $"0x{BattleBGLookup:X02}",
                ["PlayerPhaseBGM"] = $"0x{PlayerPhaseBGM:X02}", ["EnemyPhaseBGM"] = $"0x{EnemyPhaseBGM:X02}",
                ["NpcPhaseBGM"] = $"0x{NpcPhaseBGM:X02}", ["HardBoost"] = $"0x{HardBoost:X02}",
                ["UnknownB24"] = $"0x{UnknownB24:X02}", ["BreakableWallHP"] = $"0x{BreakableWallHP:X02}",
                ["UnknownB26"] = $"0x{UnknownB26:X02}", ["UnknownB27"] = $"0x{UnknownB27:X02}",
                ["UnknownB28"] = $"0x{UnknownB28:X02}", ["UnknownB29"] = $"0x{UnknownB29:X02}",
                ["UnknownB30"] = $"0x{UnknownB30:X02}", ["UnknownB31"] = $"0x{UnknownB31:X02}",
                ["PlayerPhaseBGMW"] = $"0x{PlayerPhaseBGMW:X04}", ["EnemyPhaseBGMW"] = $"0x{EnemyPhaseBGMW:X04}",
                ["NpcPhaseBGMW"] = $"0x{NpcPhaseBGMW:X04}", ["UnknownW38"] = $"0x{UnknownW38:X04}",
                ["UnknownW40"] = $"0x{UnknownW40:X04}", ["UnknownW42"] = $"0x{UnknownW42:X04}",
                ["UnknownW44"] = $"0x{UnknownW44:X04}", ["UnknownW46"] = $"0x{UnknownW46:X04}",
                ["ClearConditionText"] = $"0x{ClearConditionText:X04}", ["UpperArmyText"] = $"0x{UpperArmyText:X04}",
                ["LowerArmyText"] = $"0x{LowerArmyText:X04}", ["EnemyBannerFlag"] = $"0x{EnemyBannerFlag:X04}",
                ["ChapterTitleText"] = $"0x{ChapterTitleText:X04}", ["EventIdPLIST"] = $"0x{EventIdPLIST:X02}",
                ["WorldMapAutoEvent"] = $"0x{WorldMapAutoEvent:X02}", ["WorldMapPlaceName"] = $"0x{WorldMapPlaceName:X04}",
                ["ChapterNumber"] = $"0x{ChapterNumber:X02}", ["WorldMapX"] = $"0x{WorldMapX:X02}",
                ["WorldMapY"] = $"0x{WorldMapY:X02}", ["WorldMapPointX"] = $"0x{WorldMapPointX:X02}",
                ["WorldMapPointY"] = $"0x{WorldMapPointY:X02}", ["VictoryBGMEnemyCount"] = $"0x{VictoryBGMEnemyCount:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr; uint ds = DataSize;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}", ["dataSize"] = $"{ds}",
                ["u32@0x00_CpPointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04_ObjectTypePLIST"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06_PalettePLIST"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_ChipsetConfigPLIST"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08_MapPointerPLIST"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_TileAnimation1PLIST"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_TileAnimation2PLIST"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_MapChangePLIST"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_FogLevel"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_BattlePreparation"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_ChapterTitleImage"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_UnknownB15"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_UnknownB16"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_UnknownB17"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12_Weather"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13_BattleBGLookup"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14_PlayerPhaseBGM"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_EnemyPhaseBGM"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_NpcPhaseBGM"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_HardBoost"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18_UnknownB24"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19_BreakableWallHP"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A_UnknownB26"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B_UnknownB27"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C_UnknownB28"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D_UnknownB29"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E_UnknownB30"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F_UnknownB31"] = $"0x{rom.u8(a + 31):X02}",
                ["u16@0x20_PlayerPhaseBGMW"] = $"0x{rom.u16(a + 32):X04}",
                ["u16@0x22_EnemyPhaseBGMW"] = $"0x{rom.u16(a + 34):X04}",
                ["u16@0x24_NpcPhaseBGMW"] = $"0x{rom.u16(a + 36):X04}",
                ["u16@0x26_UnknownW38"] = $"0x{rom.u16(a + 38):X04}",
                ["u16@0x28_UnknownW40"] = $"0x{rom.u16(a + 40):X04}",
                ["u16@0x2A_UnknownW42"] = $"0x{rom.u16(a + 42):X04}",
                ["u16@0x2C_UnknownW44"] = $"0x{rom.u16(a + 44):X04}",
                ["u16@0x2E_UnknownW46"] = $"0x{rom.u16(a + 46):X04}",
                ["u16@0x30_ClearConditionText"] = $"0x{rom.u16(a + 48):X04}",
                ["u16@0x32_UpperArmyText"] = $"0x{rom.u16(a + 50):X04}",
                ["u16@0x34_LowerArmyText"] = $"0x{rom.u16(a + 52):X04}",
                ["u16@0x36_EnemyBannerFlag"] = $"0x{rom.u16(a + 54):X04}",
                ["u16@0x38_ChapterTitleText"] = $"0x{rom.u16(a + 56):X04}",
            };
            if (ds > 58) report["u8@0x3A_EventIdPLIST"] = $"0x{rom.u8(a + 58):X02}";
            if (ds > 59) report["u8@0x3B_WorldMapAutoEvent"] = $"0x{rom.u8(a + 59):X02}";
            if (ds > 61) report["u16@0x3C_WorldMapPlaceName"] = $"0x{rom.u16(a + 60):X04}";
            if (ds > 62) report["u8@0x3E_ChapterNumber"] = $"0x{rom.u8(a + 62):X02}";
            if (ds > 63) report["u8@0x3F_WorldMapX"] = $"0x{rom.u8(a + 63):X02}";
            if (ds > 64) report["u8@0x40_WorldMapY"] = $"0x{rom.u8(a + 64):X02}";
            if (ds > 65) report["u8@0x41_WorldMapPointX"] = $"0x{rom.u8(a + 65):X02}";
            if (ds > 66) report["u8@0x42_WorldMapPointY"] = $"0x{rom.u8(a + 66):X02}";
            if (ds > 67) report["u8@0x43_VictoryBGMEnemyCount"] = $"0x{rom.u8(a + 67):X02}";
            return report;
        }

        public List<AddrResult> LoadList() => LoadMapSettingList();
        public uint TilesetPLIST => ObjectTypePLIST;
        public uint MapPLIST => MapPointerPLIST;
    }
}
