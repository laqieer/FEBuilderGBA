using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ArenaEnemyWeaponViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _weaponId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint WeaponId { get => _weaponId; set => SetField(ref _weaponId, value); }

        public List<AddrResult> LoadArenaEnemyWeaponList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.arena_enemy_weapon_basic_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 8; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint weaponId = rom.u8(addr);
                string name = U.ToHexString(i) + " Arena Enemy Weapon 0x" + weaponId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadArenaEnemyWeapon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            WeaponId = rom.u8(addr);
            IsLoaded = true;
        }
    }
}
