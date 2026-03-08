using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuDefinitionViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _b0, _b1, _b2, _b3;
        uint _d4;
        uint _handlerPtr;
        uint _p12, _p16, _p20, _p24, _p28, _p32;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        public uint TextId => (uint)(D4 & 0xFFFF); // legacy alias: u16@4
        public uint HandlerPtr { get => _handlerPtr; set => SetField(ref _handlerPtr, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }
        public uint P20 { get => _p20; set => SetField(ref _p20, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }
        public uint P28 { get => _p28; set => SetField(ref _p28, value); }
        public uint P32 { get => _p32; set => SetField(ref _p32, value); }

        public List<AddrResult> LoadMenuDefinitionList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 36);
                if (addr + 36 > (uint)rom.Data.Length) break;

                // Termination: offset+8 must be a pointer
                if (!U.isPointer(rom.u32(addr + 8))) break;

                string name = U.ToHexString(i) + " Menu Definition";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMenuDefinition(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 36 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            D4 = rom.u32(addr + 4);
            HandlerPtr = rom.u32(addr + 8);
            P12 = rom.u32(addr + 12);
            P16 = rom.u32(addr + 16);
            P20 = rom.u32(addr + 20);
            P24 = rom.u32(addr + 24);
            P28 = rom.u32(addr + 28);
            P32 = rom.u32(addr + 32);
            CanWrite = true;
        }

        public void WriteMenuDefinition()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 36 > (uint)rom.Data.Length) return;

            rom.write_u8(addr + 0, (byte)B0);
            rom.write_u8(addr + 1, (byte)B1);
            rom.write_u8(addr + 2, (byte)B2);
            rom.write_u8(addr + 3, (byte)B3);
            rom.write_u32(addr + 4, D4);
            rom.write_u32(addr + 8, HandlerPtr);
            rom.write_u32(addr + 12, P12);
            rom.write_u32(addr + 16, P16);
            rom.write_u32(addr + 20, P20);
            rom.write_u32(addr + 24, P24);
            rom.write_u32(addr + 28, P28);
            rom.write_u32(addr + 32, P32);
        }

        public int GetListCount() => LoadMenuDefinitionList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["D4"] = $"0x{D4:X08}",
                ["HandlerPtr"] = $"0x{HandlerPtr:X08}",
                ["P12"] = $"0x{P12:X08}",
                ["P16"] = $"0x{P16:X08}",
                ["P20"] = $"0x{P20:X08}",
                ["P24"] = $"0x{P24:X08}",
                ["P28"] = $"0x{P28:X08}",
                ["P32"] = $"0x{P32:X08}",
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
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20"] = $"0x{rom.u32(a + 32):X08}",
            };
        }
    }
}
