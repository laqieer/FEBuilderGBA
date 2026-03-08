using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundRoomViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _songId;
        uint _textId;
        uint _raw4, _raw8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SongId { get => _songId; set => SetField(ref _songId, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint Raw4 { get => _raw4; set => SetField(ref _raw4, value); }
        public uint Raw8 { get => _raw8; set => SetField(ref _raw8, value); }

        public List<AddrResult> LoadSoundRoomList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_room_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (rom.u32(addr) == 0xFFFFFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, dataSize * 10)) break;

                uint songId = rom.u16(addr);
                string name = (i + 1).ToString("D3") + " Song:0x" + songId.ToString("X04");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSoundRoom(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SongId = rom.u32(addr + 0);      // D0
            Raw4 = rom.u32(addr + 4);        // D4
            Raw8 = rom.u32(addr + 8);        // P8
            if (dataSize >= 16)
            {
                TextId = rom.u32(addr + 12); // D12
            }
            else
            {
                TextId = 0;
            }
            CanWrite = true;
        }

        public void WriteSoundRoom()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (CurrentAddr + dataSize > (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr + 0, SongId);
            rom.write_u32(CurrentAddr + 4, Raw4);
            rom.write_u32(CurrentAddr + 8, Raw8);
            if (dataSize >= 16)
            {
                rom.write_u32(CurrentAddr + 12, TextId);
            }
        }

        public int GetListCount() => LoadSoundRoomList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SongId"] = $"0x{SongId:X04}",
                ["Raw4"] = $"0x{Raw4:X08}",
                ["Raw8"] = $"0x{Raw8:X08}",
                ["TextId"] = $"0x{TextId:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
            if (dataSize >= 16)
            {
                report["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}";
            }
            return report;
        }
    }
}
