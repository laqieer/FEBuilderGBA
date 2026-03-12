using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEffectivenessViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _classId;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadItemEffectivenessList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.weapon_effectiveness_2x3x_address;
            if (baseAddr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;

                uint classId = rom.u8(addr);
                if (classId == 0) break;

                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(i)} {className} (0x{classId:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemEffectiveness(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ClassId = rom.u8(addr);

            CanWrite = true;
        }

        public void WriteItemEffectiveness()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr, ClassId);
        }

        public int GetListCount() => LoadItemEffectivenessList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ClassId"] = $"0x{ClassId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }
    }
}
