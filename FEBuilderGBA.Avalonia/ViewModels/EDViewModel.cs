using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _unitId;
        uint _flag;
        uint _b2, _b3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint Flag { get => _flag; set => SetField(ref _flag, value); }
        // B2, B3: Additional bytes
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }

        public List<AddrResult> LoadEDList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ed_1_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                // Termination: all zeros
                if (rom.u32(addr) == 0x00) break;

                uint uid = rom.u8(addr);
                string name = U.ToHexString(i) + " Unit 0x" + uid.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadED(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr);        // B0
            Flag = rom.u8(addr + 1);      // B1
            B2 = rom.u8(addr + 2);        // B2
            B3 = rom.u8(addr + 3);        // B3
            CanWrite = true;
        }

        public void WriteED()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr, (byte)UnitId);
            rom.write_u8(CurrentAddr + 1, (byte)Flag);
            rom.write_u8(CurrentAddr + 2, (byte)B2);
            rom.write_u8(CurrentAddr + 3, (byte)B3);
        }

        public int GetListCount() => LoadEDList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["Flag"] = $"0x{Flag:X02}",
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
            };
        }
    }
}
