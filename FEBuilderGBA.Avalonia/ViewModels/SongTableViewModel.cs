using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTableViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _songIndex;
        uint _headerPointer;
        uint _trackCount;
        uint _priority, _reverb;
        uint _d4;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SongIndex { get => _songIndex; set => SetField(ref _songIndex, value); }
        public uint HeaderPointer { get => _headerPointer; set => SetField(ref _headerPointer, value); }
        public uint TrackCount { get => _trackCount; set => SetField(ref _trackCount, value); }
        public uint Priority { get => _priority; set => SetField(ref _priority, value); }
        public uint Reverb { get => _reverb; set => SetField(ref _reverb, value); }
        // D4: second dword in song table entry
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
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

                string name = U.ToHexString(i) + " Song 0x" + headerPtr.ToString("X08");
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

            uint headerPtr = rom.u32(addr);  // P0
            D4 = rom.u32(addr + 4);          // D4
            HeaderPointer = headerPtr;

            if (U.isPointer(headerPtr))
            {
                uint headerAddr = rom.p32(addr);
                if (U.isSafetyOffset(headerAddr) && headerAddr + 7 < (uint)rom.Data.Length)
                {
                    TrackCount = rom.u8(headerAddr + 0);
                    Priority = rom.u8(headerAddr + 2);
                    Reverb = rom.u8(headerAddr + 3);
                }
            }

            CanWrite = true;
        }

        public void WriteSong()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 7 >= (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr + 0, HeaderPointer);
            rom.write_u32(CurrentAddr + 4, D4);
        }

        public int GetListCount() => LoadSongList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["HeaderPointer"] = $"0x{HeaderPointer:X08}",
                ["TrackCount"] = $"0x{TrackCount:X02}",
                ["Priority"] = $"0x{Priority:X02}",
                ["Reverb"] = $"0x{Reverb:X02}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };

            // Also report header fields if the pointer is valid
            uint headerPtr = rom.u32(a);
            if (U.isPointer(headerPtr))
            {
                uint h = rom.p32(a);
                if (U.isSafetyOffset(h) && h + 7 < (uint)rom.Data.Length)
                {
                    report["u8@0x08"] = $"0x{rom.u8(h + 0):X02}";
                    report["u8@0x0A"] = $"0x{rom.u8(h + 2):X02}";
                    report["u8@0x0B"] = $"0x{rom.u8(h + 3):X02}";
                }
            }
            return report;
        }
    }
}
