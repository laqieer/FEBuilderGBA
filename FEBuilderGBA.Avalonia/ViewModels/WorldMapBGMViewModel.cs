using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapBGMViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _songId1;
        uint _songId2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
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

                string songName1 = NameResolver.GetSongName(songId1);
                string name = $"{U.ToHexString(i)} {songName1}";
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
            CanWrite = true;
        }

        public void WriteWorldMapBGM()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            rom.write_u16(CurrentAddr + 0, (ushort)SongId1);
            rom.write_u16(CurrentAddr + 2, (ushort)SongId2);
        }

        public int GetListCount() => LoadWorldMapBGMList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SongId1"] = $"0x{SongId1:X04}",
                ["SongId2"] = $"0x{SongId2:X04}",
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
            };
        }
    }
}
