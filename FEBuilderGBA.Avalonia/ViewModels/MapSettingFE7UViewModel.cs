using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingFE7UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        bool _isLoaded;
        uint _dataSize;

        // --- ChapterPointer: Pointer/CString at offset 0 ---
        uint _chapterPointer;
        // --- ObjectTypePLIST: Object type PLIST (u16) ---
        uint _objectTypePLIST;
        // --- PalettePLIST-BattleBG: byte fields ---
        uint _palettePLIST, _chipsetConfigPLIST, _mapPointerPLIST, _tileAnimation1PLIST, _tileAnimation2PLIST, _mapChangePLIST;
        uint _fogLevel, _battlePreparation, _chapterTitleImage, _chapterTitleImage2, _padding16, _padding17, _weather, _battleBG;
        // --- DifficultyAdjust-PrologueBGMHector: word fields ---
        uint _difficultyAdjust, _playerPhaseBGM, _enemyPhaseBGM, _npcBGM, _playerPhaseBGM2, _enemyPhaseBGM2, _npcBGM2, _playerPhaseBGMFlag4, _enemyPhaseBGMFlag4, _prologueBGMCommon, _prologueBGMEliwood, _prologueBGMHector;
        // --- BreakableWallHP-UnknownB61: byte fields ---
        uint _breakableWallHP, _ratingAEliwoodNormal, _ratingAEliwoodHard, _ratingAHectorNormal, _ratingAHectorHard, _ratingBEliwoodNormal, _ratingBEliwoodHard, _ratingBHectorNormal;
        uint _ratingBHectorHard, _ratingCEliwoodNormal, _ratingCEliwoodHard, _ratingCHectorNormal, _ratingCHectorHard, _ratingDEliwoodNormal, _ratingDEliwoodHard, _ratingDHectorNormal, _ratingDHectorHard, _unknownB61;
        // --- RatingAEliwoodNormalW-UnknownW94: word fields ---
        uint _ratingAEliwoodNormalW, _ratingAEliwoodHardW, _ratingAHectorNormalW, _ratingAHectorHardW, _ratingBEliwoodNormalW, _ratingBEliwoodHardW, _ratingBHectorNormalW, _ratingBHectorHardW;
        uint _ratingCEliwoodNormalW, _ratingCEliwoodHardW, _ratingCHectorNormalW, _ratingCHectorHardW, _ratingDEliwoodNormalW, _ratingDEliwoodHardW, _ratingDHectorNormalW, _ratingDHectorHardW, _unknownW94;
        // --- EliwoodNormalPtr-HectorHardPtr: dword fields ---
        uint _eliwoodNormalPtr, _eliwoodHardPtr, _hectorNormalPtr, _hectorHardPtr;
        // --- ChapterTitleEliwoodTextId-ChapterTitleHectorText2Id: word fields ---
        uint _chapterTitleEliwoodTextId, _chapterTitleHectorTextId, _chapterTitleEliwoodText2Id, _chapterTitleHectorText2Id;
        // --- EventPLIST, WorldMapAutoEvent ---
        uint _eventPLIST, _worldMapAutoEvent;
        // --- FortuneDialogOpeningTextId-FortuneDialogConfirmTextId: word fields ---
        uint _fortuneDialogOpeningTextId, _fortuneDialogEliwoodTextId, _fortuneDialogHectorTextId, _fortuneDialogConfirmTextId;
        // --- FortunePortrait-DarkenBeforeStartEvent ---
        uint _fortunePortrait, _fortuneFee, _prepScreenChNo1, _prepScreenChNo2, _unknownB134, _unknownB135, _unknownB136, _unknownB137, _victoryBGMEnemyCount, _darkenBeforeStartEvent;
        // --- ClearConditionTextId, DetailClearConditionTextId: text IDs ---
        uint _clearConditionTextId, _detailClearConditionTextId;
        // --- SpecialDisplay-UnknownB151 ---
        uint _specialDisplay, _turnCountDisplay, _defenseUnitMark, _escapeMarkerX, _escapeMarkerY, _unknownB149, _unknownB150, _unknownB151;

        // ---- Properties ----

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }

        // ChapterPointer: CP / CString pointer
        public uint ChapterPointer { get => _chapterPointer; set => SetField(ref _chapterPointer, value); }

        // ObjectTypePLIST: Object type PLIST
        public uint ObjectTypePLIST { get => _objectTypePLIST; set => SetField(ref _objectTypePLIST, value); }

        // PalettePLIST-BattleBG
        public uint PalettePLIST { get => _palettePLIST; set => SetField(ref _palettePLIST, value); }
        public uint ChipsetConfigPLIST { get => _chipsetConfigPLIST; set => SetField(ref _chipsetConfigPLIST, value); }
        public uint MapPointerPLIST { get => _mapPointerPLIST; set => SetField(ref _mapPointerPLIST, value); }
        public uint TileAnimation1PLIST { get => _tileAnimation1PLIST; set => SetField(ref _tileAnimation1PLIST, value); }
        public uint TileAnimation2PLIST { get => _tileAnimation2PLIST; set => SetField(ref _tileAnimation2PLIST, value); }
        public uint MapChangePLIST { get => _mapChangePLIST; set => SetField(ref _mapChangePLIST, value); }
        public uint FogLevel { get => _fogLevel; set => SetField(ref _fogLevel, value); }
        public uint BattlePreparation { get => _battlePreparation; set => SetField(ref _battlePreparation, value); }
        public uint ChapterTitleImage { get => _chapterTitleImage; set => SetField(ref _chapterTitleImage, value); }
        public uint ChapterTitleImage2 { get => _chapterTitleImage2; set => SetField(ref _chapterTitleImage2, value); }
        public uint Padding16 { get => _padding16; set => SetField(ref _padding16, value); }
        public uint Padding17 { get => _padding17; set => SetField(ref _padding17, value); }
        public uint Weather { get => _weather; set => SetField(ref _weather, value); }
        public uint BattleBG { get => _battleBG; set => SetField(ref _battleBG, value); }

        // DifficultyAdjust-PrologueBGMHector
        public uint DifficultyAdjust { get => _difficultyAdjust; set => SetField(ref _difficultyAdjust, value); }
        public uint PlayerPhaseBGM { get => _playerPhaseBGM; set => SetField(ref _playerPhaseBGM, value); }
        public uint EnemyPhaseBGM { get => _enemyPhaseBGM; set => SetField(ref _enemyPhaseBGM, value); }
        public uint NpcBGM { get => _npcBGM; set => SetField(ref _npcBGM, value); }
        public uint PlayerPhaseBGM2 { get => _playerPhaseBGM2; set => SetField(ref _playerPhaseBGM2, value); }
        public uint EnemyPhaseBGM2 { get => _enemyPhaseBGM2; set => SetField(ref _enemyPhaseBGM2, value); }
        public uint NpcBGM2 { get => _npcBGM2; set => SetField(ref _npcBGM2, value); }
        public uint PlayerPhaseBGMFlag4 { get => _playerPhaseBGMFlag4; set => SetField(ref _playerPhaseBGMFlag4, value); }
        public uint EnemyPhaseBGMFlag4 { get => _enemyPhaseBGMFlag4; set => SetField(ref _enemyPhaseBGMFlag4, value); }
        public uint PrologueBGMCommon { get => _prologueBGMCommon; set => SetField(ref _prologueBGMCommon, value); }
        public uint PrologueBGMEliwood { get => _prologueBGMEliwood; set => SetField(ref _prologueBGMEliwood, value); }
        public uint PrologueBGMHector { get => _prologueBGMHector; set => SetField(ref _prologueBGMHector, value); }

        // BreakableWallHP-UnknownB61
        public uint BreakableWallHP { get => _breakableWallHP; set => SetField(ref _breakableWallHP, value); }
        public uint RatingAEliwoodNormal { get => _ratingAEliwoodNormal; set => SetField(ref _ratingAEliwoodNormal, value); }
        public uint RatingAEliwoodHard { get => _ratingAEliwoodHard; set => SetField(ref _ratingAEliwoodHard, value); }
        public uint RatingAHectorNormal { get => _ratingAHectorNormal; set => SetField(ref _ratingAHectorNormal, value); }
        public uint RatingAHectorHard { get => _ratingAHectorHard; set => SetField(ref _ratingAHectorHard, value); }
        public uint RatingBEliwoodNormal { get => _ratingBEliwoodNormal; set => SetField(ref _ratingBEliwoodNormal, value); }
        public uint RatingBEliwoodHard { get => _ratingBEliwoodHard; set => SetField(ref _ratingBEliwoodHard, value); }
        public uint RatingBHectorNormal { get => _ratingBHectorNormal; set => SetField(ref _ratingBHectorNormal, value); }
        public uint RatingBHectorHard { get => _ratingBHectorHard; set => SetField(ref _ratingBHectorHard, value); }
        public uint RatingCEliwoodNormal { get => _ratingCEliwoodNormal; set => SetField(ref _ratingCEliwoodNormal, value); }
        public uint RatingCEliwoodHard { get => _ratingCEliwoodHard; set => SetField(ref _ratingCEliwoodHard, value); }
        public uint RatingCHectorNormal { get => _ratingCHectorNormal; set => SetField(ref _ratingCHectorNormal, value); }
        public uint RatingCHectorHard { get => _ratingCHectorHard; set => SetField(ref _ratingCHectorHard, value); }
        public uint RatingDEliwoodNormal { get => _ratingDEliwoodNormal; set => SetField(ref _ratingDEliwoodNormal, value); }
        public uint RatingDEliwoodHard { get => _ratingDEliwoodHard; set => SetField(ref _ratingDEliwoodHard, value); }
        public uint RatingDHectorNormal { get => _ratingDHectorNormal; set => SetField(ref _ratingDHectorNormal, value); }
        public uint RatingDHectorHard { get => _ratingDHectorHard; set => SetField(ref _ratingDHectorHard, value); }
        public uint UnknownB61 { get => _unknownB61; set => SetField(ref _unknownB61, value); }

        // RatingAEliwoodNormalW-UnknownW94
        public uint RatingAEliwoodNormalW { get => _ratingAEliwoodNormalW; set => SetField(ref _ratingAEliwoodNormalW, value); }
        public uint RatingAEliwoodHardW { get => _ratingAEliwoodHardW; set => SetField(ref _ratingAEliwoodHardW, value); }
        public uint RatingAHectorNormalW { get => _ratingAHectorNormalW; set => SetField(ref _ratingAHectorNormalW, value); }
        public uint RatingAHectorHardW { get => _ratingAHectorHardW; set => SetField(ref _ratingAHectorHardW, value); }
        public uint RatingBEliwoodNormalW { get => _ratingBEliwoodNormalW; set => SetField(ref _ratingBEliwoodNormalW, value); }
        public uint RatingBEliwoodHardW { get => _ratingBEliwoodHardW; set => SetField(ref _ratingBEliwoodHardW, value); }
        public uint RatingBHectorNormalW { get => _ratingBHectorNormalW; set => SetField(ref _ratingBHectorNormalW, value); }
        public uint RatingBHectorHardW { get => _ratingBHectorHardW; set => SetField(ref _ratingBHectorHardW, value); }
        public uint RatingCEliwoodNormalW { get => _ratingCEliwoodNormalW; set => SetField(ref _ratingCEliwoodNormalW, value); }
        public uint RatingCEliwoodHardW { get => _ratingCEliwoodHardW; set => SetField(ref _ratingCEliwoodHardW, value); }
        public uint RatingCHectorNormalW { get => _ratingCHectorNormalW; set => SetField(ref _ratingCHectorNormalW, value); }
        public uint RatingCHectorHardW { get => _ratingCHectorHardW; set => SetField(ref _ratingCHectorHardW, value); }
        public uint RatingDEliwoodNormalW { get => _ratingDEliwoodNormalW; set => SetField(ref _ratingDEliwoodNormalW, value); }
        public uint RatingDEliwoodHardW { get => _ratingDEliwoodHardW; set => SetField(ref _ratingDEliwoodHardW, value); }
        public uint RatingDHectorNormalW { get => _ratingDHectorNormalW; set => SetField(ref _ratingDHectorNormalW, value); }
        public uint RatingDHectorHardW { get => _ratingDHectorHardW; set => SetField(ref _ratingDHectorHardW, value); }
        public uint UnknownW94 { get => _unknownW94; set => SetField(ref _unknownW94, value); }

        // EliwoodNormalPtr-HectorHardPtr
        public uint EliwoodNormalPtr { get => _eliwoodNormalPtr; set => SetField(ref _eliwoodNormalPtr, value); }
        public uint EliwoodHardPtr { get => _eliwoodHardPtr; set => SetField(ref _eliwoodHardPtr, value); }
        public uint HectorNormalPtr { get => _hectorNormalPtr; set => SetField(ref _hectorNormalPtr, value); }
        public uint HectorHardPtr { get => _hectorHardPtr; set => SetField(ref _hectorHardPtr, value); }

        // ChapterTitleEliwoodTextId-ChapterTitleHectorText2Id: map name text IDs and related
        public uint ChapterTitleEliwoodTextId { get => _chapterTitleEliwoodTextId; set => SetField(ref _chapterTitleEliwoodTextId, value); }
        public uint ChapterTitleHectorTextId { get => _chapterTitleHectorTextId; set => SetField(ref _chapterTitleHectorTextId, value); }
        public uint ChapterTitleEliwoodText2Id { get => _chapterTitleEliwoodText2Id; set => SetField(ref _chapterTitleEliwoodText2Id, value); }
        public uint ChapterTitleHectorText2Id { get => _chapterTitleHectorText2Id; set => SetField(ref _chapterTitleHectorText2Id, value); }

        // EventPLIST, WorldMapAutoEvent: event ID PLIST, world map auto event
        public uint EventPLIST { get => _eventPLIST; set => SetField(ref _eventPLIST, value); }
        public uint WorldMapAutoEvent { get => _worldMapAutoEvent; set => SetField(ref _worldMapAutoEvent, value); }

        // FortuneDialogOpeningTextId-FortuneDialogConfirmTextId
        public uint FortuneDialogOpeningTextId { get => _fortuneDialogOpeningTextId; set => SetField(ref _fortuneDialogOpeningTextId, value); }
        public uint FortuneDialogEliwoodTextId { get => _fortuneDialogEliwoodTextId; set => SetField(ref _fortuneDialogEliwoodTextId, value); }
        public uint FortuneDialogHectorTextId { get => _fortuneDialogHectorTextId; set => SetField(ref _fortuneDialogHectorTextId, value); }
        public uint FortuneDialogConfirmTextId { get => _fortuneDialogConfirmTextId; set => SetField(ref _fortuneDialogConfirmTextId, value); }

        // FortunePortrait-DarkenBeforeStartEvent
        public uint FortunePortrait { get => _fortunePortrait; set => SetField(ref _fortunePortrait, value); }
        public uint FortuneFee { get => _fortuneFee; set => SetField(ref _fortuneFee, value); }
        public uint PrepScreenChNo1 { get => _prepScreenChNo1; set => SetField(ref _prepScreenChNo1, value); }
        public uint PrepScreenChNo2 { get => _prepScreenChNo2; set => SetField(ref _prepScreenChNo2, value); }
        public uint UnknownB134 { get => _unknownB134; set => SetField(ref _unknownB134, value); }
        public uint UnknownB135 { get => _unknownB135; set => SetField(ref _unknownB135, value); }
        public uint UnknownB136 { get => _unknownB136; set => SetField(ref _unknownB136, value); }
        public uint UnknownB137 { get => _unknownB137; set => SetField(ref _unknownB137, value); }
        public uint VictoryBGMEnemyCount { get => _victoryBGMEnemyCount; set => SetField(ref _victoryBGMEnemyCount, value); }
        public uint DarkenBeforeStartEvent { get => _darkenBeforeStartEvent; set => SetField(ref _darkenBeforeStartEvent, value); }

        // ClearConditionTextId, DetailClearConditionTextId: text IDs
        public uint ClearConditionTextId { get => _clearConditionTextId; set => SetField(ref _clearConditionTextId, value); }
        public uint DetailClearConditionTextId { get => _detailClearConditionTextId; set => SetField(ref _detailClearConditionTextId, value); }

        // SpecialDisplay-UnknownB151
        public uint SpecialDisplay { get => _specialDisplay; set => SetField(ref _specialDisplay, value); }
        public uint TurnCountDisplay { get => _turnCountDisplay; set => SetField(ref _turnCountDisplay, value); }
        public uint DefenseUnitMark { get => _defenseUnitMark; set => SetField(ref _defenseUnitMark, value); }
        public uint EscapeMarkerX { get => _escapeMarkerX; set => SetField(ref _escapeMarkerX, value); }
        public uint EscapeMarkerY { get => _escapeMarkerY; set => SetField(ref _escapeMarkerY, value); }
        public uint UnknownB149 { get => _unknownB149; set => SetField(ref _unknownB149, value); }
        public uint UnknownB150 { get => _unknownB150; set => SetField(ref _unknownB150, value); }
        public uint UnknownB151 { get => _unknownB151; set => SetField(ref _unknownB151, value); }

        // ---- Helpers for label text resolution ----

        public string ChapterTitleEliwoodText => ResolveText(ChapterTitleEliwoodTextId);
        public string ChapterTitleHectorText => ResolveText(ChapterTitleHectorTextId);
        public string ChapterTitleEliwoodText2 => ResolveText(ChapterTitleEliwoodText2Id);
        public string ChapterTitleHectorText2 => ResolveText(ChapterTitleHectorText2Id);
        public string ClearConditionText => ResolveText(ClearConditionTextId);
        public string DetailClearConditionText => ResolveText(DetailClearConditionTextId);

        static string ResolveText(uint id)
        {
            if (id == 0) return "(none)";
            try { return NameResolver.GetTextById(id); }
            catch { return "???"; }
        }

        // ---- List ----

        public List<AddrResult> LoadList() => LoadMapSettingList();

        public List<AddrResult> LoadMapSettingList()
        {
            try
            {
                return MapSettingCore.MakeMapIDList();
            }
            catch
            {
                return new List<AddrResult>();
            }
        }

        // ---- Load all fields from ROM ----

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) dataSize = 152;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataSize = dataSize;

            // ChapterPointer: dword at offset 0
            ChapterPointer = rom.u32(addr + 0);

            // ObjectTypePLIST: word at offset 4
            ObjectTypePLIST = rom.u16(addr + 4);

            // PalettePLIST-BattleBG
            PalettePLIST = rom.u8(addr + 6);
            ChipsetConfigPLIST = rom.u8(addr + 7);
            MapPointerPLIST = rom.u8(addr + 8);
            TileAnimation1PLIST = rom.u8(addr + 9);
            TileAnimation2PLIST = rom.u8(addr + 10);
            MapChangePLIST = rom.u8(addr + 11);
            FogLevel = rom.u8(addr + 12);
            BattlePreparation = rom.u8(addr + 13);
            ChapterTitleImage = rom.u8(addr + 14);
            ChapterTitleImage2 = rom.u8(addr + 15);
            Padding16 = rom.u8(addr + 16);
            Padding17 = rom.u8(addr + 17);
            Weather = rom.u8(addr + 18);
            BattleBG = rom.u8(addr + 19);

            // DifficultyAdjust-PrologueBGMHector
            DifficultyAdjust = rom.u16(addr + 20);
            PlayerPhaseBGM = rom.u16(addr + 22);
            EnemyPhaseBGM = rom.u16(addr + 24);
            NpcBGM = rom.u16(addr + 26);
            PlayerPhaseBGM2 = rom.u16(addr + 28);
            EnemyPhaseBGM2 = rom.u16(addr + 30);
            NpcBGM2 = rom.u16(addr + 32);
            PlayerPhaseBGMFlag4 = rom.u16(addr + 34);
            EnemyPhaseBGMFlag4 = rom.u16(addr + 36);
            PrologueBGMCommon = rom.u16(addr + 38);
            PrologueBGMEliwood = rom.u16(addr + 40);
            PrologueBGMHector = rom.u16(addr + 42);

            // BreakableWallHP-UnknownB61
            BreakableWallHP = rom.u8(addr + 44);
            if (dataSize > 45) RatingAEliwoodNormal = rom.u8(addr + 45);
            if (dataSize > 46) RatingAEliwoodHard = rom.u8(addr + 46);
            if (dataSize > 47) RatingAHectorNormal = rom.u8(addr + 47);
            if (dataSize > 48) RatingAHectorHard = rom.u8(addr + 48);
            if (dataSize > 49) RatingBEliwoodNormal = rom.u8(addr + 49);
            if (dataSize > 50) RatingBEliwoodHard = rom.u8(addr + 50);
            if (dataSize > 51) RatingBHectorNormal = rom.u8(addr + 51);
            if (dataSize > 52) RatingBHectorHard = rom.u8(addr + 52);
            if (dataSize > 53) RatingCEliwoodNormal = rom.u8(addr + 53);
            if (dataSize > 54) RatingCEliwoodHard = rom.u8(addr + 54);
            if (dataSize > 55) RatingCHectorNormal = rom.u8(addr + 55);
            if (dataSize > 56) RatingCHectorHard = rom.u8(addr + 56);
            if (dataSize > 57) RatingDEliwoodNormal = rom.u8(addr + 57);
            if (dataSize > 58) RatingDEliwoodHard = rom.u8(addr + 58);
            if (dataSize > 59) RatingDHectorNormal = rom.u8(addr + 59);
            if (dataSize > 60) RatingDHectorHard = rom.u8(addr + 60);
            if (dataSize > 61) UnknownB61 = rom.u8(addr + 61);

            // RatingAEliwoodNormalW-UnknownW94
            if (dataSize > 63) RatingAEliwoodNormalW = rom.u16(addr + 62);
            if (dataSize > 65) RatingAEliwoodHardW = rom.u16(addr + 64);
            if (dataSize > 67) RatingAHectorNormalW = rom.u16(addr + 66);
            if (dataSize > 69) RatingAHectorHardW = rom.u16(addr + 68);
            if (dataSize > 71) RatingBEliwoodNormalW = rom.u16(addr + 70);
            if (dataSize > 73) RatingBEliwoodHardW = rom.u16(addr + 72);
            if (dataSize > 75) RatingBHectorNormalW = rom.u16(addr + 74);
            if (dataSize > 77) RatingBHectorHardW = rom.u16(addr + 76);
            if (dataSize > 79) RatingCEliwoodNormalW = rom.u16(addr + 78);
            if (dataSize > 81) RatingCEliwoodHardW = rom.u16(addr + 80);
            if (dataSize > 83) RatingCHectorNormalW = rom.u16(addr + 82);
            if (dataSize > 85) RatingCHectorHardW = rom.u16(addr + 84);
            if (dataSize > 87) RatingDEliwoodNormalW = rom.u16(addr + 86);
            if (dataSize > 89) RatingDEliwoodHardW = rom.u16(addr + 88);
            if (dataSize > 91) RatingDHectorNormalW = rom.u16(addr + 90);
            if (dataSize > 93) RatingDHectorHardW = rom.u16(addr + 92);
            if (dataSize > 95) UnknownW94 = rom.u16(addr + 94);

            // EliwoodNormalPtr-HectorHardPtr
            if (dataSize > 99) EliwoodNormalPtr = rom.u32(addr + 96);
            if (dataSize > 103) EliwoodHardPtr = rom.u32(addr + 100);
            if (dataSize > 107) HectorNormalPtr = rom.u32(addr + 104);
            if (dataSize > 111) HectorHardPtr = rom.u32(addr + 108);

            // ChapterTitleEliwoodTextId-ChapterTitleHectorText2Id
            if (dataSize > 113) ChapterTitleEliwoodTextId = rom.u16(addr + 112);
            if (dataSize > 115) ChapterTitleHectorTextId = rom.u16(addr + 114);
            if (dataSize > 117) ChapterTitleEliwoodText2Id = rom.u16(addr + 116);
            if (dataSize > 119) ChapterTitleHectorText2Id = rom.u16(addr + 118);

            // EventPLIST, WorldMapAutoEvent
            if (dataSize > 120) EventPLIST = rom.u8(addr + 120);
            if (dataSize > 121) WorldMapAutoEvent = rom.u8(addr + 121);

            // FortuneDialogOpeningTextId-FortuneDialogConfirmTextId
            if (dataSize > 123) FortuneDialogOpeningTextId = rom.u16(addr + 122);
            if (dataSize > 125) FortuneDialogEliwoodTextId = rom.u16(addr + 124);
            if (dataSize > 127) FortuneDialogHectorTextId = rom.u16(addr + 126);
            if (dataSize > 129) FortuneDialogConfirmTextId = rom.u16(addr + 128);

            // FortunePortrait-DarkenBeforeStartEvent
            if (dataSize > 130) FortunePortrait = rom.u8(addr + 130);
            if (dataSize > 131) FortuneFee = rom.u8(addr + 131);
            if (dataSize > 132) PrepScreenChNo1 = rom.u8(addr + 132);
            if (dataSize > 133) PrepScreenChNo2 = rom.u8(addr + 133);
            if (dataSize > 134) UnknownB134 = rom.u8(addr + 134);
            if (dataSize > 135) UnknownB135 = rom.u8(addr + 135);
            if (dataSize > 136) UnknownB136 = rom.u8(addr + 136);
            if (dataSize > 137) UnknownB137 = rom.u8(addr + 137);
            if (dataSize > 138) VictoryBGMEnemyCount = rom.u8(addr + 138);
            if (dataSize > 139) DarkenBeforeStartEvent = rom.u8(addr + 139);

            // ClearConditionTextId, DetailClearConditionTextId
            if (dataSize > 141) ClearConditionTextId = rom.u16(addr + 140);
            if (dataSize > 143) DetailClearConditionTextId = rom.u16(addr + 142);

            // SpecialDisplay-UnknownB151
            if (dataSize > 144) SpecialDisplay = rom.u8(addr + 144);
            if (dataSize > 145) TurnCountDisplay = rom.u8(addr + 145);
            if (dataSize > 146) DefenseUnitMark = rom.u8(addr + 146);
            if (dataSize > 147) EscapeMarkerX = rom.u8(addr + 147);
            if (dataSize > 148) EscapeMarkerY = rom.u8(addr + 148);
            if (dataSize > 149) UnknownB149 = rom.u8(addr + 149);
            if (dataSize > 150) UnknownB150 = rom.u8(addr + 150);
            if (dataSize > 151) UnknownB151 = rom.u8(addr + 151);

            // Resolve map name for display
            try
            {
                uint nameTextPos = rom.RomInfo.map_setting_name_text_pos;
                if (nameTextPos > 0 && dataSize > nameTextPos + 1)
                {
                    uint nameId = rom.u16(addr + nameTextPos);
                    Name = NameResolver.GetTextById(nameId);
                }
                else
                {
                    Name = $"Map 0x{addr:X}";
                }
            }
            catch
            {
                Name = "???";
            }

            // Notify text-resolved properties changed
            OnPropertyChanged(nameof(ChapterTitleEliwoodText));
            OnPropertyChanged(nameof(ChapterTitleHectorText));
            OnPropertyChanged(nameof(ChapterTitleEliwoodText2));
            OnPropertyChanged(nameof(ChapterTitleHectorText2));
            OnPropertyChanged(nameof(ClearConditionText));
            OnPropertyChanged(nameof(DetailClearConditionText));

            IsLoaded = true;
        }

        // ---- Write all fields back to ROM ----

        public void WriteMapSetting()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            uint dataSize = DataSize;
            if (dataSize == 0) return;

            Undo undo = CoreState.Undo;
            Undo.UndoData undoData = null;
            if (undo != null)
            {
                undoData = undo.NewUndoData("MapSettingFE7U");
                undoData.list.Add(new Undo.UndoPostion(addr, dataSize));
            }

            // ChapterPointer
            rom.write_u32(addr + 0, ChapterPointer);
            // ObjectTypePLIST
            rom.write_u16(addr + 4, ObjectTypePLIST);

            // PalettePLIST-BattleBG
            rom.write_u8(addr + 6, PalettePLIST);
            rom.write_u8(addr + 7, ChipsetConfigPLIST);
            rom.write_u8(addr + 8, MapPointerPLIST);
            rom.write_u8(addr + 9, TileAnimation1PLIST);
            rom.write_u8(addr + 10, TileAnimation2PLIST);
            rom.write_u8(addr + 11, MapChangePLIST);
            rom.write_u8(addr + 12, FogLevel);
            rom.write_u8(addr + 13, BattlePreparation);
            rom.write_u8(addr + 14, ChapterTitleImage);
            rom.write_u8(addr + 15, ChapterTitleImage2);
            rom.write_u8(addr + 16, Padding16);
            rom.write_u8(addr + 17, Padding17);
            rom.write_u8(addr + 18, Weather);
            rom.write_u8(addr + 19, BattleBG);

            // DifficultyAdjust-PrologueBGMHector
            rom.write_u16(addr + 20, DifficultyAdjust);
            rom.write_u16(addr + 22, PlayerPhaseBGM);
            rom.write_u16(addr + 24, EnemyPhaseBGM);
            rom.write_u16(addr + 26, NpcBGM);
            rom.write_u16(addr + 28, PlayerPhaseBGM2);
            rom.write_u16(addr + 30, EnemyPhaseBGM2);
            rom.write_u16(addr + 32, NpcBGM2);
            rom.write_u16(addr + 34, PlayerPhaseBGMFlag4);
            rom.write_u16(addr + 36, EnemyPhaseBGMFlag4);
            rom.write_u16(addr + 38, PrologueBGMCommon);
            rom.write_u16(addr + 40, PrologueBGMEliwood);
            rom.write_u16(addr + 42, PrologueBGMHector);

            // BreakableWallHP-UnknownB61
            rom.write_u8(addr + 44, BreakableWallHP);
            if (dataSize > 45) rom.write_u8(addr + 45, RatingAEliwoodNormal);
            if (dataSize > 46) rom.write_u8(addr + 46, RatingAEliwoodHard);
            if (dataSize > 47) rom.write_u8(addr + 47, RatingAHectorNormal);
            if (dataSize > 48) rom.write_u8(addr + 48, RatingAHectorHard);
            if (dataSize > 49) rom.write_u8(addr + 49, RatingBEliwoodNormal);
            if (dataSize > 50) rom.write_u8(addr + 50, RatingBEliwoodHard);
            if (dataSize > 51) rom.write_u8(addr + 51, RatingBHectorNormal);
            if (dataSize > 52) rom.write_u8(addr + 52, RatingBHectorHard);
            if (dataSize > 53) rom.write_u8(addr + 53, RatingCEliwoodNormal);
            if (dataSize > 54) rom.write_u8(addr + 54, RatingCEliwoodHard);
            if (dataSize > 55) rom.write_u8(addr + 55, RatingCHectorNormal);
            if (dataSize > 56) rom.write_u8(addr + 56, RatingCHectorHard);
            if (dataSize > 57) rom.write_u8(addr + 57, RatingDEliwoodNormal);
            if (dataSize > 58) rom.write_u8(addr + 58, RatingDEliwoodHard);
            if (dataSize > 59) rom.write_u8(addr + 59, RatingDHectorNormal);
            if (dataSize > 60) rom.write_u8(addr + 60, RatingDHectorHard);
            if (dataSize > 61) rom.write_u8(addr + 61, UnknownB61);

            // RatingAEliwoodNormalW-UnknownW94
            if (dataSize > 63) rom.write_u16(addr + 62, RatingAEliwoodNormalW);
            if (dataSize > 65) rom.write_u16(addr + 64, RatingAEliwoodHardW);
            if (dataSize > 67) rom.write_u16(addr + 66, RatingAHectorNormalW);
            if (dataSize > 69) rom.write_u16(addr + 68, RatingAHectorHardW);
            if (dataSize > 71) rom.write_u16(addr + 70, RatingBEliwoodNormalW);
            if (dataSize > 73) rom.write_u16(addr + 72, RatingBEliwoodHardW);
            if (dataSize > 75) rom.write_u16(addr + 74, RatingBHectorNormalW);
            if (dataSize > 77) rom.write_u16(addr + 76, RatingBHectorHardW);
            if (dataSize > 79) rom.write_u16(addr + 78, RatingCEliwoodNormalW);
            if (dataSize > 81) rom.write_u16(addr + 80, RatingCEliwoodHardW);
            if (dataSize > 83) rom.write_u16(addr + 82, RatingCHectorNormalW);
            if (dataSize > 85) rom.write_u16(addr + 84, RatingCHectorHardW);
            if (dataSize > 87) rom.write_u16(addr + 86, RatingDEliwoodNormalW);
            if (dataSize > 89) rom.write_u16(addr + 88, RatingDEliwoodHardW);
            if (dataSize > 91) rom.write_u16(addr + 90, RatingDHectorNormalW);
            if (dataSize > 93) rom.write_u16(addr + 92, RatingDHectorHardW);
            if (dataSize > 95) rom.write_u16(addr + 94, UnknownW94);

            // EliwoodNormalPtr-HectorHardPtr
            if (dataSize > 99) rom.write_u32(addr + 96, EliwoodNormalPtr);
            if (dataSize > 103) rom.write_u32(addr + 100, EliwoodHardPtr);
            if (dataSize > 107) rom.write_u32(addr + 104, HectorNormalPtr);
            if (dataSize > 111) rom.write_u32(addr + 108, HectorHardPtr);

            // ChapterTitleEliwoodTextId-ChapterTitleHectorText2Id
            if (dataSize > 113) rom.write_u16(addr + 112, ChapterTitleEliwoodTextId);
            if (dataSize > 115) rom.write_u16(addr + 114, ChapterTitleHectorTextId);
            if (dataSize > 117) rom.write_u16(addr + 116, ChapterTitleEliwoodText2Id);
            if (dataSize > 119) rom.write_u16(addr + 118, ChapterTitleHectorText2Id);

            // EventPLIST, WorldMapAutoEvent
            if (dataSize > 120) rom.write_u8(addr + 120, EventPLIST);
            if (dataSize > 121) rom.write_u8(addr + 121, WorldMapAutoEvent);

            // FortuneDialogOpeningTextId-FortuneDialogConfirmTextId
            if (dataSize > 123) rom.write_u16(addr + 122, FortuneDialogOpeningTextId);
            if (dataSize > 125) rom.write_u16(addr + 124, FortuneDialogEliwoodTextId);
            if (dataSize > 127) rom.write_u16(addr + 126, FortuneDialogHectorTextId);
            if (dataSize > 129) rom.write_u16(addr + 128, FortuneDialogConfirmTextId);

            // FortunePortrait-DarkenBeforeStartEvent
            if (dataSize > 130) rom.write_u8(addr + 130, FortunePortrait);
            if (dataSize > 131) rom.write_u8(addr + 131, FortuneFee);
            if (dataSize > 132) rom.write_u8(addr + 132, PrepScreenChNo1);
            if (dataSize > 133) rom.write_u8(addr + 133, PrepScreenChNo2);
            if (dataSize > 134) rom.write_u8(addr + 134, UnknownB134);
            if (dataSize > 135) rom.write_u8(addr + 135, UnknownB135);
            if (dataSize > 136) rom.write_u8(addr + 136, UnknownB136);
            if (dataSize > 137) rom.write_u8(addr + 137, UnknownB137);
            if (dataSize > 138) rom.write_u8(addr + 138, VictoryBGMEnemyCount);
            if (dataSize > 139) rom.write_u8(addr + 139, DarkenBeforeStartEvent);

            // ClearConditionTextId, DetailClearConditionTextId
            if (dataSize > 141) rom.write_u16(addr + 140, ClearConditionTextId);
            if (dataSize > 143) rom.write_u16(addr + 142, DetailClearConditionTextId);

            // SpecialDisplay-UnknownB151
            if (dataSize > 144) rom.write_u8(addr + 144, SpecialDisplay);
            if (dataSize > 145) rom.write_u8(addr + 145, TurnCountDisplay);
            if (dataSize > 146) rom.write_u8(addr + 146, DefenseUnitMark);
            if (dataSize > 147) rom.write_u8(addr + 147, EscapeMarkerX);
            if (dataSize > 148) rom.write_u8(addr + 148, EscapeMarkerY);
            if (dataSize > 149) rom.write_u8(addr + 149, UnknownB149);
            if (dataSize > 150) rom.write_u8(addr + 150, UnknownB150);
            if (dataSize > 151) rom.write_u8(addr + 151, UnknownB151);

            if (undo != null && undoData != null)
            {
                undo.Push(undoData);
            }
        }

        // ---- IDataVerifiable ----

        public int GetListCount() => LoadMapSettingList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var r = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["dataSize"] = $"{DataSize}",
                ["ChapterPointer"] = $"0x{ChapterPointer:X08}",
                ["ObjectTypePLIST"] = $"0x{ObjectTypePLIST:X04}",
                ["PalettePLIST"] = $"0x{PalettePLIST:X02}",
                ["ChipsetConfigPLIST"] = $"0x{ChipsetConfigPLIST:X02}",
                ["MapPointerPLIST"] = $"0x{MapPointerPLIST:X02}",
                ["TileAnimation1PLIST"] = $"0x{TileAnimation1PLIST:X02}",
                ["TileAnimation2PLIST"] = $"0x{TileAnimation2PLIST:X02}",
                ["MapChangePLIST"] = $"0x{MapChangePLIST:X02}",
                ["FogLevel"] = $"0x{FogLevel:X02}",
                ["BattlePreparation"] = $"0x{BattlePreparation:X02}",
                ["ChapterTitleImage"] = $"0x{ChapterTitleImage:X02}",
                ["ChapterTitleImage2"] = $"0x{ChapterTitleImage2:X02}",
                ["Padding16"] = $"0x{Padding16:X02}",
                ["Padding17"] = $"0x{Padding17:X02}",
                ["Weather"] = $"0x{Weather:X02}",
                ["BattleBG"] = $"0x{BattleBG:X02}",
                ["DifficultyAdjust"] = $"0x{DifficultyAdjust:X04}",
                ["PlayerPhaseBGM"] = $"0x{PlayerPhaseBGM:X04}",
                ["EnemyPhaseBGM"] = $"0x{EnemyPhaseBGM:X04}",
                ["NpcBGM"] = $"0x{NpcBGM:X04}",
                ["PlayerPhaseBGM2"] = $"0x{PlayerPhaseBGM2:X04}",
                ["EnemyPhaseBGM2"] = $"0x{EnemyPhaseBGM2:X04}",
                ["NpcBGM2"] = $"0x{NpcBGM2:X04}",
                ["PlayerPhaseBGMFlag4"] = $"0x{PlayerPhaseBGMFlag4:X04}",
                ["EnemyPhaseBGMFlag4"] = $"0x{EnemyPhaseBGMFlag4:X04}",
                ["PrologueBGMCommon"] = $"0x{PrologueBGMCommon:X04}",
                ["PrologueBGMEliwood"] = $"0x{PrologueBGMEliwood:X04}",
                ["PrologueBGMHector"] = $"0x{PrologueBGMHector:X04}",
                ["BreakableWallHP"] = $"0x{BreakableWallHP:X02}",
                ["RatingAEliwoodNormal"] = $"0x{RatingAEliwoodNormal:X02}",
                ["RatingAEliwoodHard"] = $"0x{RatingAEliwoodHard:X02}",
                ["RatingAHectorNormal"] = $"0x{RatingAHectorNormal:X02}",
                ["RatingAHectorHard"] = $"0x{RatingAHectorHard:X02}",
                ["RatingBEliwoodNormal"] = $"0x{RatingBEliwoodNormal:X02}",
                ["RatingBEliwoodHard"] = $"0x{RatingBEliwoodHard:X02}",
                ["RatingBHectorNormal"] = $"0x{RatingBHectorNormal:X02}",
                ["RatingBHectorHard"] = $"0x{RatingBHectorHard:X02}",
                ["RatingCEliwoodNormal"] = $"0x{RatingCEliwoodNormal:X02}",
                ["RatingCEliwoodHard"] = $"0x{RatingCEliwoodHard:X02}",
                ["RatingCHectorNormal"] = $"0x{RatingCHectorNormal:X02}",
                ["RatingCHectorHard"] = $"0x{RatingCHectorHard:X02}",
                ["RatingDEliwoodNormal"] = $"0x{RatingDEliwoodNormal:X02}",
                ["RatingDEliwoodHard"] = $"0x{RatingDEliwoodHard:X02}",
                ["RatingDHectorNormal"] = $"0x{RatingDHectorNormal:X02}",
                ["RatingDHectorHard"] = $"0x{RatingDHectorHard:X02}",
                ["UnknownB61"] = $"0x{UnknownB61:X02}",
                ["RatingAEliwoodNormalW"] = $"0x{RatingAEliwoodNormalW:X04}",
                ["RatingAEliwoodHardW"] = $"0x{RatingAEliwoodHardW:X04}",
                ["RatingAHectorNormalW"] = $"0x{RatingAHectorNormalW:X04}",
                ["RatingAHectorHardW"] = $"0x{RatingAHectorHardW:X04}",
                ["RatingBEliwoodNormalW"] = $"0x{RatingBEliwoodNormalW:X04}",
                ["RatingBEliwoodHardW"] = $"0x{RatingBEliwoodHardW:X04}",
                ["RatingBHectorNormalW"] = $"0x{RatingBHectorNormalW:X04}",
                ["RatingBHectorHardW"] = $"0x{RatingBHectorHardW:X04}",
                ["RatingCEliwoodNormalW"] = $"0x{RatingCEliwoodNormalW:X04}",
                ["RatingCEliwoodHardW"] = $"0x{RatingCEliwoodHardW:X04}",
                ["RatingCHectorNormalW"] = $"0x{RatingCHectorNormalW:X04}",
                ["RatingCHectorHardW"] = $"0x{RatingCHectorHardW:X04}",
                ["RatingDEliwoodNormalW"] = $"0x{RatingDEliwoodNormalW:X04}",
                ["RatingDEliwoodHardW"] = $"0x{RatingDEliwoodHardW:X04}",
                ["RatingDHectorNormalW"] = $"0x{RatingDHectorNormalW:X04}",
                ["RatingDHectorHardW"] = $"0x{RatingDHectorHardW:X04}",
                ["UnknownW94"] = $"0x{UnknownW94:X04}",
                ["EliwoodNormalPtr"] = $"0x{EliwoodNormalPtr:X08}",
                ["EliwoodHardPtr"] = $"0x{EliwoodHardPtr:X08}",
                ["HectorNormalPtr"] = $"0x{HectorNormalPtr:X08}",
                ["HectorHardPtr"] = $"0x{HectorHardPtr:X08}",
                ["ChapterTitleEliwoodTextId"] = $"0x{ChapterTitleEliwoodTextId:X04}",
                ["ChapterTitleHectorTextId"] = $"0x{ChapterTitleHectorTextId:X04}",
                ["ChapterTitleEliwoodText2Id"] = $"0x{ChapterTitleEliwoodText2Id:X04}",
                ["ChapterTitleHectorText2Id"] = $"0x{ChapterTitleHectorText2Id:X04}",
                ["EventPLIST"] = $"0x{EventPLIST:X02}",
                ["WorldMapAutoEvent"] = $"0x{WorldMapAutoEvent:X02}",
                ["FortuneDialogOpeningTextId"] = $"0x{FortuneDialogOpeningTextId:X04}",
                ["FortuneDialogEliwoodTextId"] = $"0x{FortuneDialogEliwoodTextId:X04}",
                ["FortuneDialogHectorTextId"] = $"0x{FortuneDialogHectorTextId:X04}",
                ["FortuneDialogConfirmTextId"] = $"0x{FortuneDialogConfirmTextId:X04}",
                ["FortunePortrait"] = $"0x{FortunePortrait:X02}",
                ["FortuneFee"] = $"0x{FortuneFee:X02}",
                ["PrepScreenChNo1"] = $"0x{PrepScreenChNo1:X02}",
                ["PrepScreenChNo2"] = $"0x{PrepScreenChNo2:X02}",
                ["UnknownB134"] = $"0x{UnknownB134:X02}",
                ["UnknownB135"] = $"0x{UnknownB135:X02}",
                ["UnknownB136"] = $"0x{UnknownB136:X02}",
                ["UnknownB137"] = $"0x{UnknownB137:X02}",
                ["VictoryBGMEnemyCount"] = $"0x{VictoryBGMEnemyCount:X02}",
                ["DarkenBeforeStartEvent"] = $"0x{DarkenBeforeStartEvent:X02}",
                ["ClearConditionTextId"] = $"0x{ClearConditionTextId:X04}",
                ["DetailClearConditionTextId"] = $"0x{DetailClearConditionTextId:X04}",
                ["SpecialDisplay"] = $"0x{SpecialDisplay:X02}",
                ["TurnCountDisplay"] = $"0x{TurnCountDisplay:X02}",
                ["DefenseUnitMark"] = $"0x{DefenseUnitMark:X02}",
                ["EscapeMarkerX"] = $"0x{EscapeMarkerX:X02}",
                ["EscapeMarkerY"] = $"0x{EscapeMarkerY:X02}",
                ["UnknownB149"] = $"0x{UnknownB149:X02}",
                ["UnknownB150"] = $"0x{UnknownB150:X02}",
                ["UnknownB151"] = $"0x{UnknownB151:X02}",
            };
            return r;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            uint ds = DataSize;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["dataSize"] = $"{ds}",
                // ChapterPointer
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                // ObjectTypePLIST
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                // PalettePLIST-BattleBG
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
                // DifficultyAdjust-PrologueBGMHector
                ["u16@0x14"] = $"0x{rom.u16(a + 20):X04}",
                ["u16@0x16"] = $"0x{rom.u16(a + 22):X04}",
                ["u16@0x18"] = $"0x{rom.u16(a + 24):X04}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u16@0x1C"] = $"0x{rom.u16(a + 28):X04}",
                ["u16@0x1E"] = $"0x{rom.u16(a + 30):X04}",
                ["u16@0x20"] = $"0x{rom.u16(a + 32):X04}",
                ["u16@0x22"] = $"0x{rom.u16(a + 34):X04}",
                ["u16@0x24"] = $"0x{rom.u16(a + 36):X04}",
                ["u16@0x26"] = $"0x{rom.u16(a + 38):X04}",
                ["u16@0x28"] = $"0x{rom.u16(a + 40):X04}",
                ["u16@0x2A"] = $"0x{rom.u16(a + 42):X04}",
                // BreakableWallHP
                ["u8@0x2C"] = $"0x{rom.u8(a + 44):X02}",
            };
            // RatingAEliwoodNormal-UnknownB61 (conditional on dataSize)
            if (ds > 45) report["u8@0x2D"] = $"0x{rom.u8(a + 45):X02}";
            if (ds > 46) report["u8@0x2E"] = $"0x{rom.u8(a + 46):X02}";
            if (ds > 47) report["u8@0x2F"] = $"0x{rom.u8(a + 47):X02}";
            if (ds > 48) report["u8@0x30"] = $"0x{rom.u8(a + 48):X02}";
            if (ds > 49) report["u8@0x31"] = $"0x{rom.u8(a + 49):X02}";
            if (ds > 50) report["u8@0x32"] = $"0x{rom.u8(a + 50):X02}";
            if (ds > 51) report["u8@0x33"] = $"0x{rom.u8(a + 51):X02}";
            if (ds > 52) report["u8@0x34"] = $"0x{rom.u8(a + 52):X02}";
            if (ds > 53) report["u8@0x35"] = $"0x{rom.u8(a + 53):X02}";
            if (ds > 54) report["u8@0x36"] = $"0x{rom.u8(a + 54):X02}";
            if (ds > 55) report["u8@0x37"] = $"0x{rom.u8(a + 55):X02}";
            if (ds > 56) report["u8@0x38"] = $"0x{rom.u8(a + 56):X02}";
            if (ds > 57) report["u8@0x39"] = $"0x{rom.u8(a + 57):X02}";
            if (ds > 58) report["u8@0x3A"] = $"0x{rom.u8(a + 58):X02}";
            if (ds > 59) report["u8@0x3B"] = $"0x{rom.u8(a + 59):X02}";
            if (ds > 60) report["u8@0x3C"] = $"0x{rom.u8(a + 60):X02}";
            if (ds > 61) report["u8@0x3D"] = $"0x{rom.u8(a + 61):X02}";
            // RatingAEliwoodNormalW-UnknownW94
            if (ds > 63) report["u16@0x3E"] = $"0x{rom.u16(a + 62):X04}";
            if (ds > 65) report["u16@0x40"] = $"0x{rom.u16(a + 64):X04}";
            if (ds > 67) report["u16@0x42"] = $"0x{rom.u16(a + 66):X04}";
            if (ds > 69) report["u16@0x44"] = $"0x{rom.u16(a + 68):X04}";
            if (ds > 71) report["u16@0x46"] = $"0x{rom.u16(a + 70):X04}";
            if (ds > 73) report["u16@0x48"] = $"0x{rom.u16(a + 72):X04}";
            if (ds > 75) report["u16@0x4A"] = $"0x{rom.u16(a + 74):X04}";
            if (ds > 77) report["u16@0x4C"] = $"0x{rom.u16(a + 76):X04}";
            if (ds > 79) report["u16@0x4E"] = $"0x{rom.u16(a + 78):X04}";
            if (ds > 81) report["u16@0x50"] = $"0x{rom.u16(a + 80):X04}";
            if (ds > 83) report["u16@0x52"] = $"0x{rom.u16(a + 82):X04}";
            if (ds > 85) report["u16@0x54"] = $"0x{rom.u16(a + 84):X04}";
            if (ds > 87) report["u16@0x56"] = $"0x{rom.u16(a + 86):X04}";
            if (ds > 89) report["u16@0x58"] = $"0x{rom.u16(a + 88):X04}";
            if (ds > 91) report["u16@0x5A"] = $"0x{rom.u16(a + 90):X04}";
            if (ds > 93) report["u16@0x5C"] = $"0x{rom.u16(a + 92):X04}";
            if (ds > 95) report["u16@0x5E"] = $"0x{rom.u16(a + 94):X04}";
            // EliwoodNormalPtr-HectorHardPtr
            if (ds > 99) report["u32@0x60"] = $"0x{rom.u32(a + 96):X08}";
            if (ds > 103) report["u32@0x64"] = $"0x{rom.u32(a + 100):X08}";
            if (ds > 107) report["u32@0x68"] = $"0x{rom.u32(a + 104):X08}";
            if (ds > 111) report["u32@0x6C"] = $"0x{rom.u32(a + 108):X08}";
            // ChapterTitleEliwoodTextId-ChapterTitleHectorText2Id
            if (ds > 113) report["u16@0x70"] = $"0x{rom.u16(a + 112):X04}";
            if (ds > 115) report["u16@0x72"] = $"0x{rom.u16(a + 114):X04}";
            if (ds > 117) report["u16@0x74"] = $"0x{rom.u16(a + 116):X04}";
            if (ds > 119) report["u16@0x76"] = $"0x{rom.u16(a + 118):X04}";
            // EventPLIST, WorldMapAutoEvent
            if (ds > 120) report["u8@0x78"] = $"0x{rom.u8(a + 120):X02}";
            if (ds > 121) report["u8@0x79"] = $"0x{rom.u8(a + 121):X02}";
            // FortuneDialogOpeningTextId-FortuneDialogConfirmTextId
            if (ds > 123) report["u16@0x7A"] = $"0x{rom.u16(a + 122):X04}";
            if (ds > 125) report["u16@0x7C"] = $"0x{rom.u16(a + 124):X04}";
            if (ds > 127) report["u16@0x7E"] = $"0x{rom.u16(a + 126):X04}";
            if (ds > 129) report["u16@0x80"] = $"0x{rom.u16(a + 128):X04}";
            // FortunePortrait-DarkenBeforeStartEvent
            if (ds > 130) report["u8@0x82"] = $"0x{rom.u8(a + 130):X02}";
            if (ds > 131) report["u8@0x83"] = $"0x{rom.u8(a + 131):X02}";
            if (ds > 132) report["u8@0x84"] = $"0x{rom.u8(a + 132):X02}";
            if (ds > 133) report["u8@0x85"] = $"0x{rom.u8(a + 133):X02}";
            if (ds > 134) report["u8@0x86"] = $"0x{rom.u8(a + 134):X02}";
            if (ds > 135) report["u8@0x87"] = $"0x{rom.u8(a + 135):X02}";
            if (ds > 136) report["u8@0x88"] = $"0x{rom.u8(a + 136):X02}";
            if (ds > 137) report["u8@0x89"] = $"0x{rom.u8(a + 137):X02}";
            if (ds > 138) report["u8@0x8A"] = $"0x{rom.u8(a + 138):X02}";
            if (ds > 139) report["u8@0x8B"] = $"0x{rom.u8(a + 139):X02}";
            // ClearConditionTextId, DetailClearConditionTextId
            if (ds > 141) report["u16@0x8C"] = $"0x{rom.u16(a + 140):X04}";
            if (ds > 143) report["u16@0x8E"] = $"0x{rom.u16(a + 142):X04}";
            // SpecialDisplay-UnknownB151
            if (ds > 144) report["u8@0x90"] = $"0x{rom.u8(a + 144):X02}";
            if (ds > 145) report["u8@0x91"] = $"0x{rom.u8(a + 145):X02}";
            if (ds > 146) report["u8@0x92"] = $"0x{rom.u8(a + 146):X02}";
            if (ds > 147) report["u8@0x93"] = $"0x{rom.u8(a + 147):X02}";
            if (ds > 148) report["u8@0x94"] = $"0x{rom.u8(a + 148):X02}";
            if (ds > 149) report["u8@0x95"] = $"0x{rom.u8(a + 149):X02}";
            if (ds > 150) report["u8@0x96"] = $"0x{rom.u8(a + 150):X02}";
            if (ds > 151) report["u8@0x97"] = $"0x{rom.u8(a + 151):X02}";
            return report;
        }

        // Backward-compatible property aliases (PalettePLIST and Weather are now primary names)
        public uint TilesetPLIST => ObjectTypePLIST;
        public uint MapPLIST => MapPointerPLIST;
        public uint ChapterNameId => ChapterTitleEliwoodTextId;
        public uint ObjType => BattlePreparation;
    }
}
