using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE8U variant).
    /// Data: op_class_demo_pointer, datasize=20, classId at offset 5.
    /// Validation: u8(addr+0x0F) must be &lt;= 6.
    /// </summary>
    public class OPClassDemoFE8UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _unavailableMessage = "";
        uint _d0;
        uint _b4;
        uint _b5;
        uint _b6;
        uint _b7;
        uint _w8;
        uint _w10;
        uint _b12;
        uint _b13;
        uint _b14;
        uint _b15;
        uint _p16;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint W8 { get => _w8; set => SetField(ref _w8, value); }
        public uint W10 { get => _w10; set => SetField(ref _w10, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }

        // Backward-compatible aliases used by the View
        public uint ClassId => B5;
        public uint AnimationType => B6;
        public uint BattleAnime => B7;

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
            // datasize=20, validate u8(addr+0xF) <= 6
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                uint animCheck = rom.u8(addr + 0x0F);
                if (animCheck > 6) break;

                uint cid = rom.u8(addr + 5);
                string name = U.ToHexString(cid) + " Class Demo (FE8U)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            D0 = rom.u32(addr + 0);
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            W8 = rom.u16(addr + 8);
            W10 = rom.u16(addr + 10);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            P16 = rom.u32(addr + 16);
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0"] = $"0x{D0:X08}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["W8"] = $"0x{W8:X04}",
                ["W10"] = $"0x{W10:X04}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u16@0x0A"] = $"0x{rom.u16(a + 10):X04}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
            };
        }
    }
}
