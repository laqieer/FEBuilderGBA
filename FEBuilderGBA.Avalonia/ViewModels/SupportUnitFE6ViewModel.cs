using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportUnitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint[] _partners = Array.Empty<uint>();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint[] Partners { get => _partners; set => SetField(ref _partners, value); }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            uint dataSize = 32;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint first = rom.u8(addr);
                if (first == 0 && i > 0)
                {
                    // Sparse look-ahead: check next 4 entries
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = (uint)(baseAddr + (i + j) * dataSize);
                        if (checkAddr + dataSize > (uint)rom.Data.Length) break;
                        if (rom.u8(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                string name = U.ToHexString(i) + " Support Unit Entry";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Read 10 support partner unit IDs (B0-B9, u8 each)
            var partners = new uint[10];
            for (int i = 0; i < 10; i++)
                partners[i] = rom.u8(addr + (uint)i);

            Partners = partners;
            IsLoaded = true;
        }

        public int GetListCount() => LoadSupportUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
            };
            for (int i = 0; i < Partners.Length; i++)
            {
                report[$"Partner[{i}]"] = $"0x{Partners[i]:X02}";
            }
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
            };
            for (int i = 0; i < 10; i++)
            {
                report[$"u8@0x{i:X02}"] = $"0x{rom.u8(a + (uint)i):X02}";
            }
            return report;
        }
    }
}
