using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";

        // W0, W2: text IDs
        uint _w0, _w2;
        // B4, B5: class ID, promotion
        uint _b4, _b5;
        // B6, B7: wait icon, walk speed
        uint _b6, _b7;
        // W8: portrait / map sprite
        uint _w8;
        // B10: build stat
        uint _b10;
        // B11-B18: base stats (HP, Str, Skl, Spd, Def, Res, Mov, Con)
        uint _b11, _b12, _b13, _b14, _b15, _b16, _b17, _b18;
        // B19-B26: stat caps
        uint _b19, _b20, _b21, _b22, _b23, _b24, _b25, _b26;
        // B27-B33: growth rates
        uint _b27, _b28, _b29, _b30, _b31, _b32, _b33;
        // B34, B35: promotion bonuses / misc
        uint _b34, _b35;
        // B36-B39: ability flags (with bit fields)
        uint _b36, _b37, _b38, _b39;
        // B40-B47: weapon levels
        uint _b40, _b41, _b42, _b43, _b44, _b45, _b46, _b47;
        // P48, P52, P56, P60, P64: pointers
        uint _p48, _p52, _p56, _p60, _p64;
        // D68: u32 field
        uint _d68;

        bool _canWrite;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // W0: Name text ID
        public uint W0 { get => _w0; set => SetField(ref _w0, value); }
        // W2: Description text ID
        public uint W2 { get => _w2; set => SetField(ref _w2, value); }
        // B4: Class ID
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        // B5: Promotion level
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        // B6: Wait icon
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        // B7: Walk speed
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        // W8: Portrait / map sprite
        public uint W8 { get => _w8; set => SetField(ref _w8, value); }
        // B10: Build stat
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }

        // B11-B18: Base stats
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }

        // B19-B26: Stat caps
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint B24 { get => _b24; set => SetField(ref _b24, value); }
        public uint B25 { get => _b25; set => SetField(ref _b25, value); }
        public uint B26 { get => _b26; set => SetField(ref _b26, value); }

        // B27-B33: Growth rates
        public uint B27 { get => _b27; set => SetField(ref _b27, value); }
        public uint B28 { get => _b28; set => SetField(ref _b28, value); }
        public uint B29 { get => _b29; set => SetField(ref _b29, value); }
        public uint B30 { get => _b30; set => SetField(ref _b30, value); }
        public uint B31 { get => _b31; set => SetField(ref _b31, value); }
        public uint B32 { get => _b32; set => SetField(ref _b32, value); }
        public uint B33 { get => _b33; set => SetField(ref _b33, value); }

        // B34, B35: Promotion bonuses / misc
        public uint B34 { get => _b34; set => SetField(ref _b34, value); }
        public uint B35 { get => _b35; set => SetField(ref _b35, value); }

        // B36-B39: Ability flags
        public uint B36 { get => _b36; set => SetField(ref _b36, value); }
        public uint B37 { get => _b37; set => SetField(ref _b37, value); }
        public uint B38 { get => _b38; set => SetField(ref _b38, value); }
        public uint B39 { get => _b39; set => SetField(ref _b39, value); }

        // B40-B47: Weapon levels
        public uint B40 { get => _b40; set => SetField(ref _b40, value); }
        public uint B41 { get => _b41; set => SetField(ref _b41, value); }
        public uint B42 { get => _b42; set => SetField(ref _b42, value); }
        public uint B43 { get => _b43; set => SetField(ref _b43, value); }
        public uint B44 { get => _b44; set => SetField(ref _b44, value); }
        public uint B45 { get => _b45; set => SetField(ref _b45, value); }
        public uint B46 { get => _b46; set => SetField(ref _b46, value); }
        public uint B47 { get => _b47; set => SetField(ref _b47, value); }

        // P48, P52, P56, P60, P64: Pointers
        public uint P48 { get => _p48; set => SetField(ref _p48, value); }
        public uint P52 { get => _p52; set => SetField(ref _p52, value); }
        public uint P56 { get => _p56; set => SetField(ref _p56, value); }
        public uint P60 { get => _p60; set => SetField(ref _p60, value); }
        public uint P64 { get => _p64; set => SetField(ref _p64, value); }

        // D68: u32 field
        public uint D68 { get => _d68; set => SetField(ref _d68, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.class_datasize ?? 84;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Text IDs
            W0 = rom.u16(addr + 0);
            W2 = rom.u16(addr + 2);
            try { Name = FETextDecode.Direct(W0); }
            catch { Name = "???"; }

            // Identity
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            W8 = rom.u16(addr + 8);
            B10 = rom.u8(addr + 10);

            // Base stats
            B11 = rom.u8(addr + 11);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B18 = rom.u8(addr + 18);

            // Stat caps
            B19 = rom.u8(addr + 19);
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            B24 = rom.u8(addr + 24);
            B25 = rom.u8(addr + 25);
            B26 = rom.u8(addr + 26);

            // Growth rates
            B27 = rom.u8(addr + 27);
            B28 = rom.u8(addr + 28);
            B29 = rom.u8(addr + 29);
            B30 = rom.u8(addr + 30);
            B31 = rom.u8(addr + 31);
            B32 = rom.u8(addr + 32);
            B33 = rom.u8(addr + 33);

            // Promotion bonuses / misc
            B34 = rom.u8(addr + 34);
            B35 = rom.u8(addr + 35);

            // Ability flags
            B36 = rom.u8(addr + 36);
            B37 = rom.u8(addr + 37);
            B38 = rom.u8(addr + 38);
            B39 = rom.u8(addr + 39);

            // Weapon levels
            B40 = rom.u8(addr + 40);
            B41 = rom.u8(addr + 41);
            B42 = rom.u8(addr + 42);
            B43 = rom.u8(addr + 43);
            B44 = rom.u8(addr + 44);
            B45 = rom.u8(addr + 45);
            B46 = rom.u8(addr + 46);
            B47 = rom.u8(addr + 47);

            // Pointers
            P48 = rom.u32(addr + 48);
            P52 = rom.u32(addr + 52);
            P56 = rom.u32(addr + 56);
            P60 = rom.u32(addr + 60);
            P64 = rom.u32(addr + 64);

            // D68
            D68 = rom.u32(addr + 68);

            CanWrite = true;
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0"] = $"0x{W0:X04}",
                ["W2"] = $"0x{W2:X04}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["W8"] = $"0x{W8:X04}",
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
                ["B32"] = $"0x{B32:X02}",
                ["B33"] = $"0x{B33:X02}",
                ["B34"] = $"0x{B34:X02}",
                ["B35"] = $"0x{B35:X02}",
                ["B36"] = $"0x{B36:X02}",
                ["B37"] = $"0x{B37:X02}",
                ["B38"] = $"0x{B38:X02}",
                ["B39"] = $"0x{B39:X02}",
                ["B40"] = $"0x{B40:X02}",
                ["B41"] = $"0x{B41:X02}",
                ["B42"] = $"0x{B42:X02}",
                ["B43"] = $"0x{B43:X02}",
                ["B44"] = $"0x{B44:X02}",
                ["B45"] = $"0x{B45:X02}",
                ["B46"] = $"0x{B46:X02}",
                ["B47"] = $"0x{B47:X02}",
                ["P48"] = $"0x{P48:X08}",
                ["P52"] = $"0x{P52:X08}",
                ["P56"] = $"0x{P56:X08}",
                ["P60"] = $"0x{P60:X08}",
                ["P64"] = $"0x{P64:X08}",
                ["D68"] = $"0x{D68:X08}",
            };
            return d;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                // W0, W2: text IDs
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                // B4-B7: identity
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                // W8: portrait / map sprite
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                // B10: build stat
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                // B11-B18: base stats
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                // B19-B26: stat caps
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                // B27-B33: growth rates
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                // B34-B35: promotion bonuses / misc
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23"] = $"0x{rom.u8(a + 35):X02}",
                // B36-B39: ability flags
                ["u8@0x24"] = $"0x{rom.u8(a + 36):X02}",
                ["u8@0x25"] = $"0x{rom.u8(a + 37):X02}",
                ["u8@0x26"] = $"0x{rom.u8(a + 38):X02}",
                ["u8@0x27"] = $"0x{rom.u8(a + 39):X02}",
                // B40-B47: weapon levels
                ["u8@0x28"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29"] = $"0x{rom.u8(a + 41):X02}",
                ["u8@0x2A"] = $"0x{rom.u8(a + 42):X02}",
                ["u8@0x2B"] = $"0x{rom.u8(a + 43):X02}",
                ["u8@0x2C"] = $"0x{rom.u8(a + 44):X02}",
                ["u8@0x2D"] = $"0x{rom.u8(a + 45):X02}",
                ["u8@0x2E"] = $"0x{rom.u8(a + 46):X02}",
                ["u8@0x2F"] = $"0x{rom.u8(a + 47):X02}",
                // P48-P64: pointers
                ["u32@0x30"] = $"0x{rom.u32(a + 48):X08}",
                ["u32@0x34"] = $"0x{rom.u32(a + 52):X08}",
                ["u32@0x38"] = $"0x{rom.u32(a + 56):X08}",
                ["u32@0x3C"] = $"0x{rom.u32(a + 60):X08}",
                ["u32@0x40"] = $"0x{rom.u32(a + 64):X08}",
                // D68
                ["u32@0x44"] = $"0x{rom.u32(a + 68):X08}",
            };
            return report;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            // Text IDs
            rom.write_u16(addr + 0, W0);
            rom.write_u16(addr + 2, W2);

            // Identity
            rom.write_u8(addr + 4, B4);
            rom.write_u8(addr + 5, B5);
            rom.write_u8(addr + 6, B6);
            rom.write_u8(addr + 7, B7);
            rom.write_u16(addr + 8, W8);
            rom.write_u8(addr + 10, B10);

            // Base stats
            rom.write_u8(addr + 11, B11);
            rom.write_u8(addr + 12, B12);
            rom.write_u8(addr + 13, B13);
            rom.write_u8(addr + 14, B14);
            rom.write_u8(addr + 15, B15);
            rom.write_u8(addr + 16, B16);
            rom.write_u8(addr + 17, B17);
            rom.write_u8(addr + 18, B18);

            // Stat caps
            rom.write_u8(addr + 19, B19);
            rom.write_u8(addr + 20, B20);
            rom.write_u8(addr + 21, B21);
            rom.write_u8(addr + 22, B22);
            rom.write_u8(addr + 23, B23);
            rom.write_u8(addr + 24, B24);
            rom.write_u8(addr + 25, B25);
            rom.write_u8(addr + 26, B26);

            // Growth rates
            rom.write_u8(addr + 27, B27);
            rom.write_u8(addr + 28, B28);
            rom.write_u8(addr + 29, B29);
            rom.write_u8(addr + 30, B30);
            rom.write_u8(addr + 31, B31);
            rom.write_u8(addr + 32, B32);
            rom.write_u8(addr + 33, B33);

            // Promotion bonuses / misc
            rom.write_u8(addr + 34, B34);
            rom.write_u8(addr + 35, B35);

            // Ability flags
            rom.write_u8(addr + 36, B36);
            rom.write_u8(addr + 37, B37);
            rom.write_u8(addr + 38, B38);
            rom.write_u8(addr + 39, B39);

            // Weapon levels
            rom.write_u8(addr + 40, B40);
            rom.write_u8(addr + 41, B41);
            rom.write_u8(addr + 42, B42);
            rom.write_u8(addr + 43, B43);
            rom.write_u8(addr + 44, B44);
            rom.write_u8(addr + 45, B45);
            rom.write_u8(addr + 46, B46);
            rom.write_u8(addr + 47, B47);

            // Pointers
            rom.write_u32(addr + 48, P48);
            rom.write_u32(addr + 52, P52);
            rom.write_u32(addr + 56, P56);
            rom.write_u32(addr + 60, P60);
            rom.write_u32(addr + 64, P64);

            // D68
            rom.write_u32(addr + 68, D68);
        }
    }
}
