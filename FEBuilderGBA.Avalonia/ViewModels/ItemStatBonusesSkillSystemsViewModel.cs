using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemStatBonusesSkillSystemsViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        int _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7;
        int _b8, _b9, _b10, _b11, _b12, _b13, _b14, _b15;
        int _b16, _b17, _b18, _b19;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public int b0 { get => _b0; set => SetField(ref _b0, value); }
        public int b1 { get => _b1; set => SetField(ref _b1, value); }
        public int b2 { get => _b2; set => SetField(ref _b2, value); }
        public int b3 { get => _b3; set => SetField(ref _b3, value); }
        public int b4 { get => _b4; set => SetField(ref _b4, value); }
        public int b5 { get => _b5; set => SetField(ref _b5, value); }
        public int b6 { get => _b6; set => SetField(ref _b6, value); }
        public int b7 { get => _b7; set => SetField(ref _b7, value); }
        public int b8 { get => _b8; set => SetField(ref _b8, value); }
        public int b9 { get => _b9; set => SetField(ref _b9, value); }
        public int b10 { get => _b10; set => SetField(ref _b10, value); }
        public int b11 { get => _b11; set => SetField(ref _b11, value); }
        public int b12 { get => _b12; set => SetField(ref _b12, value); }
        public int b13 { get => _b13; set => SetField(ref _b13, value); }
        public int b14 { get => _b14; set => SetField(ref _b14, value); }
        public int b15 { get => _b15; set => SetField(ref _b15, value); }
        public int b16 { get => _b16; set => SetField(ref _b16, value); }
        public int b17 { get => _b17; set => SetField(ref _b17, value); }
        public int b18 { get => _b18; set => SetField(ref _b18, value); }
        public int b19 { get => _b19; set => SetField(ref _b19, value); }

        // Unsigned accessors at same offsets for coverage
        public uint B0 => (uint)((byte)b0);
        public uint B1 => (uint)((byte)b1);
        public uint B2 => (uint)((byte)b2);
        public uint B3 => (uint)((byte)b3);
        public uint B4 => (uint)((byte)b4);
        public uint B5 => (uint)((byte)b5);
        public uint B6 => (uint)((byte)b6);
        public uint B7 => (uint)((byte)b7);
        public uint B8 => (uint)((byte)b8);
        public uint B9 => (uint)((byte)b9);
        public uint B10 => (uint)((byte)b10);
        public uint B11 => (uint)((byte)b11);
        public uint B12 => (uint)((byte)b12);
        public uint B13 => (uint)((byte)b13);
        public uint B14 => (uint)((byte)b14);
        public uint B15 => (uint)((byte)b15);
        public uint B16 => (uint)((byte)b16);
        public uint B17 => (uint)((byte)b17);
        public uint B18 => (uint)((byte)b18);
        public uint B19 => (uint)((byte)b19);

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Stat Bonuses (Skill Systems)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 19 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            b0 = (int)(sbyte)rom.u8(addr + 0);
            b1 = (int)(sbyte)rom.u8(addr + 1);
            b2 = (int)(sbyte)rom.u8(addr + 2);
            b3 = (int)(sbyte)rom.u8(addr + 3);
            b4 = (int)(sbyte)rom.u8(addr + 4);
            b5 = (int)(sbyte)rom.u8(addr + 5);
            b6 = (int)(sbyte)rom.u8(addr + 6);
            b7 = (int)(sbyte)rom.u8(addr + 7);
            b8 = (int)(sbyte)rom.u8(addr + 8);
            b9 = (int)(sbyte)rom.u8(addr + 9);
            b10 = (int)(sbyte)rom.u8(addr + 10);
            b11 = (int)(sbyte)rom.u8(addr + 11);
            b12 = (int)(sbyte)rom.u8(addr + 12);
            b13 = (int)(sbyte)rom.u8(addr + 13);
            b14 = (int)(sbyte)rom.u8(addr + 14);
            b15 = (int)(sbyte)rom.u8(addr + 15);
            b16 = (int)(sbyte)rom.u8(addr + 16);
            b17 = (int)(sbyte)rom.u8(addr + 17);
            b18 = (int)(sbyte)rom.u8(addr + 18);
            b19 = (int)(sbyte)rom.u8(addr + 19);

            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["b0"] = $"0x{B0:X02}",
                ["b1"] = $"0x{B1:X02}",
                ["b2"] = $"0x{B2:X02}",
                ["b3"] = $"0x{B3:X02}",
                ["b4"] = $"0x{B4:X02}",
                ["b5"] = $"0x{B5:X02}",
                ["b6"] = $"0x{B6:X02}",
                ["b7"] = $"0x{B7:X02}",
                ["b8"] = $"0x{B8:X02}",
                ["b9"] = $"0x{B9:X02}",
                ["b10"] = $"0x{B10:X02}",
                ["b11"] = $"0x{B11:X02}",
                ["b12"] = $"0x{B12:X02}",
                ["b13"] = $"0x{B13:X02}",
                ["b14"] = $"0x{B14:X02}",
                ["b15"] = $"0x{B15:X02}",
                ["b16"] = $"0x{B16:X02}",
                ["b17"] = $"0x{B17:X02}",
                ["b18"] = $"0x{B18:X02}",
                ["b19"] = $"0x{B19:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
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
            };
        }
    }
}
