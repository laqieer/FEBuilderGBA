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
                ["W112"] = $"0x{W112:X04}",
                ["W114"] = $"0x{W114:X04}",
                ["B116"] = $"0x{B116:X02}",
                ["B117"] = $"0x{B117:X02}",
                ["B128"] = $"0x{B128:X02}",
                ["W136"] = $"0x{W136:X04}",
                ["W138"] = $"0x{W138:X04}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@4"] = $"0x{rom.u16(a + 4):X04}",
            };
            for (uint i = 6; i < 20 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
            for (uint i = 20; i < 44 && i + 1 < ds; i += 2)
                report[$"u16@{i}"] = $"0x{rom.u16(a + i):X04}";
            for (uint i = 44; i < 62 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
            for (uint i = 62; i < 96 && i + 1 < ds; i += 2)
                report[$"u16@{i}"] = $"0x{rom.u16(a + i):X04}";
            for (uint i = 96; i < 112 && i + 3 < ds; i += 4)
                report[$"u32@{i}"] = $"0x{rom.u32(a + i):X08}";
            if (ds > 113) report["u16@112"] = $"0x{rom.u16(a + 112):X04}";
            if (ds > 115) report["u16@114"] = $"0x{rom.u16(a + 114):X04}";
            for (uint i = 116; i < 136 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
            if (ds > 137) report["u16@136"] = $"0x{rom.u16(a + 136):X04}";
            if (ds > 139) report["u16@138"] = $"0x{rom.u16(a + 138):X04}";
            for (uint i = 140; i < 148 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
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
