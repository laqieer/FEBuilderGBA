using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterWMapProbabilityViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _basePointId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint BasePointId { get => _basePointId; set => SetField(ref _basePointId, value); }

        public List<AddrResult> LoadMonsterWMapProbabilityList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_wmap_base_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 9; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint basePointId = rom.u8(addr);
                string name = U.ToHexString(i) + " WMap Monster 0x" + basePointId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterWMapProbability(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            BasePointId = rom.u8(addr);
            CanWrite = true;
        }

        public void WriteMonsterWMapProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr >= (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr, (byte)BasePointId);
        }

        public int GetListCount() => LoadMonsterWMapProbabilityList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BasePointId"] = $"0x{BasePointId:X02}",
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
