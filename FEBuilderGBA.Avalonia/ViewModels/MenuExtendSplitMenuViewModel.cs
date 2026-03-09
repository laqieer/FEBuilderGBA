using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuExtendSplitMenuViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _posX, _posY, _width, _style;
        uint _str1, _str2, _str3, _str4, _str5, _str6, _str7, _str8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public uint Style { get => _style; set => SetField(ref _style, value); }
        public uint String0 { get => _str1; set => SetField(ref _str1, value); }
        public uint String1 { get => _str2; set => SetField(ref _str2, value); }
        public uint String2 { get => _str3; set => SetField(ref _str3, value); }
        public uint String3 { get => _str4; set => SetField(ref _str4, value); }
        public uint String4 { get => _str5; set => SetField(ref _str5, value); }
        public uint String5 { get => _str6; set => SetField(ref _str6, value); }
        public uint String6 { get => _str7; set => SetField(ref _str7, value); }
        public uint String7 { get => _str8; set => SetField(ref _str8, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Menu Extend Split", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 40 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            PosX = rom.u8(addr + 0);
            PosY = rom.u8(addr + 1);
            Width = rom.u8(addr + 2);
            // byte 3 is padding
            Style = rom.u32(addr + 4);
            String0 = rom.u32(addr + 8);
            String1 = rom.u32(addr + 12);
            String2 = rom.u32(addr + 16);
            String3 = rom.u32(addr + 20);
            String4 = rom.u32(addr + 24);
            String5 = rom.u32(addr + 28);
            String6 = rom.u32(addr + 32);
            String7 = rom.u32(addr + 36);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 40 > (uint)rom.Data.Length) return;

            rom.write_u8(addr + 0, (byte)PosX);
            rom.write_u8(addr + 1, (byte)PosY);
            rom.write_u8(addr + 2, (byte)Width);
            rom.write_u32(addr + 4, Style);
            rom.write_u32(addr + 8, String0);
            rom.write_u32(addr + 12, String1);
            rom.write_u32(addr + 16, String2);
            rom.write_u32(addr + 20, String3);
            rom.write_u32(addr + 24, String4);
            rom.write_u32(addr + 28, String5);
            rom.write_u32(addr + 32, String6);
            rom.write_u32(addr + 36, String7);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["Width"] = $"0x{Width:X02}",
                ["Style"] = $"0x{Style:X08}",
                ["String0"] = $"0x{String0:X08}",
                ["String1"] = $"0x{String1:X08}",
                ["String2"] = $"0x{String2:X08}",
                ["String3"] = $"0x{String3:X08}",
                ["String4"] = $"0x{String4:X08}",
                ["String5"] = $"0x{String5:X08}",
                ["String6"] = $"0x{String6:X08}",
                ["String7"] = $"0x{String7:X08}",
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
                ["u8@0x00_PosX"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_PosY"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Width"] = $"0x{rom.u8(a + 2):X02}",
                ["u32@0x04_Style"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_String0"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_String1"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10_String2"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14_String3"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_String4"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C_String5"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20_String6"] = $"0x{rom.u32(a + 32):X08}",
                ["u32@0x24_String7"] = $"0x{rom.u32(a + 36):X08}",
            };
        }
    }
}
