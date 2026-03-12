using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongInstrumentDirectSoundViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "D12" });

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Direct Sound Instruments", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _header;
        uint _frequencyHz1024;
        uint _loopStartByte;
        uint _lengthByte;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Header flags (D0). DirectSound=0x40000000, DirectSoundFixedFreq=0x00000000.</summary>
        public uint Header { get => _header; set => SetField(ref _header, value); }
        /// <summary>Frequency in Hz*1024 (D4).</summary>
        public uint FrequencyHz1024 { get => _frequencyHz1024; set => SetField(ref _frequencyHz1024, value); }
        /// <summary>Loop start position in bytes (D8).</summary>
        public uint LoopStartByte { get => _loopStartByte; set => SetField(ref _loopStartByte, value); }
        /// <summary>Wave data length in bytes (D12).</summary>
        public uint LengthByte { get => _lengthByte; set => SetField(ref _lengthByte, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            Header = v["D0"];
            FrequencyHz1024 = v["D4"];
            LoopStartByte = v["D8"];
            LengthByte = v["D12"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (!U.isSafetyOffset(CurrentAddr + 16)) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = Header, ["D4"] = FrequencyHz1024,
                ["D8"] = LoopStartByte, ["D12"] = LengthByte,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Header"] = $"0x{Header:X08}",
                ["FrequencyHz1024"] = $"0x{FrequencyHz1024:X08}",
                ["LoopStartByte"] = $"0x{LoopStartByte:X08}",
                ["LengthByte"] = $"0x{LengthByte:X08}",
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
                ["Header@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["FrequencyHz1024@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["LoopStartByte@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["LengthByte@0x0C"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
