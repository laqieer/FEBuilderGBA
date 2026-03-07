using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapPointViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _b0, _b1, _b2, _b3, _b4, _b5;
        uint _w6;
        uint _b8, _b9, _b10, _b11;
        uint _d12, _d16, _d20;
        uint _w24, _w26;
        uint _nameTextId;
        uint _b30, _b31;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint W6 { get => _w6; set => SetField(ref _w6, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint D12 { get => _d12; set => SetField(ref _d12, value); }
        public uint D16 { get => _d16; set => SetField(ref _d16, value); }
        public uint D20 { get => _d20; set => SetField(ref _d20, value); }
        public uint W24 { get => _w24; set => SetField(ref _w24, value); }
        public uint W26 { get => _w26; set => SetField(ref _w26, value); }
        public uint NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }
        public uint B30 { get => _b30; set => SetField(ref _b30, value); }
        public uint B31 { get => _b31; set => SetField(ref _b31, value); }
        // Legacy aliases
        public uint X => B0 | (B1 << 8);
        public uint Y => B2 | (B3 << 8);

        public List<AddrResult> LoadWorldMapPointList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                // Termination: pointers at +12, +16, +20 must be pointer-or-null
                if (!U.isPointerOrNULL(rom.u32(addr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 16))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 20))) break;

                string name = U.ToHexString(i) + " World Map Point";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadWorldMapPoint(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            W6 = rom.u16(addr + 6);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            D12 = rom.u32(addr + 12);
            D16 = rom.u32(addr + 16);
            D20 = rom.u32(addr + 20);
            W24 = rom.u16(addr + 24);
            W26 = rom.u16(addr + 26);
            NameTextId = rom.u16(addr + 28);
            B30 = rom.u8(addr + 30);
            B31 = rom.u8(addr + 31);
            IsLoaded = true;
        }

        public int GetListCount() => LoadWorldMapPointList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["W6"] = $"0x{W6:X04}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["D12"] = $"0x{D12:X08}",
                ["D16"] = $"0x{D16:X08}",
                ["D20"] = $"0x{D20:X08}",
                ["W24"] = $"0x{W24:X04}",
                ["W26"] = $"0x{W26:X04}",
                ["NameTextId"] = $"0x{NameTextId:X04}",
                ["B30"] = $"0x{B30:X02}",
                ["B31"] = $"0x{B31:X02}",
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
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
                ["u16@0x18"] = $"0x{rom.u16(a + 24):X04}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u16@0x1C"] = $"0x{rom.u16(a + 28):X04}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
            };
        }
    }
}
