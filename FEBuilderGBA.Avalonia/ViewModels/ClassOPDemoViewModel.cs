using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassOPDemoViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        uint _d4;
        uint _p8;
        uint _b12;
        uint _b13;
        uint _b14;
        uint _b15;
        uint _b16;
        uint _b17;
        uint _d18;
        uint _b22;
        uint _b23;
        uint _p24;
        uint _n1B0;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint D18 { get => _d18; set => SetField(ref _d18, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }
        public uint N1B0 { get => _n1B0; set => SetField(ref _n1B0, value); }

        public List<AddrResult> LoadClassOPDemoList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_demo_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                if (!U.isPointer(rom.u32(addr))) break;

                uint cid = rom.u8(addr + 14);
                string name = U.ToHexString(i) + " " + U.ToHexString(cid);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadClassOPDemo(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);
            D4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            D18 = rom.u32(addr + 18);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            P24 = rom.u32(addr + 24);

            // N1 sub-structure: pointed to by P8, 1 byte per entry
            if (U.isPointer(P8))
            {
                uint n1Addr = U.toOffset(P8);
                if (n1Addr < (uint)rom.Data.Length)
                    N1B0 = rom.u8(n1Addr);
            }

            IsLoaded = true;
        }

        public int GetListCount() => LoadClassOPDemoList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["D4"] = $"0x{D4:X08}",
                ["P8"] = $"0x{P8:X08}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["D18"] = $"0x{D18:X08}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["P24"] = $"0x{P24:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u32@0x12"] = $"0x{rom.u32(a + 18):X08}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18"] = $"0x{rom.u32(a + 24):X08}",
            };
        }
    }
}
