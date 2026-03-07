using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7U variant).
    /// Data: op_class_demo_pointer, datasize=28, classId at offset 11.
    /// </summary>
    public class OPClassDemoFE7UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _unavailableMessage = "";
        uint _p0;
        uint _w4;
        uint _w8;
        uint _b10;
        uint _b11;
        uint _b12;
        uint _b13;
        uint _b14;
        uint _b15;
        uint _b16;
        uint _b17;
        uint _b19;
        uint _b20;
        uint _b21;
        uint _b22;
        uint _b23;
        uint _p24;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }
        public uint W8 { get => _w8; set => SetField(ref _w8, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }

        // Backward-compatible aliases used by the View
        public uint ClassId => B11;
        public uint AnimationType => B12;
        public uint BattleAnime => B13;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_demo_pointer;
            if (baseAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                IsLoaded = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                IsLoaded = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // datasize=28, up to 0x42 entries
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                uint cid = rom.u8(addr + 11);
                string name = U.ToHexString(cid) + " Class Demo (FE7U)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);
            W4 = rom.u16(addr + 4);
            W8 = rom.u16(addr + 8);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B19 = rom.u8(addr + 19);
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            P24 = rom.u32(addr + 24);
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["W4"] = $"0x{W4:X04}",
                ["W8"] = $"0x{W8:X04}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["B19"] = $"0x{B19:X02}",
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
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
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18"] = $"0x{rom.u32(a + 24):X08}",
            };
        }
    }
}
