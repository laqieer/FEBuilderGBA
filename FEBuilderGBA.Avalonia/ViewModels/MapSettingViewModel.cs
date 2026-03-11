using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        bool _isLoaded;
        uint _dataSize;

        // --- Backing fields ---
        uint _cpPointer;
        uint _objectTypePLIST;
        uint _palettePLIST, _chipsetConfigPLIST, _mapPointerPLIST;
        uint _tileAnimation1PLIST, _tileAnimation2PLIST, _mapChangePLIST;
        uint _fogLevel, _battlePreparation, _chapterTitleImage, _chapterTitleImage2;
        uint _initialX, _initialY, _weather, _battleBGLookup;
        uint _difficultyAdjustment;
        uint _playerPhaseBGM, _enemyPhaseBGM, _npcPhaseBGM;
        uint _playerPhaseBGM2, _enemyPhaseBGM2, _npcPhaseBGM2;
        uint _playerPhaseBGMFlag4, _enemyPhaseBGMFlag4;
        uint _unknownW38, _unknownW40, _unknownW42;
        uint _breakableWallHP;
        uint _ratingAEliwoodNormal, _ratingAEliwoodHard, _ratingAHectorNormal, _ratingAHectorHard;
        uint _ratingBEliwoodNormal, _ratingBEliwoodHard, _ratingBHectorNormal, _ratingBHectorHard;
        uint _ratingCEliwoodNormal, _ratingCEliwoodHard, _ratingCHectorNormal, _ratingCHectorHard;
        uint _ratingDEliwoodNormal, _ratingDEliwoodHard, _ratingDHectorNormal, _ratingDHectorHard;
        uint _unknownB61;
        uint _ratingAEliwoodNormalW, _ratingAEliwoodHardW, _ratingAHectorNormalW, _ratingAHectorHardW;
        uint _ratingBEliwoodNormalW, _ratingBEliwoodHardW, _ratingBHectorNormalW, _ratingBHectorHardW;
        uint _ratingCEliwoodNormalW, _ratingCEliwoodHardW, _ratingCHectorNormalW, _ratingCHectorHardW;
        uint _ratingDEliwoodNormalW, _ratingDEliwoodHardW, _ratingDHectorNormalW, _ratingDHectorHardW;
        uint _unknownW94;
        uint _diffPtrEliwoodNormal, _diffPtrEliwoodHard, _diffPtrHectorNormal, _diffPtrHectorHard;
        uint _mapNameText1, _mapNameText2;
        uint _eventIdPLIST, _worldMapAutoEvent;
        uint _unknownB118, _unknownB119, _unknownB120, _unknownB121;
        uint _unknownB122, _unknownB123, _unknownB124, _unknownB125;
        uint _unknownB126, _unknownB127;
        uint _chapterNumber;
        uint _unknownB129, _unknownB130, _unknownB131, _unknownB132, _unknownB133;
        uint _victoryBGMEnemyCount, _blackoutBeforeStart;
        uint _clearConditionText, _detailClearConditionText;
        uint _specialDisplay, _turnCountDisplay, _defenseUnitMark;
        uint _escapeMarkerX, _escapeMarkerY;
        uint _unknownB145, _unknownB146, _unknownB147;

        // ---- Properties ----

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }

        // D0: CP / CString pointer
        public uint CpPointer { get => _cpPointer; set => SetField(ref _cpPointer, value); }

        // W4: Object type PLIST
        public uint ObjectTypePLIST { get => _objectTypePLIST; set => SetField(ref _objectTypePLIST, value); }

        // B6-B11: Map style PLISTs
        public uint PalettePLIST { get => _palettePLIST; set => SetField(ref _palettePLIST, value); }
        public uint ChipsetConfigPLIST { get => _chipsetConfigPLIST; set => SetField(ref _chipsetConfigPLIST, value); }
        public uint MapPointerPLIST { get => _mapPointerPLIST; set => SetField(ref _mapPointerPLIST, value); }
        public uint TileAnimation1PLIST { get => _tileAnimation1PLIST; set => SetField(ref _tileAnimation1PLIST, value); }
        public uint TileAnimation2PLIST { get => _tileAnimation2PLIST; set => SetField(ref _tileAnimation2PLIST, value); }
        public uint MapChangePLIST { get => _mapChangePLIST; set => SetField(ref _mapChangePLIST, value); }

        // B12-B19: Map properties
        public uint FogLevel { get => _fogLevel; set => SetField(ref _fogLevel, value); }
        public uint BattlePreparation { get => _battlePreparation; set => SetField(ref _battlePreparation, value); }
        public uint ChapterTitleImage { get => _chapterTitleImage; set => SetField(ref _chapterTitleImage, value); }
        public uint ChapterTitleImage2 { get => _chapterTitleImage2; set => SetField(ref _chapterTitleImage2, value); }
        public uint InitialX { get => _initialX; set => SetField(ref _initialX, value); }
        public uint InitialY { get => _initialY; set => SetField(ref _initialY, value); }
        public uint Weather { get => _weather; set => SetField(ref _weather, value); }
        public uint BattleBGLookup { get => _battleBGLookup; set => SetField(ref _battleBGLookup, value); }

        // W20: Difficulty adjustment
        public uint DifficultyAdjustment { get => _difficultyAdjustment; set => SetField(ref _difficultyAdjustment, value); }

        // W22-W36: BGM settings
        public uint PlayerPhaseBGM { get => _playerPhaseBGM; set => SetField(ref _playerPhaseBGM, value); }
        public uint EnemyPhaseBGM { get => _enemyPhaseBGM; set => SetField(ref _enemyPhaseBGM, value); }
        public uint NpcPhaseBGM { get => _npcPhaseBGM; set => SetField(ref _npcPhaseBGM, value); }
        public uint PlayerPhaseBGM2 { get => _playerPhaseBGM2; set => SetField(ref _playerPhaseBGM2, value); }
        public uint EnemyPhaseBGM2 { get => _enemyPhaseBGM2; set => SetField(ref _enemyPhaseBGM2, value); }
        public uint NpcPhaseBGM2 { get => _npcPhaseBGM2; set => SetField(ref _npcPhaseBGM2, value); }
        public uint PlayerPhaseBGMFlag4 { get => _playerPhaseBGMFlag4; set => SetField(ref _playerPhaseBGMFlag4, value); }
        public uint EnemyPhaseBGMFlag4 { get => _enemyPhaseBGMFlag4; set => SetField(ref _enemyPhaseBGMFlag4, value); }
        public uint UnknownW38 { get => _unknownW38; set => SetField(ref _unknownW38, value); }
        public uint UnknownW40 { get => _unknownW40; set => SetField(ref _unknownW40, value); }
        public uint UnknownW42 { get => _unknownW42; set => SetField(ref _unknownW42, value); }

        // B44: Breakable wall HP
        public uint BreakableWallHP { get => _breakableWallHP; set => SetField(ref _breakableWallHP, value); }

        // B45-B60: Difficulty rating bytes (A/B/C/D x Eliwood/Hector x Normal/Hard)
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

        // W62-W94: Difficulty rating words
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

        // D96-D108: Difficulty pointers
        public uint DiffPtrEliwoodNormal { get => _diffPtrEliwoodNormal; set => SetField(ref _diffPtrEliwoodNormal, value); }
        public uint DiffPtrEliwoodHard { get => _diffPtrEliwoodHard; set => SetField(ref _diffPtrEliwoodHard, value); }
        public uint DiffPtrHectorNormal { get => _diffPtrHectorNormal; set => SetField(ref _diffPtrHectorNormal, value); }
        public uint DiffPtrHectorHard { get => _diffPtrHectorHard; set => SetField(ref _diffPtrHectorHard, value); }

        // W112-W114: Map name text IDs
        public uint MapNameText1 { get => _mapNameText1; set => SetField(ref _mapNameText1, value); }
        public uint MapNameText2 { get => _mapNameText2; set => SetField(ref _mapNameText2, value); }

        // B116-B135: Event / World Map
        public uint EventIdPLIST { get => _eventIdPLIST; set => SetField(ref _eventIdPLIST, value); }
        public uint WorldMapAutoEvent { get => _worldMapAutoEvent; set => SetField(ref _worldMapAutoEvent, value); }
        public uint UnknownB118 { get => _unknownB118; set => SetField(ref _unknownB118, value); }
        public uint UnknownB119 { get => _unknownB119; set => SetField(ref _unknownB119, value); }
        public uint UnknownB120 { get => _unknownB120; set => SetField(ref _unknownB120, value); }
        public uint UnknownB121 { get => _unknownB121; set => SetField(ref _unknownB121, value); }
        public uint UnknownB122 { get => _unknownB122; set => SetField(ref _unknownB122, value); }
        public uint UnknownB123 { get => _unknownB123; set => SetField(ref _unknownB123, value); }
        public uint UnknownB124 { get => _unknownB124; set => SetField(ref _unknownB124, value); }
        public uint UnknownB125 { get => _unknownB125; set => SetField(ref _unknownB125, value); }
        public uint UnknownB126 { get => _unknownB126; set => SetField(ref _unknownB126, value); }
        public uint UnknownB127 { get => _unknownB127; set => SetField(ref _unknownB127, value); }
        public uint ChapterNumber { get => _chapterNumber; set => SetField(ref _chapterNumber, value); }
        public uint UnknownB129 { get => _unknownB129; set => SetField(ref _unknownB129, value); }
        public uint UnknownB130 { get => _unknownB130; set => SetField(ref _unknownB130, value); }
        public uint UnknownB131 { get => _unknownB131; set => SetField(ref _unknownB131, value); }
        public uint UnknownB132 { get => _unknownB132; set => SetField(ref _unknownB132, value); }
        public uint UnknownB133 { get => _unknownB133; set => SetField(ref _unknownB133, value); }
        public uint VictoryBGMEnemyCount { get => _victoryBGMEnemyCount; set => SetField(ref _victoryBGMEnemyCount, value); }
        public uint BlackoutBeforeStart { get => _blackoutBeforeStart; set => SetField(ref _blackoutBeforeStart, value); }

        // W136-W138: Clear condition text IDs
        public uint ClearConditionText { get => _clearConditionText; set => SetField(ref _clearConditionText, value); }
        public uint DetailClearConditionText { get => _detailClearConditionText; set => SetField(ref _detailClearConditionText, value); }

        // B140-B147: Display flags
        public uint SpecialDisplay { get => _specialDisplay; set => SetField(ref _specialDisplay, value); }
        public uint TurnCountDisplay { get => _turnCountDisplay; set => SetField(ref _turnCountDisplay, value); }
        public uint DefenseUnitMark { get => _defenseUnitMark; set => SetField(ref _defenseUnitMark, value); }
        public uint EscapeMarkerX { get => _escapeMarkerX; set => SetField(ref _escapeMarkerX, value); }
        public uint EscapeMarkerY { get => _escapeMarkerY; set => SetField(ref _escapeMarkerY, value); }
        public uint UnknownB145 { get => _unknownB145; set => SetField(ref _unknownB145, value); }
        public uint UnknownB146 { get => _unknownB146; set => SetField(ref _unknownB146, value); }
        public uint UnknownB147 { get => _unknownB147; set => SetField(ref _unknownB147, value); }

        // ---- Helpers for label text resolution ----

        public string MapNameText1Resolved => ResolveText(MapNameText1);
        public string MapNameText2Resolved => ResolveText(MapNameText2);
        public string ClearConditionTextResolved => ResolveText(ClearConditionText);
        public string DetailClearConditionTextResolved => ResolveText(DetailClearConditionText);

        public string PlayerPhaseBGMResolved => NameResolver.GetSongName(PlayerPhaseBGM);
        public string EnemyPhaseBGMResolved => NameResolver.GetSongName(EnemyPhaseBGM);
        public string NpcPhaseBGMResolved => NameResolver.GetSongName(NpcPhaseBGM);
        public string PlayerPhaseBGM2Resolved => NameResolver.GetSongName(PlayerPhaseBGM2);
        public string EnemyPhaseBGM2Resolved => NameResolver.GetSongName(EnemyPhaseBGM2);
        public string NpcPhaseBGM2Resolved => NameResolver.GetSongName(NpcPhaseBGM2);

        static string ResolveText(uint id)
        {
            if (id == 0) return "(none)";
            try { return FETextDecode.Direct(id); }
            catch { return "???"; }
        }

        // ---- List ----

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

        public void LoadMapSetting(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) dataSize = 148;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataSize = dataSize;

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
            ChapterTitleImage2 = rom.u8(addr + 15);
            InitialX = rom.u8(addr + 16);
            InitialY = rom.u8(addr + 17);
            Weather = rom.u8(addr + 18);
            BattleBGLookup = rom.u8(addr + 19);

            DifficultyAdjustment = rom.u16(addr + 20);
            PlayerPhaseBGM = rom.u16(addr + 22);
            EnemyPhaseBGM = rom.u16(addr + 24);
            NpcPhaseBGM = rom.u16(addr + 26);
            PlayerPhaseBGM2 = rom.u16(addr + 28);
            EnemyPhaseBGM2 = rom.u16(addr + 30);
            NpcPhaseBGM2 = rom.u16(addr + 32);
            PlayerPhaseBGMFlag4 = rom.u16(addr + 34);
            EnemyPhaseBGMFlag4 = rom.u16(addr + 36);
            UnknownW38 = rom.u16(addr + 38);
            UnknownW40 = rom.u16(addr + 40);
            UnknownW42 = rom.u16(addr + 42);

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

            if (dataSize > 99) DiffPtrEliwoodNormal = rom.u32(addr + 96);
            if (dataSize > 103) DiffPtrEliwoodHard = rom.u32(addr + 100);
            if (dataSize > 107) DiffPtrHectorNormal = rom.u32(addr + 104);
            if (dataSize > 111) DiffPtrHectorHard = rom.u32(addr + 108);

            if (dataSize > 113) MapNameText1 = rom.u16(addr + 112);
            if (dataSize > 115) MapNameText2 = rom.u16(addr + 114);

            if (dataSize > 116) EventIdPLIST = rom.u8(addr + 116);
            if (dataSize > 117) WorldMapAutoEvent = rom.u8(addr + 117);
            if (dataSize > 118) UnknownB118 = rom.u8(addr + 118);
            if (dataSize > 119) UnknownB119 = rom.u8(addr + 119);
            if (dataSize > 120) UnknownB120 = rom.u8(addr + 120);
            if (dataSize > 121) UnknownB121 = rom.u8(addr + 121);
            if (dataSize > 122) UnknownB122 = rom.u8(addr + 122);
            if (dataSize > 123) UnknownB123 = rom.u8(addr + 123);
            if (dataSize > 124) UnknownB124 = rom.u8(addr + 124);
            if (dataSize > 125) UnknownB125 = rom.u8(addr + 125);
            if (dataSize > 126) UnknownB126 = rom.u8(addr + 126);
            if (dataSize > 127) UnknownB127 = rom.u8(addr + 127);
            if (dataSize > 128) ChapterNumber = rom.u8(addr + 128);
            if (dataSize > 129) UnknownB129 = rom.u8(addr + 129);
            if (dataSize > 130) UnknownB130 = rom.u8(addr + 130);
            if (dataSize > 131) UnknownB131 = rom.u8(addr + 131);
            if (dataSize > 132) UnknownB132 = rom.u8(addr + 132);
            if (dataSize > 133) UnknownB133 = rom.u8(addr + 133);
            if (dataSize > 134) VictoryBGMEnemyCount = rom.u8(addr + 134);
            if (dataSize > 135) BlackoutBeforeStart = rom.u8(addr + 135);

            if (dataSize > 137) ClearConditionText = rom.u16(addr + 136);
            if (dataSize > 139) DetailClearConditionText = rom.u16(addr + 138);

            if (dataSize > 140) SpecialDisplay = rom.u8(addr + 140);
            if (dataSize > 141) TurnCountDisplay = rom.u8(addr + 141);
            if (dataSize > 142) DefenseUnitMark = rom.u8(addr + 142);
            if (dataSize > 143) EscapeMarkerX = rom.u8(addr + 143);
            if (dataSize > 144) EscapeMarkerY = rom.u8(addr + 144);
            if (dataSize > 145) UnknownB145 = rom.u8(addr + 145);
            if (dataSize > 146) UnknownB146 = rom.u8(addr + 146);
            if (dataSize > 147) UnknownB147 = rom.u8(addr + 147);

            // Resolve map name for display
            try
            {
                uint nameTextPos = rom.RomInfo.map_setting_name_text_pos;
                if (nameTextPos > 0 && dataSize > nameTextPos + 1)
                {
                    uint nameId = rom.u16(addr + nameTextPos);
                    Name = FETextDecode.Direct(nameId);
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

            OnPropertyChanged(nameof(MapNameText1Resolved));
            OnPropertyChanged(nameof(MapNameText2Resolved));
            OnPropertyChanged(nameof(ClearConditionTextResolved));
            OnPropertyChanged(nameof(DetailClearConditionTextResolved));
            OnPropertyChanged(nameof(PlayerPhaseBGMResolved));
            OnPropertyChanged(nameof(EnemyPhaseBGMResolved));
            OnPropertyChanged(nameof(NpcPhaseBGMResolved));
            OnPropertyChanged(nameof(PlayerPhaseBGM2Resolved));
            OnPropertyChanged(nameof(EnemyPhaseBGM2Resolved));
            OnPropertyChanged(nameof(NpcPhaseBGM2Resolved));

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
                undoData = undo.NewUndoData("MapSetting");
                undoData.list.Add(new Undo.UndoPostion(addr, dataSize));
            }

            rom.write_u32(addr + 0, CpPointer);
            rom.write_u16(addr + 4, ObjectTypePLIST);

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
            rom.write_u8(addr + 16, InitialX);
            rom.write_u8(addr + 17, InitialY);
            rom.write_u8(addr + 18, Weather);
            rom.write_u8(addr + 19, BattleBGLookup);

            rom.write_u16(addr + 20, DifficultyAdjustment);
            rom.write_u16(addr + 22, PlayerPhaseBGM);
            rom.write_u16(addr + 24, EnemyPhaseBGM);
            rom.write_u16(addr + 26, NpcPhaseBGM);
            rom.write_u16(addr + 28, PlayerPhaseBGM2);
            rom.write_u16(addr + 30, EnemyPhaseBGM2);
            rom.write_u16(addr + 32, NpcPhaseBGM2);
            rom.write_u16(addr + 34, PlayerPhaseBGMFlag4);
            rom.write_u16(addr + 36, EnemyPhaseBGMFlag4);
            rom.write_u16(addr + 38, UnknownW38);
            rom.write_u16(addr + 40, UnknownW40);
            rom.write_u16(addr + 42, UnknownW42);

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

            if (dataSize > 99) rom.write_u32(addr + 96, DiffPtrEliwoodNormal);
            if (dataSize > 103) rom.write_u32(addr + 100, DiffPtrEliwoodHard);
            if (dataSize > 107) rom.write_u32(addr + 104, DiffPtrHectorNormal);
            if (dataSize > 111) rom.write_u32(addr + 108, DiffPtrHectorHard);

            if (dataSize > 113) rom.write_u16(addr + 112, MapNameText1);
            if (dataSize > 115) rom.write_u16(addr + 114, MapNameText2);

            if (dataSize > 116) rom.write_u8(addr + 116, EventIdPLIST);
            if (dataSize > 117) rom.write_u8(addr + 117, WorldMapAutoEvent);
            if (dataSize > 118) rom.write_u8(addr + 118, UnknownB118);
            if (dataSize > 119) rom.write_u8(addr + 119, UnknownB119);
            if (dataSize > 120) rom.write_u8(addr + 120, UnknownB120);
            if (dataSize > 121) rom.write_u8(addr + 121, UnknownB121);
            if (dataSize > 122) rom.write_u8(addr + 122, UnknownB122);
            if (dataSize > 123) rom.write_u8(addr + 123, UnknownB123);
            if (dataSize > 124) rom.write_u8(addr + 124, UnknownB124);
            if (dataSize > 125) rom.write_u8(addr + 125, UnknownB125);
            if (dataSize > 126) rom.write_u8(addr + 126, UnknownB126);
            if (dataSize > 127) rom.write_u8(addr + 127, UnknownB127);
            if (dataSize > 128) rom.write_u8(addr + 128, ChapterNumber);
            if (dataSize > 129) rom.write_u8(addr + 129, UnknownB129);
            if (dataSize > 130) rom.write_u8(addr + 130, UnknownB130);
            if (dataSize > 131) rom.write_u8(addr + 131, UnknownB131);
            if (dataSize > 132) rom.write_u8(addr + 132, UnknownB132);
            if (dataSize > 133) rom.write_u8(addr + 133, UnknownB133);
            if (dataSize > 134) rom.write_u8(addr + 134, VictoryBGMEnemyCount);
            if (dataSize > 135) rom.write_u8(addr + 135, BlackoutBeforeStart);

            if (dataSize > 137) rom.write_u16(addr + 136, ClearConditionText);
            if (dataSize > 139) rom.write_u16(addr + 138, DetailClearConditionText);

            if (dataSize > 140) rom.write_u8(addr + 140, SpecialDisplay);
            if (dataSize > 141) rom.write_u8(addr + 141, TurnCountDisplay);
            if (dataSize > 142) rom.write_u8(addr + 142, DefenseUnitMark);
            if (dataSize > 143) rom.write_u8(addr + 143, EscapeMarkerX);
            if (dataSize > 144) rom.write_u8(addr + 144, EscapeMarkerY);
            if (dataSize > 145) rom.write_u8(addr + 145, UnknownB145);
            if (dataSize > 146) rom.write_u8(addr + 146, UnknownB146);
            if (dataSize > 147) rom.write_u8(addr + 147, UnknownB147);

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
                ["CpPointer"] = $"0x{CpPointer:X08}",
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
                ["InitialX"] = $"0x{InitialX:X02}",
                ["InitialY"] = $"0x{InitialY:X02}",
                ["Weather"] = $"0x{Weather:X02}",
                ["BattleBGLookup"] = $"0x{BattleBGLookup:X02}",
                ["DifficultyAdjustment"] = $"0x{DifficultyAdjustment:X04}",
                ["PlayerPhaseBGM"] = $"0x{PlayerPhaseBGM:X04}",
                ["EnemyPhaseBGM"] = $"0x{EnemyPhaseBGM:X04}",
                ["NpcPhaseBGM"] = $"0x{NpcPhaseBGM:X04}",
                ["PlayerPhaseBGM2"] = $"0x{PlayerPhaseBGM2:X04}",
                ["EnemyPhaseBGM2"] = $"0x{EnemyPhaseBGM2:X04}",
                ["NpcPhaseBGM2"] = $"0x{NpcPhaseBGM2:X04}",
                ["PlayerPhaseBGMFlag4"] = $"0x{PlayerPhaseBGMFlag4:X04}",
                ["EnemyPhaseBGMFlag4"] = $"0x{EnemyPhaseBGMFlag4:X04}",
                ["UnknownW38"] = $"0x{UnknownW38:X04}",
                ["UnknownW40"] = $"0x{UnknownW40:X04}",
                ["UnknownW42"] = $"0x{UnknownW42:X04}",
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
                ["DiffPtrEliwoodNormal"] = $"0x{DiffPtrEliwoodNormal:X08}",
                ["DiffPtrEliwoodHard"] = $"0x{DiffPtrEliwoodHard:X08}",
                ["DiffPtrHectorNormal"] = $"0x{DiffPtrHectorNormal:X08}",
                ["DiffPtrHectorHard"] = $"0x{DiffPtrHectorHard:X08}",
                ["MapNameText1"] = $"0x{MapNameText1:X04}",
                ["MapNameText2"] = $"0x{MapNameText2:X04}",
                ["EventIdPLIST"] = $"0x{EventIdPLIST:X02}",
                ["WorldMapAutoEvent"] = $"0x{WorldMapAutoEvent:X02}",
                ["UnknownB118"] = $"0x{UnknownB118:X02}",
                ["UnknownB119"] = $"0x{UnknownB119:X02}",
                ["UnknownB120"] = $"0x{UnknownB120:X02}",
                ["UnknownB121"] = $"0x{UnknownB121:X02}",
                ["UnknownB122"] = $"0x{UnknownB122:X02}",
                ["UnknownB123"] = $"0x{UnknownB123:X02}",
                ["UnknownB124"] = $"0x{UnknownB124:X02}",
                ["UnknownB125"] = $"0x{UnknownB125:X02}",
                ["UnknownB126"] = $"0x{UnknownB126:X02}",
                ["UnknownB127"] = $"0x{UnknownB127:X02}",
                ["ChapterNumber"] = $"0x{ChapterNumber:X02}",
                ["UnknownB129"] = $"0x{UnknownB129:X02}",
                ["UnknownB130"] = $"0x{UnknownB130:X02}",
                ["UnknownB131"] = $"0x{UnknownB131:X02}",
                ["UnknownB132"] = $"0x{UnknownB132:X02}",
                ["UnknownB133"] = $"0x{UnknownB133:X02}",
                ["VictoryBGMEnemyCount"] = $"0x{VictoryBGMEnemyCount:X02}",
                ["BlackoutBeforeStart"] = $"0x{BlackoutBeforeStart:X02}",
                ["ClearConditionText"] = $"0x{ClearConditionText:X04}",
                ["DetailClearConditionText"] = $"0x{DetailClearConditionText:X04}",
                ["SpecialDisplay"] = $"0x{SpecialDisplay:X02}",
                ["TurnCountDisplay"] = $"0x{TurnCountDisplay:X02}",
                ["DefenseUnitMark"] = $"0x{DefenseUnitMark:X02}",
                ["EscapeMarkerX"] = $"0x{EscapeMarkerX:X02}",
                ["EscapeMarkerY"] = $"0x{EscapeMarkerY:X02}",
                ["UnknownB145"] = $"0x{UnknownB145:X02}",
                ["UnknownB146"] = $"0x{UnknownB146:X02}",
                ["UnknownB147"] = $"0x{UnknownB147:X02}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
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
                ["u8@0x2C"] = $"0x{rom.u8(a + 44):X02}",
            };
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
            if (ds > 99) report["u32@0x60"] = $"0x{rom.u32(a + 96):X08}";
            if (ds > 103) report["u32@0x64"] = $"0x{rom.u32(a + 100):X08}";
            if (ds > 107) report["u32@0x68"] = $"0x{rom.u32(a + 104):X08}";
            if (ds > 111) report["u32@0x6C"] = $"0x{rom.u32(a + 108):X08}";
            if (ds > 113) report["u16@0x70"] = $"0x{rom.u16(a + 112):X04}";
            if (ds > 115) report["u16@0x72"] = $"0x{rom.u16(a + 114):X04}";
            if (ds > 116) report["u8@0x74"] = $"0x{rom.u8(a + 116):X02}";
            if (ds > 117) report["u8@0x75"] = $"0x{rom.u8(a + 117):X02}";
            if (ds > 118) report["u8@0x76"] = $"0x{rom.u8(a + 118):X02}";
            if (ds > 119) report["u8@0x77"] = $"0x{rom.u8(a + 119):X02}";
            if (ds > 120) report["u8@0x78"] = $"0x{rom.u8(a + 120):X02}";
            if (ds > 121) report["u8@0x79"] = $"0x{rom.u8(a + 121):X02}";
            if (ds > 122) report["u8@0x7A"] = $"0x{rom.u8(a + 122):X02}";
            if (ds > 123) report["u8@0x7B"] = $"0x{rom.u8(a + 123):X02}";
            if (ds > 124) report["u8@0x7C"] = $"0x{rom.u8(a + 124):X02}";
            if (ds > 125) report["u8@0x7D"] = $"0x{rom.u8(a + 125):X02}";
            if (ds > 126) report["u8@0x7E"] = $"0x{rom.u8(a + 126):X02}";
            if (ds > 127) report["u8@0x7F"] = $"0x{rom.u8(a + 127):X02}";
            if (ds > 128) report["u8@0x80"] = $"0x{rom.u8(a + 128):X02}";
            if (ds > 129) report["u8@0x81"] = $"0x{rom.u8(a + 129):X02}";
            if (ds > 130) report["u8@0x82"] = $"0x{rom.u8(a + 130):X02}";
            if (ds > 131) report["u8@0x83"] = $"0x{rom.u8(a + 131):X02}";
            if (ds > 132) report["u8@0x84"] = $"0x{rom.u8(a + 132):X02}";
            if (ds > 133) report["u8@0x85"] = $"0x{rom.u8(a + 133):X02}";
            if (ds > 134) report["u8@0x86"] = $"0x{rom.u8(a + 134):X02}";
            if (ds > 135) report["u8@0x87"] = $"0x{rom.u8(a + 135):X02}";
            if (ds > 137) report["u16@0x88"] = $"0x{rom.u16(a + 136):X04}";
            if (ds > 139) report["u16@0x8A"] = $"0x{rom.u16(a + 138):X04}";
            if (ds > 140) report["u8@0x8C"] = $"0x{rom.u8(a + 140):X02}";
            if (ds > 141) report["u8@0x8D"] = $"0x{rom.u8(a + 141):X02}";
            if (ds > 142) report["u8@0x8E"] = $"0x{rom.u8(a + 142):X02}";
            if (ds > 143) report["u8@0x8F"] = $"0x{rom.u8(a + 143):X02}";
            if (ds > 144) report["u8@0x90"] = $"0x{rom.u8(a + 144):X02}";
            if (ds > 145) report["u8@0x91"] = $"0x{rom.u8(a + 145):X02}";
            if (ds > 146) report["u8@0x92"] = $"0x{rom.u8(a + 146):X02}";
            if (ds > 147) report["u8@0x93"] = $"0x{rom.u8(a + 147):X02}";
            return report;
        }

        // Backward-compatible property aliases used by old code
        public uint TilesetPLIST => ObjectTypePLIST;
        public uint MapPLIST => MapPointerPLIST;
        public uint ChapterNameId => MapNameText1;
        public uint ObjType => BattlePreparation;
    }
}
