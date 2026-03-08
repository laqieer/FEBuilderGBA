using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 28;

        uint _currentAddr;
        bool _isLoaded;
        uint _d0, _d4, _d8, _d12, _d16;
        uint _b20, _b21, _b22, _b23, _b24, _b25, _b26, _b27;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Portrait image pointer
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        // D4: Mini portrait pointer
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        // D8: Palette pointer
        public uint D8 { get => _d8; set => SetField(ref _d8, value); }
        // D12: Mouth frames pointer
        public uint D12 { get => _d12; set => SetField(ref _d12, value); }
        // D16: Eye frames pointer
        public uint D16 { get => _d16; set => SetField(ref _d16, value); }
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

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Portrait Image Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            D0 = rom.u32(addr + 0);
            D4 = rom.u32(addr + 4);
            D8 = rom.u32(addr + 8);
            D12 = rom.u32(addr + 12);
            D16 = rom.u32(addr + 16);
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            B24 = rom.u8(addr + 24);
            B25 = rom.u8(addr + 25);
            B26 = rom.u8(addr + 26);
            B27 = rom.u8(addr + 27);

            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0"] = $"0x{D0:X08}",
                ["D4"] = $"0x{D4:X08}",
                ["D8"] = $"0x{D8:X08}",
                ["D12"] = $"0x{D12:X08}",
                ["D16"] = $"0x{D16:X08}",
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["B24"] = $"0x{B24:X02}",
                ["B25"] = $"0x{B25:X02}",
                ["B26"] = $"0x{B26:X02}",
                ["B27"] = $"0x{B27:X02}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@16"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@20"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@21"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@22"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@23"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@24"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@25"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@26"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@27"] = $"0x{rom.u8(a + 27):X02}",
            };
        }
    }
}
