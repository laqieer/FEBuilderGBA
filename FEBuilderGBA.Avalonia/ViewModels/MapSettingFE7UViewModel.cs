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

        // --- D0: Pointer/CString at offset 0 ---
        uint _d0;
        // --- W4: Object type PLIST (u16) ---
        uint _w4;
        // --- B6-B19: byte fields ---
        uint _b6, _b7, _b8, _b9, _b10, _b11;
        uint _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        // --- W20-W42: word fields ---
        uint _w20, _w22, _w24, _w26, _w28, _w30, _w32, _w34, _w36, _w38, _w40, _w42;
        // --- B44-B61: byte fields ---
        uint _b44, _b45, _b46, _b47, _b48, _b49, _b50, _b51;
        uint _b52, _b53, _b54, _b55, _b56, _b57, _b58, _b59, _b60, _b61;
        // --- W62-W94: word fields ---
        uint _w62, _w64, _w66, _w68, _w70, _w72, _w74, _w76;
        uint _w78, _w80, _w82, _w84, _w86, _w88, _w90, _w92, _w94;
        // --- D96-D108: dword fields ---
        uint _d96, _d100, _d104, _d108;
        // --- W112-W118: word fields ---
        uint _w112, _w114, _w116, _w118;
        // --- B120, B121 ---
        uint _b120, _b121;
        // --- W122-W128: word fields ---
        uint _w122, _w124, _w126, _w128;
        // --- B130-B139 ---
        uint _b130, _b131, _b132, _b133, _b134, _b135, _b136, _b137, _b138, _b139;
        // --- W140, W142: text IDs ---
        uint _w140, _w142;
        // --- B144-B151 ---
        uint _b144, _b145, _b146, _b147, _b148, _b149, _b150, _b151;

        // ---- Properties ----

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }

        // D0: CP / CString pointer
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }

        // W4: Object type PLIST
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }

        // B6-B19
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }

        // W20-W42
        public uint W20 { get => _w20; set => SetField(ref _w20, value); }
        public uint W22 { get => _w22; set => SetField(ref _w22, value); }
        public uint W24 { get => _w24; set => SetField(ref _w24, value); }
        public uint W26 { get => _w26; set => SetField(ref _w26, value); }
        public uint W28 { get => _w28; set => SetField(ref _w28, value); }
        public uint W30 { get => _w30; set => SetField(ref _w30, value); }
        public uint W32 { get => _w32; set => SetField(ref _w32, value); }
        public uint W34 { get => _w34; set => SetField(ref _w34, value); }
        public uint W36 { get => _w36; set => SetField(ref _w36, value); }
        public uint W38 { get => _w38; set => SetField(ref _w38, value); }
        public uint W40 { get => _w40; set => SetField(ref _w40, value); }
        public uint W42 { get => _w42; set => SetField(ref _w42, value); }

        // B44-B61
        public uint B44 { get => _b44; set => SetField(ref _b44, value); }
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

        // W62-W94
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

        // D96-D108
        public uint D96 { get => _d96; set => SetField(ref _d96, value); }
        public uint D100 { get => _d100; set => SetField(ref _d100, value); }
        public uint D104 { get => _d104; set => SetField(ref _d104, value); }
        public uint D108 { get => _d108; set => SetField(ref _d108, value); }

        // W112-W118: map name text IDs and related
        public uint W112 { get => _w112; set => SetField(ref _w112, value); }
        public uint W114 { get => _w114; set => SetField(ref _w114, value); }
        public uint W116 { get => _w116; set => SetField(ref _w116, value); }
        public uint W118 { get => _w118; set => SetField(ref _w118, value); }

        // B120, B121: event ID PLIST, world map auto event
        public uint B120 { get => _b120; set => SetField(ref _b120, value); }
        public uint B121 { get => _b121; set => SetField(ref _b121, value); }

        // W122-W128
        public uint W122 { get => _w122; set => SetField(ref _w122, value); }
        public uint W124 { get => _w124; set => SetField(ref _w124, value); }
        public uint W126 { get => _w126; set => SetField(ref _w126, value); }
        public uint W128 { get => _w128; set => SetField(ref _w128, value); }

        // B130-B139
        public uint B130 { get => _b130; set => SetField(ref _b130, value); }
        public uint B131 { get => _b131; set => SetField(ref _b131, value); }
        public uint B132 { get => _b132; set => SetField(ref _b132, value); }
        public uint B133 { get => _b133; set => SetField(ref _b133, value); }
        public uint B134 { get => _b134; set => SetField(ref _b134, value); }
        public uint B135 { get => _b135; set => SetField(ref _b135, value); }
        public uint B136 { get => _b136; set => SetField(ref _b136, value); }
        public uint B137 { get => _b137; set => SetField(ref _b137, value); }
        public uint B138 { get => _b138; set => SetField(ref _b138, value); }
        public uint B139 { get => _b139; set => SetField(ref _b139, value); }

        // W140, W142: text IDs
        public uint W140 { get => _w140; set => SetField(ref _w140, value); }
        public uint W142 { get => _w142; set => SetField(ref _w142, value); }

        // B144-B151
        public uint B144 { get => _b144; set => SetField(ref _b144, value); }
        public uint B145 { get => _b145; set => SetField(ref _b145, value); }
        public uint B146 { get => _b146; set => SetField(ref _b146, value); }
        public uint B147 { get => _b147; set => SetField(ref _b147, value); }
        public uint B148 { get => _b148; set => SetField(ref _b148, value); }
        public uint B149 { get => _b149; set => SetField(ref _b149, value); }
        public uint B150 { get => _b150; set => SetField(ref _b150, value); }
        public uint B151 { get => _b151; set => SetField(ref _b151, value); }

        // ---- Helpers for label text resolution ----

        public string W112Text => ResolveText(W112);
        public string W114Text => ResolveText(W114);
        public string W116Text => ResolveText(W116);
        public string W118Text => ResolveText(W118);
        public string W140Text => ResolveText(W140);
        public string W142Text => ResolveText(W142);

        static string ResolveText(uint id)
        {
            if (id == 0) return "(none)";
            try { return FETextDecode.Direct(id); }
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

            // D96-D108
            if (dataSize > 99) D96 = rom.u32(addr + 96);
            if (dataSize > 103) D100 = rom.u32(addr + 100);
            if (dataSize > 107) D104 = rom.u32(addr + 104);
            if (dataSize > 111) D108 = rom.u32(addr + 108);

            // W112-W118
            if (dataSize > 113) W112 = rom.u16(addr + 112);
            if (dataSize > 115) W114 = rom.u16(addr + 114);
            if (dataSize > 117) W116 = rom.u16(addr + 116);
            if (dataSize > 119) W118 = rom.u16(addr + 118);

            // B120, B121
            if (dataSize > 120) B120 = rom.u8(addr + 120);
            if (dataSize > 121) B121 = rom.u8(addr + 121);

            // W122-W128
            if (dataSize > 123) W122 = rom.u16(addr + 122);
            if (dataSize > 125) W124 = rom.u16(addr + 124);
            if (dataSize > 127) W126 = rom.u16(addr + 126);
            if (dataSize > 129) W128 = rom.u16(addr + 128);

            // B130-B139
            if (dataSize > 130) B130 = rom.u8(addr + 130);
            if (dataSize > 131) B131 = rom.u8(addr + 131);
            if (dataSize > 132) B132 = rom.u8(addr + 132);
            if (dataSize > 133) B133 = rom.u8(addr + 133);
            if (dataSize > 134) B134 = rom.u8(addr + 134);
            if (dataSize > 135) B135 = rom.u8(addr + 135);
            if (dataSize > 136) B136 = rom.u8(addr + 136);
            if (dataSize > 137) B137 = rom.u8(addr + 137);
            if (dataSize > 138) B138 = rom.u8(addr + 138);
            if (dataSize > 139) B139 = rom.u8(addr + 139);

            // W140, W142
            if (dataSize > 141) W140 = rom.u16(addr + 140);
            if (dataSize > 143) W142 = rom.u16(addr + 142);

            // B144-B151
            if (dataSize > 144) B144 = rom.u8(addr + 144);
            if (dataSize > 145) B145 = rom.u8(addr + 145);
            if (dataSize > 146) B146 = rom.u8(addr + 146);
            if (dataSize > 147) B147 = rom.u8(addr + 147);
            if (dataSize > 148) B148 = rom.u8(addr + 148);
            if (dataSize > 149) B149 = rom.u8(addr + 149);
            if (dataSize > 150) B150 = rom.u8(addr + 150);
            if (dataSize > 151) B151 = rom.u8(addr + 151);

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
            OnPropertyChanged(nameof(W116Text));
            OnPropertyChanged(nameof(W118Text));
            OnPropertyChanged(nameof(W140Text));
            OnPropertyChanged(nameof(W142Text));

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

            // W112-W118
            if (dataSize > 113) rom.write_u16(addr + 112, W112);
            if (dataSize > 115) rom.write_u16(addr + 114, W114);
            if (dataSize > 117) rom.write_u16(addr + 116, W116);
            if (dataSize > 119) rom.write_u16(addr + 118, W118);

            // B120, B121
            if (dataSize > 120) rom.write_u8(addr + 120, B120);
            if (dataSize > 121) rom.write_u8(addr + 121, B121);

            // W122-W128
            if (dataSize > 123) rom.write_u16(addr + 122, W122);
            if (dataSize > 125) rom.write_u16(addr + 124, W124);
            if (dataSize > 127) rom.write_u16(addr + 126, W126);
            if (dataSize > 129) rom.write_u16(addr + 128, W128);

            // B130-B139
            if (dataSize > 130) rom.write_u8(addr + 130, B130);
            if (dataSize > 131) rom.write_u8(addr + 131, B131);
            if (dataSize > 132) rom.write_u8(addr + 132, B132);
            if (dataSize > 133) rom.write_u8(addr + 133, B133);
            if (dataSize > 134) rom.write_u8(addr + 134, B134);
            if (dataSize > 135) rom.write_u8(addr + 135, B135);
            if (dataSize > 136) rom.write_u8(addr + 136, B136);
            if (dataSize > 137) rom.write_u8(addr + 137, B137);
            if (dataSize > 138) rom.write_u8(addr + 138, B138);
            if (dataSize > 139) rom.write_u8(addr + 139, B139);

            // W140, W142
            if (dataSize > 141) rom.write_u16(addr + 140, W140);
            if (dataSize > 143) rom.write_u16(addr + 142, W142);

            // B144-B151
            if (dataSize > 144) rom.write_u8(addr + 144, B144);
            if (dataSize > 145) rom.write_u8(addr + 145, B145);
            if (dataSize > 146) rom.write_u8(addr + 146, B146);
            if (dataSize > 147) rom.write_u8(addr + 147, B147);
            if (dataSize > 148) rom.write_u8(addr + 148, B148);
            if (dataSize > 149) rom.write_u8(addr + 149, B149);
            if (dataSize > 150) rom.write_u8(addr + 150, B150);
            if (dataSize > 151) rom.write_u8(addr + 151, B151);

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
                ["W116"] = $"0x{W116:X04}",
                ["W118"] = $"0x{W118:X04}",
                ["B120"] = $"0x{B120:X02}",
                ["B121"] = $"0x{B121:X02}",
                ["W122"] = $"0x{W122:X04}",
                ["W124"] = $"0x{W124:X04}",
                ["W126"] = $"0x{W126:X04}",
                ["W128"] = $"0x{W128:X04}",
                ["B134"] = $"0x{B134:X02}",
                ["B135"] = $"0x{B135:X02}",
                ["B138"] = $"0x{B138:X02}",
                ["B139"] = $"0x{B139:X02}",
                ["W140"] = $"0x{W140:X04}",
                ["W142"] = $"0x{W142:X04}",
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
            // W112-W118
            if (ds > 113) report["u16@112"] = $"0x{rom.u16(a + 112):X04}";
            if (ds > 115) report["u16@114"] = $"0x{rom.u16(a + 114):X04}";
            if (ds > 117) report["u16@116"] = $"0x{rom.u16(a + 116):X04}";
            if (ds > 119) report["u16@118"] = $"0x{rom.u16(a + 118):X04}";
            // B120, B121
            if (ds > 120) report["u8@120"] = $"0x{rom.u8(a + 120):X02}";
            if (ds > 121) report["u8@121"] = $"0x{rom.u8(a + 121):X02}";
            // W122-W128
            if (ds > 123) report["u16@122"] = $"0x{rom.u16(a + 122):X04}";
            if (ds > 125) report["u16@124"] = $"0x{rom.u16(a + 124):X04}";
            if (ds > 127) report["u16@126"] = $"0x{rom.u16(a + 126):X04}";
            if (ds > 129) report["u16@128"] = $"0x{rom.u16(a + 128):X04}";
            // B130-B139
            for (uint i = 130; i < 140 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
            if (ds > 141) report["u16@140"] = $"0x{rom.u16(a + 140):X04}";
            if (ds > 143) report["u16@142"] = $"0x{rom.u16(a + 142):X04}";
            for (uint i = 144; i < 152 && i < ds; i++)
                report[$"u8@{i}"] = $"0x{rom.u8(a + i):X02}";
            return report;
        }

        // Backward-compatible property aliases
        public uint TilesetPLIST => W4;
        public uint MapPLIST => B8;
        public uint PalettePLIST => B6;
        public uint Weather => B18;
        public uint ChapterNameId => W112;
        public uint ObjType => B13;
    }
}
