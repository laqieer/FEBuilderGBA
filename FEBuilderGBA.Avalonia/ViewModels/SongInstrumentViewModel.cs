using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Instrument type categories derived from the header byte (offset 0).
    /// </summary>
    public enum InstrumentCategory
    {
        DirectSound,   // 0x00, 0x08, 0x10, 0x18
        SquareWave,    // 0x01, 0x02, 0x09, 0x0A
        WaveMemory,    // 0x03, 0x0B
        Noise,         // 0x04, 0x0C
        MultiSample,   // 0x40
        Drum,          // 0x80
        Unknown
    }

    /// <summary>
    /// Unified ViewModel for all 6 GBA instrument types.
    /// Each instrument is a 12-byte block; up to 128 per instrument set.
    /// </summary>
    public class SongInstrumentViewModel : ViewModelBase, IDataVerifiable
    {
        const int BlockSize = 12;
        const int MaxInstruments = 128;

        uint _baseAddr;
        uint _currentAddr;
        bool _isLoaded;

        // Common: byte 0
        byte _headerByte;
        InstrumentCategory _category;
        string _typeName = "";

        // DirectSound / WaveMemory: u32 at offset 4 = wave pointer
        uint _wavePtr;

        // SquareWave: bytes 1-3
        byte _sweep;
        byte _dutyLen;
        byte _envStep;

        // Noise: byte 4
        byte _period;

        // MultiSample: u32 at offset 4 = key map ptr, u32 at offset 8 = sub-instr ptr
        uint _keyMapPtr;
        uint _subInstrPtr;

        // Drum: u32 at offset 4 = sub-instr ptr (reuses _subInstrPtr)

        // ADSR: bytes 8-11 (DirectSound, SquareWave, WaveMemory, Noise)
        byte _attack;
        byte _decay;
        byte _sustain;
        byte _release;

        public uint BaseAddr { get => _baseAddr; set => SetField(ref _baseAddr, value); }
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public byte HeaderByte { get => _headerByte; set => SetField(ref _headerByte, value); }
        public InstrumentCategory Category { get => _category; set => SetField(ref _category, value); }
        public string TypeName { get => _typeName; set => SetField(ref _typeName, value); }

        // DirectSound / WaveMemory
        public uint WavePtr { get => _wavePtr; set => SetField(ref _wavePtr, value); }

        // SquareWave
        public byte Sweep { get => _sweep; set => SetField(ref _sweep, value); }
        public byte DutyLen { get => _dutyLen; set => SetField(ref _dutyLen, value); }
        public byte EnvStep { get => _envStep; set => SetField(ref _envStep, value); }

        // Noise
        public byte Period { get => _period; set => SetField(ref _period, value); }

        // MultiSample
        public uint KeyMapPtr { get => _keyMapPtr; set => SetField(ref _keyMapPtr, value); }

        // MultiSample / Drum
        public uint SubInstrPtr { get => _subInstrPtr; set => SetField(ref _subInstrPtr, value); }

        // ADSR
        public byte Attack { get => _attack; set => SetField(ref _attack, value); }
        public byte Decay { get => _decay; set => SetField(ref _decay, value); }
        public byte Sustain { get => _sustain; set => SetField(ref _sustain, value); }
        public byte Release { get => _release; set => SetField(ref _release, value); }

        // Visibility helpers for the view
        public bool IsDirectSound => _category == InstrumentCategory.DirectSound;
        public bool IsSquareWave => _category == InstrumentCategory.SquareWave;
        public bool IsWaveMemory => _category == InstrumentCategory.WaveMemory;
        public bool IsNoise => _category == InstrumentCategory.Noise;
        public bool IsMultiSample => _category == InstrumentCategory.MultiSample;
        public bool IsDrum => _category == InstrumentCategory.Drum;
        public bool HasADSR => IsDirectSound || IsSquareWave || IsWaveMemory || IsNoise;
        public bool HasWavePtr => IsDirectSound || IsWaveMemory;
        public bool HasSubInstrPtr => IsMultiSample || IsDrum;

        /// <summary>
        /// Classify a header byte into an instrument category.
        /// </summary>
        public static InstrumentCategory ClassifyType(byte type)
        {
            switch (type)
            {
                case 0x00: case 0x08: case 0x10: case 0x18:
                    return InstrumentCategory.DirectSound;
                case 0x01: case 0x02: case 0x09: case 0x0A:
                    return InstrumentCategory.SquareWave;
                case 0x03: case 0x0B:
                    return InstrumentCategory.WaveMemory;
                case 0x04: case 0x0C:
                    return InstrumentCategory.Noise;
                case 0x40:
                    return InstrumentCategory.MultiSample;
                case 0x80:
                    return InstrumentCategory.Drum;
                default:
                    return InstrumentCategory.Unknown;
            }
        }

        /// <summary>
        /// Get a human-readable name for a header byte value.
        /// </summary>
        public static string GetInstrumentTypeName(byte type)
        {
            switch (type)
            {
                case 0x00: return "DirectSound";
                case 0x01: return "SquareWave1";
                case 0x02: return "SquareWave2";
                case 0x03: return "Wave Memory";
                case 0x04: return "Noise";
                case 0x08: return "DirectSound Fixed Freq";
                case 0x09: return "SquareWave (no pop)";
                case 0x0A: return "SquareWave (no pop)";
                case 0x0B: return "Wave Memory (no pop)";
                case 0x0C: return "Noise (no pop)";
                case 0x10: return "DirectSound Reverse";
                case 0x18: return "DirectSound Fixed Freq Reverse";
                case 0x40: return "Multi Sample";
                case 0x80: return "Drum Part";
                default: return $"Unknown (0x{type:X02})";
            }
        }

        /// <summary>
        /// Build the instrument list for the AddressListControl.
        /// Enumerates up to 128 instruments from baseAddr.
        /// </summary>
        public List<AddrResult> LoadInstrumentList(uint baseAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            BaseAddr = baseAddr;
            var result = new List<AddrResult>();

            for (int i = 0; i < MaxInstruments; i++)
            {
                uint addr = baseAddr + (uint)(i * BlockSize);
                if (addr + BlockSize > (uint)rom.Data.Length) break;

                byte type = (byte)rom.u8(addr);
                if (!IsValidInstrument(rom, addr, type))
                    break;

                string name = $"{i:D3} {GetInstrumentTypeName(type)} (0x{i:X02})";
                result.Add(new AddrResult(addr, name, (uint)(i * BlockSize)));
            }

            return result;
        }

        /// <summary>
        /// Check whether the 12 bytes at addr look like a valid instrument entry.
        /// Mirrors the WinForms validation logic.
        /// </summary>
        static bool IsValidInstrument(ROM rom, uint addr, byte type)
        {
            var cat = ClassifyType(type);
            switch (cat)
            {
                case InstrumentCategory.DirectSound:
                case InstrumentCategory.WaveMemory:
                case InstrumentCategory.Drum:
                {
                    uint p = rom.u32(addr + 4);
                    return U.isSafetyPointer(p);
                }
                case InstrumentCategory.MultiSample:
                {
                    uint p1 = rom.u32(addr + 4);
                    uint p2 = rom.u32(addr + 8);
                    return U.isSafetyPointer(p1) && U.isSafetyPointer(p2);
                }
                case InstrumentCategory.SquareWave:
                case InstrumentCategory.Noise:
                    return true;
                default:
                    return false;
            }
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            // Return a placeholder; the real list comes from LoadInstrumentList
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Instrument Editor", 0));
            return result;
        }

        /// <summary>
        /// Load a single 12-byte instrument entry and populate type-appropriate fields.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BlockSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            HeaderByte = (byte)rom.u8(addr);
            Category = ClassifyType(HeaderByte);
            TypeName = GetInstrumentTypeName(HeaderByte);

            // Reset all fields
            WavePtr = 0; Sweep = 0; DutyLen = 0; EnvStep = 0;
            Period = 0; KeyMapPtr = 0; SubInstrPtr = 0;
            Attack = 0; Decay = 0; Sustain = 0; Release = 0;

            switch (Category)
            {
                case InstrumentCategory.DirectSound:
                    // B0=type, B1-B3=pad, P4=WavePtr(u32), B8=atk, B9=dec, B10=sus, B11=rel
                    WavePtr = rom.u32(addr + 4);
                    Attack = (byte)rom.u8(addr + 8);
                    Decay = (byte)rom.u8(addr + 9);
                    Sustain = (byte)rom.u8(addr + 10);
                    Release = (byte)rom.u8(addr + 11);
                    break;

                case InstrumentCategory.SquareWave:
                    // B0=type, B1=sweep, B2=dutyLen, B3=envStep, B4-B7=pad, B8-B11=ADSR
                    Sweep = (byte)rom.u8(addr + 1);
                    DutyLen = (byte)rom.u8(addr + 2);
                    EnvStep = (byte)rom.u8(addr + 3);
                    Attack = (byte)rom.u8(addr + 8);
                    Decay = (byte)rom.u8(addr + 9);
                    Sustain = (byte)rom.u8(addr + 10);
                    Release = (byte)rom.u8(addr + 11);
                    break;

                case InstrumentCategory.WaveMemory:
                    // B0=type, B1-B3=pad, P4=WavePtr(u32), B8-B11=ADSR
                    WavePtr = rom.u32(addr + 4);
                    Attack = (byte)rom.u8(addr + 8);
                    Decay = (byte)rom.u8(addr + 9);
                    Sustain = (byte)rom.u8(addr + 10);
                    Release = (byte)rom.u8(addr + 11);
                    break;

                case InstrumentCategory.Noise:
                    // B0=type, B1-B3=pad, B4=period, B5-B7=pad, B8-B11=ADSR
                    Period = (byte)rom.u8(addr + 4);
                    Attack = (byte)rom.u8(addr + 8);
                    Decay = (byte)rom.u8(addr + 9);
                    Sustain = (byte)rom.u8(addr + 10);
                    Release = (byte)rom.u8(addr + 11);
                    break;

                case InstrumentCategory.MultiSample:
                    // B0=type, B1-B3=pad, P4=KeyMapPtr(u32), P8=SubInstrPtr(u32)
                    KeyMapPtr = rom.u32(addr + 4);
                    SubInstrPtr = rom.u32(addr + 8);
                    break;

                case InstrumentCategory.Drum:
                    // B0=type, B1-B3=pad, P4=SubInstrPtr(u32), B8-B11=pad
                    SubInstrPtr = rom.u32(addr + 4);
                    break;
            }

            // Notify visibility changes
            OnPropertyChanged(nameof(IsDirectSound));
            OnPropertyChanged(nameof(IsSquareWave));
            OnPropertyChanged(nameof(IsWaveMemory));
            OnPropertyChanged(nameof(IsNoise));
            OnPropertyChanged(nameof(IsMultiSample));
            OnPropertyChanged(nameof(IsDrum));
            OnPropertyChanged(nameof(HasADSR));
            OnPropertyChanged(nameof(HasWavePtr));
            OnPropertyChanged(nameof(HasSubInstrPtr));

            IsLoaded = true;
        }

        /// <summary>
        /// Write the current instrument data back to ROM (12 bytes).
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (!U.isSafetyOffset(CurrentAddr + BlockSize)) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr, HeaderByte);

            switch (Category)
            {
                case InstrumentCategory.DirectSound:
                    rom.write_u8(addr + 1, 0);
                    rom.write_u8(addr + 2, 0);
                    rom.write_u8(addr + 3, 0);
                    rom.write_u32(addr + 4, WavePtr);
                    rom.write_u8(addr + 8, Attack);
                    rom.write_u8(addr + 9, Decay);
                    rom.write_u8(addr + 10, Sustain);
                    rom.write_u8(addr + 11, Release);
                    break;

                case InstrumentCategory.SquareWave:
                    rom.write_u8(addr + 1, Sweep);
                    rom.write_u8(addr + 2, DutyLen);
                    rom.write_u8(addr + 3, EnvStep);
                    rom.write_u32(addr + 4, 0);
                    rom.write_u8(addr + 8, Attack);
                    rom.write_u8(addr + 9, Decay);
                    rom.write_u8(addr + 10, Sustain);
                    rom.write_u8(addr + 11, Release);
                    break;

                case InstrumentCategory.WaveMemory:
                    rom.write_u8(addr + 1, 0);
                    rom.write_u8(addr + 2, 0);
                    rom.write_u8(addr + 3, 0);
                    rom.write_u32(addr + 4, WavePtr);
                    rom.write_u8(addr + 8, Attack);
                    rom.write_u8(addr + 9, Decay);
                    rom.write_u8(addr + 10, Sustain);
                    rom.write_u8(addr + 11, Release);
                    break;

                case InstrumentCategory.Noise:
                    rom.write_u8(addr + 1, 0);
                    rom.write_u8(addr + 2, 0);
                    rom.write_u8(addr + 3, 0);
                    rom.write_u8(addr + 4, Period);
                    rom.write_u8(addr + 5, 0);
                    rom.write_u8(addr + 6, 0);
                    rom.write_u8(addr + 7, 0);
                    rom.write_u8(addr + 8, Attack);
                    rom.write_u8(addr + 9, Decay);
                    rom.write_u8(addr + 10, Sustain);
                    rom.write_u8(addr + 11, Release);
                    break;

                case InstrumentCategory.MultiSample:
                    rom.write_u8(addr + 1, 0);
                    rom.write_u8(addr + 2, 0);
                    rom.write_u8(addr + 3, 0);
                    rom.write_u32(addr + 4, KeyMapPtr);
                    rom.write_u32(addr + 8, SubInstrPtr);
                    break;

                case InstrumentCategory.Drum:
                    rom.write_u8(addr + 1, 0);
                    rom.write_u8(addr + 2, 0);
                    rom.write_u8(addr + 3, 0);
                    rom.write_u32(addr + 4, SubInstrPtr);
                    rom.write_u32(addr + 8, 0);
                    break;
            }
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["HeaderByte"] = $"0x{HeaderByte:X02}",
                ["TypeName"] = TypeName,
                ["Category"] = Category.ToString(),
            };

            switch (Category)
            {
                case InstrumentCategory.DirectSound:
                case InstrumentCategory.WaveMemory:
                    d["WavePtr"] = $"0x{WavePtr:X08}";
                    d["Attack"] = Attack.ToString();
                    d["Decay"] = Decay.ToString();
                    d["Sustain"] = Sustain.ToString();
                    d["Release"] = Release.ToString();
                    break;
                case InstrumentCategory.SquareWave:
                    d["Sweep"] = $"0x{Sweep:X02}";
                    d["DutyLen"] = $"0x{DutyLen:X02}";
                    d["EnvStep"] = $"0x{EnvStep:X02}";
                    d["Attack"] = Attack.ToString();
                    d["Decay"] = Decay.ToString();
                    d["Sustain"] = Sustain.ToString();
                    d["Release"] = Release.ToString();
                    break;
                case InstrumentCategory.Noise:
                    d["Period"] = $"0x{Period:X02}";
                    d["Attack"] = Attack.ToString();
                    d["Decay"] = Decay.ToString();
                    d["Sustain"] = Sustain.ToString();
                    d["Release"] = Release.ToString();
                    break;
                case InstrumentCategory.MultiSample:
                    d["KeyMapPtr"] = $"0x{KeyMapPtr:X08}";
                    d["SubInstrPtr"] = $"0x{SubInstrPtr:X08}";
                    break;
                case InstrumentCategory.Drum:
                    d["SubInstrPtr"] = $"0x{SubInstrPtr:X08}";
                    break;
            }

            return d;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_Header"] = $"0x{rom.u8(a):X02}",
                ["u8@0x01_Sweep"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_DutyLen"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_EnvStep"] = $"0x{rom.u8(a + 3):X02}",
                ["u32@0x04_Ptr"] = $"0x{rom.u32(a + 4):X08}",
                ["u8@0x08_Attack"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_Decay"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_Sustain"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Release"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
