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

        // --- D0: Pointer/CString at offset 0 ---
        uint _d0;
        // --- W4: Object type PLIST (u16) ---
        uint _w4;
        // --- B6-B19: byte fields ---
        uint _b6, _b7, _b8, _b9, _b10, _b11;
        uint _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        // --- W20-W42: word fields (BGM, difficulty, etc.) ---
        uint _w20, _w22, _w24, _w26, _w28, _w30, _w32, _w34, _w36, _w38, _w40, _w42;
        // --- B44-B61: byte fields (wall HP, event unit data, etc.) ---
        uint _b44, _b45, _b46, _b47, _b48, _b49, _b50, _b51;
        uint _b52, _b53, _b54, _b55, _b56, _b57, _b58, _b59, _b60, _b61;
        // --- W62-W94: word fields (event unit positions, etc.) ---
        uint _w62, _w64, _w66, _w68, _w70, _w72, _w74, _w76;
        uint _w78, _w80, _w82, _w84, _w86, _w88, _w90, _w92, _w94;
        // --- D96, D100, D104, D108: dword fields ---
        uint _d96, _d100, _d104, _d108;
        // --- W112, W114: map name text IDs ---
        uint _w112, _w114;
        // --- B116-B135: byte fields (event PLIST, worldmap event, etc.) ---
        uint _b116, _b117, _b118, _b119, _b120, _b121, _b122, _b123;
        uint _b124, _b125, _b126, _b127, _b128, _b129, _b130, _b131;
        uint _b132, _b133, _b134, _b135;
        // --- W136, W138: text IDs ---
        uint _w136, _w138;
        // --- B140-B147: byte fields ---
        uint _b140, _b141, _b142, _b143, _b144, _b145, _b146, _b147;

        // ---- Properties ----

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }

        // D0: CP / CString pointer
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }

        // W4: Object type PLIST
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }

        // B6: Palette PLIST
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        // B7: Chipset config PLIST
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        // B8: Map pointer PLIST
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        // B9: Tile animation 1 PLIST
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        // B10: Tile animation 2 PLIST
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        // B11: Map change PLIST
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        // B12: Fog level
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        // B13: Battle preparation (X)
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        // B14: Chapter title image
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        // B15: Chapter title image 2 (X)
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        // B16
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        // B17
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        // B18: Weather
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        // B19: Battle BG terrain lookup table
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }

        // W20: Difficulty adjustment
        public uint W20 { get => _w20; set => SetField(ref _w20, value); }
        // W22: Player phase BGM
        public uint W22 { get => _w22; set => SetField(ref _w22, value); }
        // W24: Enemy phase BGM
        public uint W24 { get => _w24; set => SetField(ref _w24, value); }
        // W26: NPC BGM
        public uint W26 { get => _w26; set => SetField(ref _w26, value); }
        // W28: Player phase BGM 2 (X)
        public uint W28 { get => _w28; set => SetField(ref _w28, value); }
        // W30: Enemy phase BGM 2 (X)
        public uint W30 { get => _w30; set => SetField(ref _w30, value); }
        // W32: NPC BGM 2 (X)
        public uint W32 { get => _w32; set => SetField(ref _w32, value); }
        // W34: Player phase BGM flag 4
        public uint W34 { get => _w34; set => SetField(ref _w34, value); }
        // W36: Enemy phase BGM flag 4
        public uint W36 { get => _w36; set => SetField(ref _w36, value); }
        // W38: ???
        public uint W38 { get => _w38; set => SetField(ref _w38, value); }
        // W40: ???
        public uint W40 { get => _w40; set => SetField(ref _w40, value); }
        // W42: ???
        public uint W42 { get => _w42; set => SetField(ref _w42, value); }

        // B44: Breakable wall HP
        public uint B44 { get => _b44; set => SetField(ref _b44, value); }
        // B45-B61
        public uint B45 { get => _b45; set => SetField(ref _b45, value); }
        public uint B46 { get => _b46; set => SetField(ref _b46, value); }
        public uint B47 { get => _b47; set => SetField(ref _b47, value); }
        public uint B48 { get => _b48; set => SetField(ref _b48, value); }
        public uint B49 { get => _b49; set => SetField(ref _b49, value); }
        public uint B50 { get => _b50; set => SetField(ref _b50, value); }
        public uint B51 { get => _b51; set => SetField(ref _b51, value); }
        public uint B52 { get => _b52; set => SetField(ref _b52, value); }
        public uint B53 { get => _b53; set => SetField(ref _b53, value); }
        public uint B54 { get => _b54; set => SetField(ref _b54, value); }
        public uint B55 { get => _b55; set => SetField(ref _b55, value); }
        public uint B56 { get => _b56; set => SetField(ref _b56, value); }
        public uint B57 { get => _b57; set => SetField(ref _b57, value); }
        public uint B58 { get => _b58; set => SetField(ref _b58, value); }
        public uint B59 { get => _b59; set => SetField(ref _b59, value); }
        public uint B60 { get => _b60; set => SetField(ref _b60, value); }
        public uint B61 { get => _b61; set => SetField(ref _b61, value); }

        // W62-W94: event unit positions / pointers
        public uint W62 { get => _w62; set => SetField(ref _w62, value); }
        public uint W64 { get => _w64; set => SetField(ref _w64, value); }
        public uint W66 { get => _w66; set => SetField(ref _w66, value); }
        public uint W68 { get => _w68; set => SetField(ref _w68, value); }
        public uint W70 { get => _w70; set => SetField(ref _w70, value); }
        public uint W72 { get => _w72; set => SetField(ref _w72, value); }
        public uint W74 { get => _w74; set => SetField(ref _w74, value); }
        public uint W76 { get => _w76; set => SetField(ref _w76, value); }
        public uint W78 { get => _w78; set => SetField(ref _w78, value); }
        public uint W80 { get => _w80; set => SetField(ref _w80, value); }
        public uint W82 { get => _w82; set => SetField(ref _w82, value); }
        public uint W84 { get => _w84; set => SetField(ref _w84, value); }
        public uint W86 { get => _w86; set => SetField(ref _w86, value); }
        public uint W88 { get => _w88; set => SetField(ref _w88, value); }
        public uint W90 { get => _w90; set => SetField(ref _w90, value); }
        public uint W92 { get => _w92; set => SetField(ref _w92, value); }
        public uint W94 { get => _w94; set => SetField(ref _w94, value); }

        // D96, D100, D104, D108: dword fields
        public uint D96 { get => _d96; set => SetField(ref _d96, value); }
        public uint D100 { get => _d100; set => SetField(ref _d100, value); }
        public uint D104 { get => _d104; set => SetField(ref _d104, value); }
        public uint D108 { get => _d108; set => SetField(ref _d108, value); }

        // W112: Map name 1 text ID
        public uint W112 { get => _w112; set => SetField(ref _w112, value); }
        // W114: Map name 2 (X) text ID
        public uint W114 { get => _w114; set => SetField(ref _w114, value); }

        // B116: Event ID (PLIST)
        public uint B116 { get => _b116; set => SetField(ref _b116, value); }
        // B117: World map auto event
        public uint B117 { get => _b117; set => SetField(ref _b117, value); }
        // B118-B127
        public uint B118 { get => _b118; set => SetField(ref _b118, value); }
        public uint B119 { get => _b119; set => SetField(ref _b119, value); }
        public uint B120 { get => _b120; set => SetField(ref _b120, value); }
        public uint B121 { get => _b121; set => SetField(ref _b121, value); }
        public uint B122 { get => _b122; set => SetField(ref _b122, value); }
        public uint B123 { get => _b123; set => SetField(ref _b123, value); }
        public uint B124 { get => _b124; set => SetField(ref _b124, value); }
        public uint B125 { get => _b125; set => SetField(ref _b125, value); }
        public uint B126 { get => _b126; set => SetField(ref _b126, value); }
        public uint B127 { get => _b127; set => SetField(ref _b127, value); }
        // B128: Chapter number
        public uint B128 { get => _b128; set => SetField(ref _b128, value); }
        // B129-B133
        public uint B129 { get => _b129; set => SetField(ref _b129, value); }
        public uint B130 { get => _b130; set => SetField(ref _b130, value); }
        public uint B131 { get => _b131; set => SetField(ref _b131, value); }
        public uint B132 { get => _b132; set => SetField(ref _b132, value); }
        public uint B133 { get => _b133; set => SetField(ref _b133, value); }
        // B134: Victory BGM enemy count threshold
        public uint B134 { get => _b134; set => SetField(ref _b134, value); }
        // B135: Darken before start event
        public uint B135 { get => _b135; set => SetField(ref _b135, value); }

        // W136: Clear condition text (display only)
        public uint W136 { get => _w136; set => SetField(ref _w136, value); }
        // W138: Detailed clear condition text (display only)
        public uint W138 { get => _w138; set => SetField(ref _w138, value); }

        // B140: Special display
        public uint B140 { get => _b140; set => SetField(ref _b140, value); }
        // B141: Turn count display
        public uint B141 { get => _b141; set => SetField(ref _b141, value); }
        // B142: Defense unit diamond mark
        public uint B142 { get => _b142; set => SetField(ref _b142, value); }
        // B143
        public uint B143 { get => _b143; set => SetField(ref _b143, value); }
        // B144
        public uint B144 { get => _b144; set => SetField(ref _b144, value); }
        // B145
        public uint B145 { get => _b145; set => SetField(ref _b145, value); }
        // B146
        public uint B146 { get => _b146; set => SetField(ref _b146, value); }
        // B147
        public uint B147 { get => _b147; set => SetField(ref _b147, value); }

        // ---- Helpers for label text resolution ----

        public string W112Text => ResolveText(W112);
        public string W114Text => ResolveText(W114);
        public string W136Text => ResolveText(W136);
        public string W138Text => ResolveText(W138);

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

            // D0: dword at offset 0
            D0 = rom.u32(addr + 0);

            // W4: word at offset 4
            W4 = rom.u16(addr + 4);

            // B6-B19
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B18 = rom.u8(addr + 18);
            B19 = rom.u8(addr + 19);

            // W20-W42
            W20 = rom.u16(addr + 20);
            W22 = rom.u16(addr + 22);
            W24 = rom.u16(addr + 24);
            W26 = rom.u16(addr + 26);
            W28 = rom.u16(addr + 28);
            W30 = rom.u16(addr + 30);
            W32 = rom.u16(addr + 32);
            W34 = rom.u16(addr + 34);
            W36 = rom.u16(addr + 36);
            W38 = rom.u16(addr + 38);
            W40 = rom.u16(addr + 40);
            W42 = rom.u16(addr + 42);

            // B44-B61
            B44 = rom.u8(addr + 44);
            if (dataSize > 45) B45 = rom.u8(addr + 45);
            if (dataSize > 46) B46 = rom.u8(addr + 46);
            if (dataSize > 47) B47 = rom.u8(addr + 47);
            if (dataSize > 48) B48 = rom.u8(addr + 48);
            if (dataSize > 49) B49 = rom.u8(addr + 49);
            if (dataSize > 50) B50 = rom.u8(addr + 50);
            if (dataSize > 51) B51 = rom.u8(addr + 51);
            if (dataSize > 52) B52 = rom.u8(addr + 52);
            if (dataSize > 53) B53 = rom.u8(addr + 53);
            if (dataSize > 54) B54 = rom.u8(addr + 54);
            if (dataSize > 55) B55 = rom.u8(addr + 55);
            if (dataSize > 56) B56 = rom.u8(addr + 56);
            if (dataSize > 57) B57 = rom.u8(addr + 57);
            if (dataSize > 58) B58 = rom.u8(addr + 58);
            if (dataSize > 59) B59 = rom.u8(addr + 59);
            if (dataSize > 60) B60 = rom.u8(addr + 60);
            if (dataSize > 61) B61 = rom.u8(addr + 61);

            // W62-W94
            if (dataSize > 63) W62 = rom.u16(addr + 62);
            if (dataSize > 65) W64 = rom.u16(addr + 64);
            if (dataSize > 67) W66 = rom.u16(addr + 66);
            if (dataSize > 69) W68 = rom.u16(addr + 68);
            if (dataSize > 71) W70 = rom.u16(addr + 70);
            if (dataSize > 73) W72 = rom.u16(addr + 72);
            if (dataSize > 75) W74 = rom.u16(addr + 74);
            if (dataSize > 77) W76 = rom.u16(addr + 76);
            if (dataSize > 79) W78 = rom.u16(addr + 78);
            if (dataSize > 81) W80 = rom.u16(addr + 80);
            if (dataSize > 83) W82 = rom.u16(addr + 82);
            if (dataSize > 85) W84 = rom.u16(addr + 84);
            if (dataSize > 87) W86 = rom.u16(addr + 86);
            if (dataSize > 89) W88 = rom.u16(addr + 88);
            if (dataSize > 91) W90 = rom.u16(addr + 90);
            if (dataSize > 93) W92 = rom.u16(addr + 92);
            if (dataSize > 95) W94 = rom.u16(addr + 94);

            // D96, D100, D104, D108
            if (dataSize > 99) D96 = rom.u32(addr + 96);
            if (dataSize > 103) D100 = rom.u32(addr + 100);
            if (dataSize > 107) D104 = rom.u32(addr + 104);
            if (dataSize > 111) D108 = rom.u32(addr + 108);

            // W112, W114
            if (dataSize > 113) W112 = rom.u16(addr + 112);
            if (dataSize > 115) W114 = rom.u16(addr + 114);

            // B116-B135
            if (dataSize > 116) B116 = rom.u8(addr + 116);
            if (dataSize > 117) B117 = rom.u8(addr + 117);
            if (dataSize > 118) B118 = rom.u8(addr + 118);
            if (dataSize > 119) B119 = rom.u8(addr + 119);
            if (dataSize > 120) B120 = rom.u8(addr + 120);
            if (dataSize > 121) B121 = rom.u8(addr + 121);
            if (dataSize > 122) B122 = rom.u8(addr + 122);
            if (dataSize > 123) B123 = rom.u8(addr + 123);
            if (dataSize > 124) B124 = rom.u8(addr + 124);
            if (dataSize > 125) B125 = rom.u8(addr + 125);
            if (dataSize > 126) B126 = rom.u8(addr + 126);
            if (dataSize > 127) B127 = rom.u8(addr + 127);
            if (dataSize > 128) B128 = rom.u8(addr + 128);
            if (dataSize > 129) B129 = rom.u8(addr + 129);
            if (dataSize > 130) B130 = rom.u8(addr + 130);
            if (dataSize > 131) B131 = rom.u8(addr + 131);
            if (dataSize > 132) B132 = rom.u8(addr + 132);
            if (dataSize > 133) B133 = rom.u8(addr + 133);
            if (dataSize > 134) B134 = rom.u8(addr + 134);
            if (dataSize > 135) B135 = rom.u8(addr + 135);

            // W136, W138
            if (dataSize > 137) W136 = rom.u16(addr + 136);
            if (dataSize > 139) W138 = rom.u16(addr + 138);

            // B140-B147
            if (dataSize > 140) B140 = rom.u8(addr + 140);
            if (dataSize > 141) B141 = rom.u8(addr + 141);
            if (dataSize > 142) B142 = rom.u8(addr + 142);
            if (dataSize > 143) B143 = rom.u8(addr + 143);
            if (dataSize > 144) B144 = rom.u8(addr + 144);
            if (dataSize > 145) B145 = rom.u8(addr + 145);
            if (dataSize > 146) B146 = rom.u8(addr + 146);
            if (dataSize > 147) B147 = rom.u8(addr + 147);

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

            // Notify text-resolved properties changed
            OnPropertyChanged(nameof(W112Text));
            OnPropertyChanged(nameof(W114Text));
            OnPropertyChanged(nameof(W136Text));
            OnPropertyChanged(nameof(W138Text));

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

            // D0
            rom.write_u32(addr + 0, D0);
            // W4
            rom.write_u16(addr + 4, W4);

            // B6-B19
            rom.write_u8(addr + 6, B6);
            rom.write_u8(addr + 7, B7);
            rom.write_u8(addr + 8, B8);
            rom.write_u8(addr + 9, B9);
            rom.write_u8(addr + 10, B10);
            rom.write_u8(addr + 11, B11);
            rom.write_u8(addr + 12, B12);
            rom.write_u8(addr + 13, B13);
            rom.write_u8(addr + 14, B14);
            rom.write_u8(addr + 15, B15);
            rom.write_u8(addr + 16, B16);
            rom.write_u8(addr + 17, B17);
            rom.write_u8(addr + 18, B18);
            rom.write_u8(addr + 19, B19);

            // W20-W42
            rom.write_u16(addr + 20, W20);
            rom.write_u16(addr + 22, W22);
            rom.write_u16(addr + 24, W24);
            rom.write_u16(addr + 26, W26);
            rom.write_u16(addr + 28, W28);
            rom.write_u16(addr + 30, W30);
            rom.write_u16(addr + 32, W32);
            rom.write_u16(addr + 34, W34);
            rom.write_u16(addr + 36, W36);
            rom.write_u16(addr + 38, W38);
            rom.write_u16(addr + 40, W40);
            rom.write_u16(addr + 42, W42);

            // B44-B61
            rom.write_u8(addr + 44, B44);
            if (dataSize > 45) rom.write_u8(addr + 45, B45);
            if (dataSize > 46) rom.write_u8(addr + 46, B46);
            if (dataSize > 47) rom.write_u8(addr + 47, B47);
            if (dataSize > 48) rom.write_u8(addr + 48, B48);
            if (dataSize > 49) rom.write_u8(addr + 49, B49);
            if (dataSize > 50) rom.write_u8(addr + 50, B50);
            if (dataSize > 51) rom.write_u8(addr + 51, B51);
            if (dataSize > 52) rom.write_u8(addr + 52, B52);
            if (dataSize > 53) rom.write_u8(addr + 53, B53);
            if (dataSize > 54) rom.write_u8(addr + 54, B54);
            if (dataSize > 55) rom.write_u8(addr + 55, B55);
            if (dataSize > 56) rom.write_u8(addr + 56, B56);
            if (dataSize > 57) rom.write_u8(addr + 57, B57);
            if (dataSize > 58) rom.write_u8(addr + 58, B58);
            if (dataSize > 59) rom.write_u8(addr + 59, B59);
            if (dataSize > 60) rom.write_u8(addr + 60, B60);
            if (dataSize > 61) rom.write_u8(addr + 61, B61);

            // W62-W94
            if (dataSize > 63) rom.write_u16(addr + 62, W62);
            if (dataSize > 65) rom.write_u16(addr + 64, W64);
            if (dataSize > 67) rom.write_u16(addr + 66, W66);
            if (dataSize > 69) rom.write_u16(addr + 68, W68);
            if (dataSize > 71) rom.write_u16(addr + 70, W70);
            if (dataSize > 73) rom.write_u16(addr + 72, W72);
            if (dataSize > 75) rom.write_u16(addr + 74, W74);
            if (dataSize > 77) rom.write_u16(addr + 76, W76);
            if (dataSize > 79) rom.write_u16(addr + 78, W78);
            if (dataSize > 81) rom.write_u16(addr + 80, W80);
            if (dataSize > 83) rom.write_u16(addr + 82, W82);
            if (dataSize > 85) rom.write_u16(addr + 84, W84);
            if (dataSize > 87) rom.write_u16(addr + 86, W86);
            if (dataSize > 89) rom.write_u16(addr + 88, W88);
            if (dataSize > 91) rom.write_u16(addr + 90, W90);
            if (dataSize > 93) rom.write_u16(addr + 92, W92);
            if (dataSize > 95) rom.write_u16(addr + 94, W94);

            // D96-D108
            if (dataSize > 99) rom.write_u32(addr + 96, D96);
            if (dataSize > 103) rom.write_u32(addr + 100, D100);
            if (dataSize > 107) rom.write_u32(addr + 104, D104);
            if (dataSize > 111) rom.write_u32(addr + 108, D108);

            // W112, W114
            if (dataSize > 113) rom.write_u16(addr + 112, W112);
            if (dataSize > 115) rom.write_u16(addr + 114, W114);

            // B116-B135
            if (dataSize > 116) rom.write_u8(addr + 116, B116);
            if (dataSize > 117) rom.write_u8(addr + 117, B117);
            if (dataSize > 118) rom.write_u8(addr + 118, B118);
            if (dataSize > 119) rom.write_u8(addr + 119, B119);
            if (dataSize > 120) rom.write_u8(addr + 120, B120);
            if (dataSize > 121) rom.write_u8(addr + 121, B121);
            if (dataSize > 122) rom.write_u8(addr + 122, B122);
            if (dataSize > 123) rom.write_u8(addr + 123, B123);
            if (dataSize > 124) rom.write_u8(addr + 124, B124);
            if (dataSize > 125) rom.write_u8(addr + 125, B125);
            if (dataSize > 126) rom.write_u8(addr + 126, B126);
            if (dataSize > 127) rom.write_u8(addr + 127, B127);
            if (dataSize > 128) rom.write_u8(addr + 128, B128);
            if (dataSize > 129) rom.write_u8(addr + 129, B129);
            if (dataSize > 130) rom.write_u8(addr + 130, B130);
            if (dataSize > 131) rom.write_u8(addr + 131, B131);
            if (dataSize > 132) rom.write_u8(addr + 132, B132);
            if (dataSize > 133) rom.write_u8(addr + 133, B133);
            if (dataSize > 134) rom.write_u8(addr + 134, B134);
            if (dataSize > 135) rom.write_u8(addr + 135, B135);

            // W136, W138
            if (dataSize > 137) rom.write_u16(addr + 136, W136);
            if (dataSize > 139) rom.write_u16(addr + 138, W138);

            // B140-B147
            if (dataSize > 140) rom.write_u8(addr + 140, B140);
            if (dataSize > 141) rom.write_u8(addr + 141, B141);
            if (dataSize > 142) rom.write_u8(addr + 142, B142);
            if (dataSize > 143) rom.write_u8(addr + 143, B143);
            if (dataSize > 144) rom.write_u8(addr + 144, B144);
            if (dataSize > 145) rom.write_u8(addr + 145, B145);
            if (dataSize > 146) rom.write_u8(addr + 146, B146);
            if (dataSize > 147) rom.write_u8(addr + 147, B147);

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
                ["D0"] = $"0x{D0:X08}",
                ["W4"] = $"0x{W4:X04}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["B18"] = $"0x{B18:X02}",
                ["B19"] = $"0x{B19:X02}",
                ["W20"] = $"0x{W20:X04}",
                ["W22"] = $"0x{W22:X04}",
                ["W24"] = $"0x{W24:X04}",
                ["W26"] = $"0x{W26:X04}",
                ["W28"] = $"0x{W28:X04}",
                ["W30"] = $"0x{W30:X04}",
                ["W32"] = $"0x{W32:X04}",
                ["W34"] = $"0x{W34:X04}",
                ["W36"] = $"0x{W36:X04}",
                ["W38"] = $"0x{W38:X04}",
                ["W40"] = $"0x{W40:X04}",
                ["W42"] = $"0x{W42:X04}",
                ["B44"] = $"0x{B44:X02}",
                ["B45"] = $"0x{B45:X02}",
                ["B46"] = $"0x{B46:X02}",
                ["B47"] = $"0x{B47:X02}",
                ["B48"] = $"0x{B48:X02}",
                ["B49"] = $"0x{B49:X02}",
                ["B50"] = $"0x{B50:X02}",
                ["B51"] = $"0x{B51:X02}",
                ["B52"] = $"0x{B52:X02}",
                ["B53"] = $"0x{B53:X02}",
                ["B54"] = $"0x{B54:X02}",
                ["B55"] = $"0x{B55:X02}",
                ["B56"] = $"0x{B56:X02}",
                ["B57"] = $"0x{B57:X02}",
                ["B58"] = $"0x{B58:X02}",
                ["B59"] = $"0x{B59:X02}",
                ["B60"] = $"0x{B60:X02}",
                ["B61"] = $"0x{B61:X02}",
                ["W62"] = $"0x{W62:X04}",
                ["W64"] = $"0x{W64:X04}",
                ["W66"] = $"0x{W66:X04}",
                ["W68"] = $"0x{W68:X04}",
                ["W70"] = $"0x{W70:X04}",
                ["W72"] = $"0x{W72:X04}",
                ["W74"] = $"0x{W74:X04}",
                ["W76"] = $"0x{W76:X04}",
                ["W78"] = $"0x{W78:X04}",
                ["W80"] = $"0x{W80:X04}",
                ["W82"] = $"0x{W82:X04}",
                ["W84"] = $"0x{W84:X04}",
                ["W86"] = $"0x{W86:X04}",
                ["W88"] = $"0x{W88:X04}",
                ["W90"] = $"0x{W90:X04}",
                ["W92"] = $"0x{W92:X04}",
                ["W94"] = $"0x{W94:X04}",
                ["D96"] = $"0x{D96:X08}",
                ["D100"] = $"0x{D100:X08}",
                ["D104"] = $"0x{D104:X08}",
                ["D108"] = $"0x{D108:X08}",
                ["W112"] = $"0x{W112:X04}",
                ["W114"] = $"0x{W114:X04}",
                ["B116"] = $"0x{B116:X02}",
                ["B117"] = $"0x{B117:X02}",
                ["B118"] = $"0x{B118:X02}",
                ["B119"] = $"0x{B119:X02}",
                ["B120"] = $"0x{B120:X02}",
                ["B121"] = $"0x{B121:X02}",
                ["B122"] = $"0x{B122:X02}",
                ["B123"] = $"0x{B123:X02}",
                ["B124"] = $"0x{B124:X02}",
                ["B125"] = $"0x{B125:X02}",
                ["B126"] = $"0x{B126:X02}",
                ["B127"] = $"0x{B127:X02}",
                ["B128"] = $"0x{B128:X02}",
                ["B129"] = $"0x{B129:X02}",
                ["B130"] = $"0x{B130:X02}",
                ["B131"] = $"0x{B131:X02}",
                ["B132"] = $"0x{B132:X02}",
                ["B133"] = $"0x{B133:X02}",
                ["B134"] = $"0x{B134:X02}",
                ["B135"] = $"0x{B135:X02}",
                ["W136"] = $"0x{W136:X04}",
                ["W138"] = $"0x{W138:X04}",
                ["B140"] = $"0x{B140:X02}",
                ["B141"] = $"0x{B141:X02}",
                ["B142"] = $"0x{B142:X02}",
                ["B143"] = $"0x{B143:X02}",
                ["B144"] = $"0x{B144:X02}",
                ["B145"] = $"0x{B145:X02}",
                ["B146"] = $"0x{B146:X02}",
                ["B147"] = $"0x{B147:X02}",
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
                // D0
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                // W4
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                // B6-B19
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
                // W20-W42
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
                // B44
                ["u8@0x2C"] = $"0x{rom.u8(a + 44):X02}",
            };
            // B45-B61 (conditional on dataSize)
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
            // W62-W94
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
            // D96-D108
            if (ds > 99) report["u32@0x60"] = $"0x{rom.u32(a + 96):X08}";
            if (ds > 103) report["u32@0x64"] = $"0x{rom.u32(a + 100):X08}";
            if (ds > 107) report["u32@0x68"] = $"0x{rom.u32(a + 104):X08}";
            if (ds > 111) report["u32@0x6C"] = $"0x{rom.u32(a + 108):X08}";
            // W112, W114
            if (ds > 113) report["u16@0x70"] = $"0x{rom.u16(a + 112):X04}";
            if (ds > 115) report["u16@0x72"] = $"0x{rom.u16(a + 114):X04}";
            // B116-B135
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
            // W136, W138
            if (ds > 137) report["u16@0x88"] = $"0x{rom.u16(a + 136):X04}";
            if (ds > 139) report["u16@0x8A"] = $"0x{rom.u16(a + 138):X04}";
            // B140-B147
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
        public uint TilesetPLIST => W4;
        public uint MapPLIST => B8;
        public uint PalettePLIST => B6;
        public uint Weather => B18;
        public uint ChapterNameId => W112;
        public uint ObjType => B13;
    }
}
