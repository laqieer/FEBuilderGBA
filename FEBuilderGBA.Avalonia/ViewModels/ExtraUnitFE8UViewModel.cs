using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ExtraUnitFE8UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _flagId;
        uint _unitInfoPtr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint FlagId { get => _flagId; set => SetField(ref _flagId, value); }
        public uint UnitInfoPtr { get => _unitInfoPtr; set => SetField(ref _unitInfoPtr, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Extra Unit (FE8U)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            FlagId = rom.u32(addr + 0);
            UnitInfoPtr = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, FlagId);
            rom.write_u32(addr + 4, UnitInfoPtr);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0_FlagId"] = $"0x{FlagId:X08}",
                ["P4_UnitInfoPtr"] = $"0x{UnitInfoPtr:X08}",
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
                ["u32@0x00_FlagId"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_UnitInfoPtr"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
