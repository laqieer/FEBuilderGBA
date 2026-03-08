using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ArenaClassViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _classId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }

        public List<AddrResult> LoadArenaClassList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.arena_class_near_weapon_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint classId = rom.u8(addr);
                if (classId == 0x00) break;

                string name = U.ToHexString(i) + " Arena Class 0x" + classId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadArenaClass(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ClassId = rom.u8(addr);
            CanWrite = true;
        }

        public void WriteArenaClass()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr, ClassId);
        }

        public int GetListCount() => LoadArenaClassList().Count;

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
