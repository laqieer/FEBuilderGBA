using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventForceSortieViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _squad;
        uint _chapterId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // W0: Unit ID (u16 at offset 0)
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        // B2: Squad number (u8 at offset 2)
        public uint Squad { get => _squad; set => SetField(ref _squad, value); }
        // B3: Chapter ID / Map ID (u8 at offset 3)
        public uint ChapterId { get => _chapterId; set => SetField(ref _chapterId, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Force Sortie", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Unit = rom.u16(addr + 0);
            Squad = rom.u8(addr + 2);
            ChapterId = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            rom.write_u16(a + 0, Unit);
            rom.write_u8(a + 2, Squad);
            rom.write_u8(a + 3, ChapterId);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Unit"] = Unit.ToString("X04"),
                ["Squad"] = Squad.ToString("X02"),
                ["ChapterId"] = ChapterId.ToString("X02"),
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
                ["u16@0x00_Unit"] = $"0x{rom.u16(a + 0):X04}",
                ["u8@0x02_Squad"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_ChapterId"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
