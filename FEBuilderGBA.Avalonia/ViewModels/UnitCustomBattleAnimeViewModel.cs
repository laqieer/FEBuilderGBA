using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitCustomBattleAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _weaponType;
        uint _special;
        uint _animeNumber;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        public uint Special { get => _special; set => SetField(ref _special, value); }
        public uint AnimeNumber { get => _animeNumber; set => SetField(ref _animeNumber, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Custom Battle Animation", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            WeaponType = rom.u8(addr + 0);
            Special = rom.u8(addr + 1);
            AnimeNumber = rom.u16(addr + 2);
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 4 > (uint)rom.Data.Length) return;

            rom.write_u8(addr + 0, (byte)WeaponType);
            rom.write_u8(addr + 1, (byte)Special);
            rom.write_u16(addr + 2, (ushort)AnimeNumber);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0_WeaponType"] = $"0x{WeaponType:X02}",
                ["B1_Special"] = $"0x{Special:X02}",
                ["W2_AnimeNumber"] = $"0x{AnimeNumber:X04}",
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
                ["u8@0x00_WeaponType"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Special"] = $"0x{rom.u8(a + 1):X02}",
                ["u16@0x02_AnimeNumber"] = $"0x{rom.u16(a + 2):X04}",
            };
        }
    }
}
