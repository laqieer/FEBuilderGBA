using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTableViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _songIndex;
        uint _songHeaderPointer;
        uint _trackCount;
        uint _headerPriority, _headerReverb;
        uint _playerType;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SongIndex { get => _songIndex; set => SetField(ref _songIndex, value); }
        /// <summary>Pointer to the song header (P0).</summary>
        public uint SongHeaderPointer { get => _songHeaderPointer; set => SetField(ref _songHeaderPointer, value); }
        /// <summary>Track count read from the song header (read-only info).</summary>
        public uint TrackCount { get => _trackCount; set => SetField(ref _trackCount, value); }
        /// <summary>Priority byte from the song header (read-only info).</summary>
        public uint HeaderPriority { get => _headerPriority; set => SetField(ref _headerPriority, value); }
        /// <summary>Reverb byte from the song header (read-only info).</summary>
        public uint HeaderReverb { get => _headerReverb; set => SetField(ref _headerReverb, value); }
        /// <summary>Priority / PlayerType for this song table entry (D4).</summary>
        public uint PlayerType { get => _playerType; set => SetField(ref _playerType, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadSongList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_table_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x400; i++) // reasonable max
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 7 >= (uint)rom.Data.Length) break;

                uint headerPtr = rom.u32(addr);
                if (!U.isPointer(headerPtr)) break;

                string songName = NameResolver.GetSongName(i);
                string name = $"{U.ToHexString(i)} {songName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSong(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 7 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            uint headerPtr = rom.u32(addr);        // P0 - Song header pointer
            PlayerType = rom.u32(addr + 4);         // D4 - Priority(PlayerType)
            SongHeaderPointer = headerPtr;

            if (U.isPointer(headerPtr))
            {
                uint headerAddr = rom.p32(addr);
                if (U.isSafetyOffset(headerAddr) && headerAddr + 7 < (uint)rom.Data.Length)
                {
                    TrackCount = rom.u8(headerAddr + 0);
                    HeaderPriority = rom.u8(headerAddr + 2);
                    HeaderReverb = rom.u8(headerAddr + 3);
                }
            }

            CanWrite = true;
        }

        public void WriteSong()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 7 >= (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr + 0, SongHeaderPointer);
            rom.write_u32(CurrentAddr + 4, PlayerType);
        }

        public int GetListCount() => LoadSongList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SongHeaderPointer"] = $"0x{SongHeaderPointer:X08}",
                ["PlayerType"] = $"0x{PlayerType:X08}",
                ["TrackCount"] = $"0x{TrackCount:X02}",
                ["HeaderPriority"] = $"0x{HeaderPriority:X02}",
                ["HeaderReverb"] = $"0x{HeaderReverb:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00_SongHeaderPointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_PlayerType"] = $"0x{rom.u32(a + 4):X08}",
            };

            // Also report header fields if the pointer is valid
            uint headerPtr = rom.u32(a);
            if (U.isPointer(headerPtr))
            {
                uint h = rom.p32(a);
                if (U.isSafetyOffset(h) && h + 7 < (uint)rom.Data.Length)
                {
                    report["u8@0x00_TrackCount"] = $"0x{rom.u8(h + 0):X02}";
                    report["u8@0x02_HeaderPriority"] = $"0x{rom.u8(h + 2):X02}";
                    report["u8@0x03_HeaderReverb"] = $"0x{rom.u8(h + 3):X02}";
                }
            }
            return report;
        }
    }
}
