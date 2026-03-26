using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongInstrumentDirectSoundViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "D12" });

        /// <summary>
        /// Find the first DirectSound instrument address by scanning the first song's
        /// instrument set (voicegroup). Returns U.NOT_FOUND if none found.
        /// </summary>
        static uint FindFirstDirectSoundAddr(ROM rom)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint songTablePtr = rom.RomInfo.sound_table_pointer;
            if (songTablePtr == 0) return U.NOT_FOUND;

            uint songTableBase = rom.p32(songTablePtr);
            if (!U.isSafetyOffset(songTableBase, rom)) return U.NOT_FOUND;

            // Scan first few songs to find one with a valid instrument set
            for (int songIdx = 0; songIdx < 10; songIdx++)
            {
                uint songEntryAddr = (uint)(songTableBase + songIdx * 8);
                if (songEntryAddr + 8 > (uint)rom.Data.Length) break;

                uint headerPtr = rom.u32(songEntryAddr);
                if (!U.isPointer(headerPtr)) continue;

                uint headerAddr = U.toOffset(headerPtr);
                if (!U.isSafetyOffset(headerAddr, rom) || headerAddr + 8 > (uint)rom.Data.Length)
                    continue;

                // Voicegroup (instrument set) pointer is at song header + 4
                uint voiceGroupPtr = rom.u32(headerAddr + 4);
                if (!U.isPointer(voiceGroupPtr)) continue;

                uint instBase = U.toOffset(voiceGroupPtr);
                if (!U.isSafetyOffset(instBase, rom)) continue;

                // Scan up to 128 instruments (12 bytes each) for a DirectSound type
                for (int i = 0; i < 128; i++)
                {
                    uint instAddr = (uint)(instBase + i * 12);
                    if (instAddr + 12 > (uint)rom.Data.Length) break;

                    byte type = (byte)rom.u8(instAddr);
                    if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18)
                    {
                        // Verify the wave pointer at offset 4
                        uint wavePtr = rom.u32(instAddr + 4);
                        if (U.isPointer(wavePtr))
                        {
                            uint waveAddr = U.toOffset(wavePtr);
                            // Ensure the 16-byte DirectSound header is fully readable
                            if (U.isSafetyOffset(waveAddr, rom) && waveAddr + 16 <= (uint)rom.Data.Length)
                                return waveAddr;
                        }
                    }
                }
            }
            return U.NOT_FOUND;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint addr = FindFirstDirectSoundAddr(rom);
            if (addr == U.NOT_FOUND || addr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(addr, "DirectSound Wave Data", 0));
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

        public int GetListCount()
        {
            return LoadList().Count;
        }

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

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Header"] = "Header@0x00",
            ["FrequencyHz1024"] = "FrequencyHz1024@0x04",
            ["LoopStartByte"] = "LoopStartByte@0x08",
            ["LengthByte"] = "LengthByte@0x0C",
        };
    }
}
