using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapBGMViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _songId1;
        uint _songId2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SongId1 { get => _songId1; set => SetField(ref _songId1, value); }
        public uint SongId2 { get => _songId2; set => SetField(ref _songId2, value); }

        public List<AddrResult> LoadWorldMapBGMList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_bgm_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint songId1 = rom.u16(addr + 0);
                uint songId2 = rom.u16(addr + 2);
                // Termination condition from WinForms
                if (songId1 == 1 && songId2 == 0) break;
                if (songId1 == 0 && songId2 == 0) break;

                string name = U.ToHexString(i) + " World Map BGM";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadWorldMapBGM(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SongId1 = rom.u16(addr + 0);
            SongId2 = rom.u16(addr + 2);
            IsLoaded = true;
        }
    }
}
