using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundBossBGMViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitId;
        uint _unknown1, _unknown2, _unknown3;
        uint _songId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }
        public uint SongId { get => _songId; set => SetField(ref _songId, value); }

        public List<AddrResult> LoadSoundBossBGMList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_boss_bgm_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 8 > (uint)rom.Data.Length) break;

                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, 8 * 10)) break;

                uint unitId = rom.u8(addr);
                uint songId = rom.u32(addr + 4);
                string name = U.ToHexString(unitId) + " Boss BGM Song:0x" + songId.ToString("X08");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSoundBossBGM(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr + 0);
            Unknown1 = rom.u8(addr + 1);
            Unknown2 = rom.u8(addr + 2);
            Unknown3 = rom.u8(addr + 3);
            SongId = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public int GetListCount() => LoadSoundBossBGMList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
                ["SongId"] = $"0x{SongId:X08}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
