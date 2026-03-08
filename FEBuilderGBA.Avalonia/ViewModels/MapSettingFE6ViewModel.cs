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

        // --- D0: Pointer at offset 0 ---
        uint _d0;
        // --- W4: Object type PLIST (u16) ---
        uint _w4;
        // --- B6-B24: byte fields ---
        uint _b6, _b7, _b8, _b9, _b10, _b11;
        uint _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        uint _b20, _b21, _b22, _b23, _b24;
        // --- B25-B31: byte fields ---
        uint _b25, _b26, _b27, _b28, _b29, _b30, _b31;
        // --- W32-W56: word fields (BGM, difficulty, etc.) ---
        uint _w32, _w34, _w36, _w38, _w40, _w42;
        uint _w44, _w46, _w48, _w50, _w52, _w54, _w56;
        // --- B58-B59: byte fields ---
        uint _b58, _b59;
        // --- W60: word field ---
        uint _w60;
        // --- B62-B67: byte fields ---
        uint _b62, _b63, _b64, _b65, _b66, _b67;

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
        // B13: Battle preparation
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        // B14: Chapter title image
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        // B15
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        // B16
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        // B17
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        // B18: Weather
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        // B19: Battle BG terrain lookup table
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }
        // B20
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }
        // B21
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        // B22
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        // B23
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        // B24
        public uint B24 { get => _b24; set => SetField(ref _b24, value); }
        // B25
        public uint B25 { get => _b25; set => SetField(ref _b25, value); }
        // B26
        public uint B26 { get => _b26; set => SetField(ref _b26, value); }
        // B27
        public uint B27 { get => _b27; set => SetField(ref _b27, value); }
        // B28
        public uint B28 { get => _b28; set => SetField(ref _b28, value); }
        // B29
        public uint B29 { get => _b29; set => SetField(ref _b29, value); }
        // B30
        public uint B30 { get => _b30; set => SetField(ref _b30, value); }
        // B31
        public uint B31 { get => _b31; set => SetField(ref _b31, value); }

        // W32: Player phase BGM
        public uint W32 { get => _w32; set => SetField(ref _w32, value); }
        // W34: Enemy phase BGM
        public uint W34 { get => _w34; set => SetField(ref _w34, value); }
        // W36: NPC BGM
        public uint W36 { get => _w36; set => SetField(ref _w36, value); }
        // W38
        public uint W38 { get => _w38; set => SetField(ref _w38, value); }
        // W40
        public uint W40 { get => _w40; set => SetField(ref _w40, value); }
        // W42
        public uint W42 { get => _w42; set => SetField(ref _w42, value); }
        // W44
        public uint W44 { get => _w44; set => SetField(ref _w44, value); }
        // W46
        public uint W46 { get => _w46; set => SetField(ref _w46, value); }
        // W48
        public uint W48 { get => _w48; set => SetField(ref _w48, value); }
        // W50
        public uint W50 { get => _w50; set => SetField(ref _w50, value); }
        // W52
        public uint W52 { get => _w52; set => SetField(ref _w52, value); }
        // W54
        public uint W54 { get => _w54; set => SetField(ref _w54, value); }
        // W56
        public uint W56 { get => _w56; set => SetField(ref _w56, value); }

        // B58
        public uint B58 { get => _b58; set => SetField(ref _b58, value); }
        // B59
        public uint B59 { get => _b59; set => SetField(ref _b59, value); }

        // W60
        public uint W60 { get => _w60; set => SetField(ref _w60, value); }

        // B62
        public uint B62 { get => _b62; set => SetField(ref _b62, value); }
        // B63
        public uint B63 { get => _b63; set => SetField(ref _b63, value); }
        // B64
        public uint B64 { get => _b64; set => SetField(ref _b64, value); }
        // B65
        public uint B65 { get => _b65; set => SetField(ref _b65, value); }
        // B66
        public uint B66 { get => _b66; set => SetField(ref _b66, value); }
        // B67
        public uint B67 { get => _b67; set => SetField(ref _b67, value); }

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

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) dataSize = 68;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataSize = dataSize;

            // D0: dword at offset 0
            D0 = rom.u32(addr + 0);

            // W4: word at offset 4
            W4 = rom.u16(addr + 4);

            // B6-B24
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
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            B24 = rom.u8(addr + 24);

            // B25-B31
            B25 = rom.u8(addr + 25);
            B26 = rom.u8(addr + 26);
            B27 = rom.u8(addr + 27);
            B28 = rom.u8(addr + 28);
            B29 = rom.u8(addr + 29);
            B30 = rom.u8(addr + 30);
            B31 = rom.u8(addr + 31);

            // W32-W56
            W32 = rom.u16(addr + 32);
            W34 = rom.u16(addr + 34);
            W36 = rom.u16(addr + 36);
            W38 = rom.u16(addr + 38);
            W40 = rom.u16(addr + 40);
            W42 = rom.u16(addr + 42);
            W44 = rom.u16(addr + 44);
            W46 = rom.u16(addr + 46);
            W48 = rom.u16(addr + 48);
            W50 = rom.u16(addr + 50);
            W52 = rom.u16(addr + 52);
            W54 = rom.u16(addr + 54);
            W56 = rom.u16(addr + 56);

            // B58-B59
            if (dataSize > 58) B58 = rom.u8(addr + 58);
            if (dataSize > 59) B59 = rom.u8(addr + 59);

            // W60
            if (dataSize > 61) W60 = rom.u16(addr + 60);

            // B62-B67
            if (dataSize > 62) B62 = rom.u8(addr + 62);
            if (dataSize > 63) B63 = rom.u8(addr + 63);
            if (dataSize > 64) B64 = rom.u8(addr + 64);
            if (dataSize > 65) B65 = rom.u8(addr + 65);
            if (dataSize > 66) B66 = rom.u8(addr + 66);
            if (dataSize > 67) B67 = rom.u8(addr + 67);

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
                undoData = undo.NewUndoData("MapSettingFE6");
                undoData.list.Add(new Undo.UndoPostion(addr, dataSize));
            }

            // D0
            rom.write_u32(addr + 0, D0);
            // W4
            rom.write_u16(addr + 4, W4);

            // B6-B24
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
            rom.write_u8(addr + 20, B20);
            rom.write_u8(addr + 21, B21);
            rom.write_u8(addr + 22, B22);
            rom.write_u8(addr + 23, B23);
            rom.write_u8(addr + 24, B24);

            // B25-B31
            rom.write_u8(addr + 25, B25);
            rom.write_u8(addr + 26, B26);
            rom.write_u8(addr + 27, B27);
            rom.write_u8(addr + 28, B28);
            rom.write_u8(addr + 29, B29);
            rom.write_u8(addr + 30, B30);
            rom.write_u8(addr + 31, B31);

            // W32-W56
            rom.write_u16(addr + 32, W32);
            rom.write_u16(addr + 34, W34);
            rom.write_u16(addr + 36, W36);
            rom.write_u16(addr + 38, W38);
            rom.write_u16(addr + 40, W40);
            rom.write_u16(addr + 42, W42);
            rom.write_u16(addr + 44, W44);
            rom.write_u16(addr + 46, W46);
            rom.write_u16(addr + 48, W48);
            rom.write_u16(addr + 50, W50);
            rom.write_u16(addr + 52, W52);
            rom.write_u16(addr + 54, W54);
            rom.write_u16(addr + 56, W56);

            // B58-B59
            if (dataSize > 58) rom.write_u8(addr + 58, B58);
            if (dataSize > 59) rom.write_u8(addr + 59, B59);

            // W60
            if (dataSize > 61) rom.write_u16(addr + 60, W60);

            // B62-B67
            if (dataSize > 62) rom.write_u8(addr + 62, B62);
            if (dataSize > 63) rom.write_u8(addr + 63, B63);
            if (dataSize > 64) rom.write_u8(addr + 64, B64);
            if (dataSize > 65) rom.write_u8(addr + 65, B65);
            if (dataSize > 66) rom.write_u8(addr + 66, B66);
            if (dataSize > 67) rom.write_u8(addr + 67, B67);

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
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["B24"] = $"0x{B24:X02}",
                ["B25"] = $"0x{B25:X02}",
                ["B26"] = $"0x{B26:X02}",
                ["B27"] = $"0x{B27:X02}",
                ["B28"] = $"0x{B28:X02}",
                ["B29"] = $"0x{B29:X02}",
                ["B30"] = $"0x{B30:X02}",
                ["B31"] = $"0x{B31:X02}",
                ["W32"] = $"0x{W32:X04}",
                ["W34"] = $"0x{W34:X04}",
                ["W36"] = $"0x{W36:X04}",
                ["W38"] = $"0x{W38:X04}",
                ["W40"] = $"0x{W40:X04}",
                ["W42"] = $"0x{W42:X04}",
                ["W44"] = $"0x{W44:X04}",
                ["W46"] = $"0x{W46:X04}",
                ["W48"] = $"0x{W48:X04}",
                ["W50"] = $"0x{W50:X04}",
                ["W52"] = $"0x{W52:X04}",
                ["W54"] = $"0x{W54:X04}",
                ["W56"] = $"0x{W56:X04}",
                ["B58"] = $"0x{B58:X02}",
                ["B59"] = $"0x{B59:X02}",
                ["W60"] = $"0x{W60:X04}",
                ["B62"] = $"0x{B62:X02}",
                ["B63"] = $"0x{B63:X02}",
                ["B64"] = $"0x{B64:X02}",
                ["B65"] = $"0x{B65:X02}",
                ["B66"] = $"0x{B66:X02}",
                ["B67"] = $"0x{B67:X02}",
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
                // B6-B24
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
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                // B25-B31
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                // W32-W56
                ["u16@0x20"] = $"0x{rom.u16(a + 32):X04}",
                ["u16@0x22"] = $"0x{rom.u16(a + 34):X04}",
                ["u16@0x24"] = $"0x{rom.u16(a + 36):X04}",
                ["u16@0x26"] = $"0x{rom.u16(a + 38):X04}",
                ["u16@0x28"] = $"0x{rom.u16(a + 40):X04}",
                ["u16@0x2A"] = $"0x{rom.u16(a + 42):X04}",
                ["u16@0x2C"] = $"0x{rom.u16(a + 44):X04}",
                ["u16@0x2E"] = $"0x{rom.u16(a + 46):X04}",
                ["u16@0x30"] = $"0x{rom.u16(a + 48):X04}",
                ["u16@0x32"] = $"0x{rom.u16(a + 50):X04}",
                ["u16@0x34"] = $"0x{rom.u16(a + 52):X04}",
                ["u16@0x36"] = $"0x{rom.u16(a + 54):X04}",
                ["u16@0x38"] = $"0x{rom.u16(a + 56):X04}",
            };
            // B58-B59
            if (ds > 58) report["u8@0x3A"] = $"0x{rom.u8(a + 58):X02}";
            if (ds > 59) report["u8@0x3B"] = $"0x{rom.u8(a + 59):X02}";
            // W60
            if (ds > 61) report["u16@0x3C"] = $"0x{rom.u16(a + 60):X04}";
            // B62-B67
            if (ds > 62) report["u8@0x3E"] = $"0x{rom.u8(a + 62):X02}";
            if (ds > 63) report["u8@0x3F"] = $"0x{rom.u8(a + 63):X02}";
            if (ds > 64) report["u8@0x40"] = $"0x{rom.u8(a + 64):X02}";
            if (ds > 65) report["u8@0x41"] = $"0x{rom.u8(a + 65):X02}";
            if (ds > 66) report["u8@0x42"] = $"0x{rom.u8(a + 66):X02}";
            if (ds > 67) report["u8@0x43"] = $"0x{rom.u8(a + 67):X02}";
            return report;
        }

        // Alias used by View code
        public List<AddrResult> LoadList() => LoadMapSettingList();

        // Backward-compatible property aliases
        public uint TilesetPLIST => W4;
        public uint MapPLIST => B8;
        public uint PalettePLIST => B6;
        public uint Weather => B18;
    }
}
