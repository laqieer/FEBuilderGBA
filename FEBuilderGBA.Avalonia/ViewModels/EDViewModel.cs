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
        uint _condition;
        uint _unknown2, _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint Condition { get => _condition; set => SetField(ref _condition, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

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
                string unitName = NameResolver.GetUnitName(uid);
                string name = $"{U.ToHexString(i)} {unitName}";
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
            Condition = rom.u8(addr + 1); // B1 - 00=died, 01=wounded/left, 02=wounded/stayed
            Unknown2 = rom.u8(addr + 2);  // B2
            Unknown3 = rom.u8(addr + 3);  // B3
            CanWrite = true;
        }

        public void WriteED()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr, (byte)UnitId);
            rom.write_u8(CurrentAddr + 1, (byte)Condition);
            rom.write_u8(CurrentAddr + 2, (byte)Unknown2);
            rom.write_u8(CurrentAddr + 3, (byte)Unknown3);
        }

        public int GetListCount() => LoadEDList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["Condition"] = $"0x{Condition:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
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
                ["u8@0x00_UnitId"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Condition"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown3"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
