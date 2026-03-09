using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTrackViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Song Track Editor", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _trackCount;
        uint _numBlks;
        uint _priority;
        uint _reverb;
        uint _instrumentAddr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Number of tracks in this song (B0).</summary>
        public uint TrackCount { get => _trackCount; set => SetField(ref _trackCount, value); }
        /// <summary>Number of blocks (B1).</summary>
        public uint NumBlks { get => _numBlks; set => SetField(ref _numBlks, value); }
        /// <summary>Song priority (B2).</summary>
        public uint Priority { get => _priority; set => SetField(ref _priority, value); }
        /// <summary>Reverb level (B3). 0x00=inherit, 0x80=off, 0xFF=max.</summary>
        public uint Reverb { get => _reverb; set => SetField(ref _reverb, value); }
        /// <summary>Pointer to the instrument set (P4).</summary>
        public uint InstrumentAddr { get => _instrumentAddr; set => SetField(ref _instrumentAddr, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TrackCount = rom.u8(addr + 0);
            NumBlks = rom.u8(addr + 1);
            Priority = rom.u8(addr + 2);
            Reverb = rom.u8(addr + 3);
            InstrumentAddr = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 8 > (uint)rom.Data.Length) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)TrackCount);
            rom.write_u8(addr + 1, (byte)NumBlks);
            rom.write_u8(addr + 2, (byte)Priority);
            rom.write_u8(addr + 3, (byte)Reverb);
            rom.write_u32(addr + 4, InstrumentAddr);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TrackCount"] = $"0x{TrackCount:X02}",
                ["NumBlks"] = $"0x{NumBlks:X02}",
                ["Priority"] = $"0x{Priority:X02}",
                ["Reverb"] = $"0x{Reverb:X02}",
                ["InstrumentAddr"] = $"0x{InstrumentAddr:X08}",
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
                ["TrackCount@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["NumBlks@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["Priority@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["Reverb@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["InstrumentAddr@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
