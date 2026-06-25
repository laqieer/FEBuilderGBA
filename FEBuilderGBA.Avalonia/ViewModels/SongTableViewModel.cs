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

        /// <summary>
        /// WF parity: Song ID 0 (the reserved "silence/unset" entry) is
        /// write-protected. <c>SongTableForm.cs:21</c> sets
        /// <c>UseWriteProtectionID00 = true</c> and
        /// <c>InputFormRef.CheckWriteProtectionID00</c> hard-refuses the write
        /// (default option → <c>ShowStopError</c>). True when the currently
        /// loaded entry is index 0 (the issue's "tag==0 / CurrentAddr==base"
        /// criterion). <see cref="SongIndex"/> is derived from the table base
        /// inside <see cref="LoadSong"/>, so this is reliable even for a headless
        /// caller that invokes <c>LoadSong(base + 8)</c> without first selecting
        /// a list row.
        /// </summary>
        public bool IsSongIdZero => _songIndex == 0;
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
            // Derive the song index from the table base so the write-protection
            // guard (IsSongIdZero) is reliable even for a direct LoadSong() call
            // that did not go through list selection. Song table entries are
            // 8 bytes each; index = (addr - base) / 8.
            SongIndex = ComputeSongIndex(rom, addr);

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

        /// <summary>
        /// Maps a song-table entry address to its index. Entries are 8 bytes
        /// each starting at the table base (<c>rom.p32(sound_table_pointer)</c>).
        /// Returns <see cref="uint.MaxValue"/> if the base cannot be resolved or
        /// the address is below it (which never matches index 0, so the guard
        /// fails open to "not protected" only for genuinely unknown addresses).
        /// </summary>
        static uint ComputeSongIndex(ROM rom, uint addr)
        {
            if (rom?.RomInfo == null) return uint.MaxValue;
            uint ptr = rom.RomInfo.sound_table_pointer;
            // Guard the full 4-byte read: ROM.p32 only checks ptr >= Data.Length,
            // so a ptr within the last 3 bytes of the buffer can throw.
            if (ptr == 0 || ptr + 3 >= (uint)rom.Data.Length) return uint.MaxValue;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr) || addr < baseAddr) return uint.MaxValue;
            return (addr - baseAddr) / 8;
        }

        /// <summary>
        /// Writes the song-table entry. Returns <c>false</c> WITHOUT mutating the
        /// ROM when the entry is the reserved Song ID 0 (silence) — WF parity,
        /// defense-in-depth behind the view's Write_Click guard so no caller can
        /// bypass the protection — or when the address is out of range.
        /// </summary>
        public bool WriteSong()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            if (CurrentAddr + 7 >= (uint)rom.Data.Length) return false;
            // WF parity: never overwrite the reserved Song ID 0 (silence) entry.
            if (IsSongIdZero) return false;

            rom.write_u32(CurrentAddr + 0, SongHeaderPointer);
            rom.write_u32(CurrentAddr + 4, PlayerType);
            return true;
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

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["SongHeaderPointer"] = "u32@0x00_SongHeaderPointer",
                ["PlayerType"] = "u32@0x04_PlayerType",
                ["TrackCount"] = "u8@0x00_TrackCount",
                ["HeaderPriority"] = "u8@0x02_HeaderPriority",
                ["HeaderReverb"] = "u8@0x03_HeaderReverb",
            };
        }
    }
}
