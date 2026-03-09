using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageBattleAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 4;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _weaponType, _special, _animationNumber;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // B0: Weapon type index
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        // B1: Special flag
        public uint Special { get => _special; set => SetField(ref _special, value); }
        // W2: Animation number
        public uint AnimationNumber { get => _animationNumber; set => SetField(ref _animationNumber, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Battle Animation Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            WeaponType = rom.u8(addr + 0);
            Special = rom.u8(addr + 1);
            AnimationNumber = rom.u16(addr + 2);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, WeaponType);
            rom.write_u8(addr + 1, Special);
            rom.write_u16(addr + 2, AnimationNumber);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["WeaponType"] = $"0x{WeaponType:X02}",
                ["Special"] = $"0x{Special:X02}",
                ["AnimationNumber"] = $"0x{AnimationNumber:X04}",
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
                ["u8@0"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1"] = $"0x{rom.u8(a + 1):X02}",
                ["u16@2"] = $"0x{rom.u16(a + 2):X04}",
            };
        }
    }
}
