using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundRoomViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _songId;
        uint _textId;
        uint _raw4, _raw8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
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
            SongId = rom.u16(addr + 0);
            Raw4 = rom.u32(addr + 4);
            Raw8 = rom.u32(addr + 8);
            if (dataSize >= 16)
            {
                TextId = rom.u16(addr + 12);
            }
            else
            {
                TextId = 0;
            }
            IsLoaded = true;
        }
    }
}
