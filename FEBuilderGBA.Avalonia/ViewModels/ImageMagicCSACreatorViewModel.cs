using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageMagicCSACreatorViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 20;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0, _p4, _p8, _p12, _p16;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // P0: CSA magic data pointer
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        // P4: Frame data pointer
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        // P8: Palette pointer
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        // P12: OAM data pointer
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        // P16: Extra data pointer
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "CSA Magic Creator", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            P0 = rom.u32(addr + 0);
            P4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);
            P12 = rom.u32(addr + 12);
            P16 = rom.u32(addr + 16);

            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["P4"] = $"0x{P4:X08}",
                ["P8"] = $"0x{P8:X08}",
                ["P12"] = $"0x{P12:X08}",
                ["P16"] = $"0x{P16:X08}",
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
            };
        }
    }
}
