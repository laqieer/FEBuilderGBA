using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDStaffRollViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _dataPointer;
        uint _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint DataPointer { get => _dataPointer; set => SetField(ref _dataPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadEDStaffRollList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ed_staffroll_image_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 12; i++) // Staff roll is limited to ~12 entries
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " Staff Roll";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEDStaffRoll(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataPointer = rom.u32(addr);
            PalettePointer = rom.u32(addr + 4);
            CanWrite = true;
        }

        public void WriteEDStaffRoll()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 8 > (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr, DataPointer);
            rom.write_u32(CurrentAddr + 4, PalettePointer);
        }

        public int GetListCount() => LoadEDStaffRollList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["DataPointer"] = $"0x{DataPointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
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
            };
        }
    }
}
