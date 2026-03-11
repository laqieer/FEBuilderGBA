using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventForceSortieFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitListPointer;

        // Sub-entry fields (N_ prefix in WinForms)
        uint _subAddr;
        uint _unitId;
        uint _unknown1;
        uint _unknown2;
        uint _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: Pointer to unit list (u32 at offset 0)
        public uint UnitListPointer { get => _unitListPointer; set => SetField(ref _unitListPointer, value); }

        // Sub-entry fields (inner list pointed to by UnitListPointer)
        public uint SubAddr { get => _subAddr; set => SetField(ref _subAddr, value); }
        // N_B0: Unit ID (u8 at offset 0)
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        // N_B1: Unknown (u8 at offset 1)
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        // N_B2: Unknown (u8 at offset 2)
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        // N_B3: Unknown (u8 at offset 3)
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_force_sortie_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            // FE7: 23 entries starting from Dragon's Gate (map 0x17)
            for (int i = 0; i < 23; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint mapId = (uint)i + 0x17;
                result.Add(new AddrResult(addr, $"0x{i:X2} Map 0x{mapId:X2}", (uint)i));
            }
            result.Add(new AddrResult(0, "Force Sortie (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitListPointer = rom.u32(addr + 0);
            IsLoaded = true;
        }

        public void LoadSubEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            SubAddr = addr;
            UnitId = rom.u8(addr + 0);
            Unknown1 = rom.u8(addr + 1);
            Unknown2 = rom.u8(addr + 2);
            Unknown3 = rom.u8(addr + 3);
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            rom.write_u32(a + 0, UnitListPointer);
        }

        public void WriteSubEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SubAddr == 0) return;
            uint a = SubAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            rom.write_u8(a + 0, UnitId);
            rom.write_u8(a + 1, Unknown1);
            rom.write_u8(a + 2, Unknown2);
            rom.write_u8(a + 3, Unknown3);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["UnitListPointer"] = UnitListPointer.ToString("X08"),
                ["UnitId"] = UnitId.ToString("X02"),
                ["Unknown1"] = Unknown1.ToString("X02"),
                ["Unknown2"] = Unknown2.ToString("X02"),
                ["Unknown3"] = Unknown3.ToString("X02"),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00_UnitListPointer"] = $"0x{rom.u32(a + 0):X08}",
            };

            if (SubAddr != 0 && SubAddr + 4 <= (uint)rom.Data.Length)
            {
                uint s = SubAddr;
                report["u8@0x00_UnitId"] = $"0x{rom.u8(s + 0):X02}";
                report["u8@0x01_Unknown1"] = $"0x{rom.u8(s + 1):X02}";
                report["u8@0x02_Unknown2"] = $"0x{rom.u8(s + 2):X02}";
                report["u8@0x03_Unknown3"] = $"0x{rom.u8(s + 3):X02}";
            }

            return report;
        }
    }
}
