using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportUnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _unitName = "";
        List<SupportEntry> _supports = new();
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string UnitName { get => _unitName; set => SetField(ref _unitName, value); }
        public List<SupportEntry> Supports { get => _supports; set => SetField(ref _supports, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public class SupportEntry
        {
            public int Index { get; set; }
            public uint PartnerId { get; set; }
            public string PartnerName { get; set; } = "";
            public uint GrowthRate { get; set; }
        }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            // support_unit_pointer may be a direct address or pointer
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            uint dataSize = 24; // Fixed 24 bytes per entry

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Check if entry is valid (first word non-zero or scan ahead)
                uint firstWord = rom.u16(addr);
                if (firstWord == 0 && i > 0)
                {
                    // Look ahead for more data
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = (uint)(baseAddr + (i + j) * dataSize);
                        if (checkAddr + dataSize > (uint)rom.Data.Length) break;
                        if (rom.u16(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                // Try to figure out the unit name
                string unitName = $"Entry {U.ToHexString(i)}";

                string name = U.ToHexString(i) + " " + unitName;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = 24;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitName = $"Support entry at 0x{addr:X08}";

            // Parse support partner entries (each 4 bytes: unit ID u16 + growth data u16)
            var supports = new List<SupportEntry>();
            for (int i = 0; i < 7; i++) // Max 7 support partners
            {
                uint entryOffset = (uint)(i * 2);
                if (addr + entryOffset + 1 >= (uint)rom.Data.Length) break;

                uint partnerId = rom.u8(addr + entryOffset);
                if (partnerId == 0 && i > 0) break; // End of list

                string partnerName;
                try
                {
                    // Try to resolve unit name via unit_pointer
                    uint unitPtr = rom.RomInfo.unit_pointer;
                    if (unitPtr != 0)
                    {
                        uint unitBase = rom.p32(unitPtr);
                        uint unitDataSize = rom.RomInfo.unit_datasize;
                        if (unitBase != 0 && U.isSafetyOffset(unitBase) && unitDataSize > 0)
                        {
                            uint unitAddr = (uint)(unitBase + partnerId * unitDataSize);
                            if (unitAddr + 2 <= (uint)rom.Data.Length)
                            {
                                uint nameId = rom.u16(unitAddr);
                                partnerName = FETextDecode.Direct(nameId);
                            }
                            else partnerName = "???";
                        }
                        else partnerName = "???";
                    }
                    else partnerName = "???";
                }
                catch { partnerName = "???"; }

                supports.Add(new SupportEntry
                {
                    Index = i,
                    PartnerId = partnerId,
                    PartnerName = partnerName,
                });
            }

            Supports = supports;
            CanWrite = true;
        }

        public int GetListCount() => LoadSupportUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
            };
            foreach (var s in Supports)
            {
                report[$"Partner[{s.Index}].Id"] = $"0x{s.PartnerId:X02}";
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
            // Read raw bytes for partner entries (each 2 bytes)
            for (int i = 0; i < 7; i++)
            {
                uint offset = (uint)(i * 2);
                if (a + offset + 1 >= (uint)rom.Data.Length) break;
                report[$"u8@0x{offset:X02}"] = $"0x{rom.u8(a + offset):X02}";
            }
            return report;
        }
    }
}
