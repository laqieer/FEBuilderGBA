using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Tactician affinity editor (FE7 only).
    /// Record size: 4 bytes. D0 = affinity ID (u32@0).
    /// B4 = linked display field (index+1), read as u8@4 for completeness.
    /// </summary>
    public class TacticianAffinityFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _d0;
        byte _b4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        public byte B4 { get => _b4; set => SetField(ref _b4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.tactician_affinity_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            int maxCount = rom.RomInfo.is_multibyte ? 48 : 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint affinityId = rom.u32(addr + 0);
                string name = U.ToHexString((uint)i) + " " + U.ToHexString(affinityId);
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 5 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            D0 = rom.u32(addr + 0);
            B4 = (byte)rom.u8(addr + 4);
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
            };
        }
    }
}
