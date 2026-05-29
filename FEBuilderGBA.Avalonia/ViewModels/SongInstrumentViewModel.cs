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
    ///
    /// Marked `partial` so the navigation manifest sibling file
    /// `SongInstrumentViewModel.NavigationTargets.cs` can declare the
    /// `INavigationTargetSource` implementation in lockstep (#387 plan
    /// review v2 concern #3).
    /// </summary>
    public partial class SongInstrumentViewModel : ViewModelBase, IDataVerifiable
    {
        const int BlockSize = 12;
        const int MaxInstruments = 128;

        uint _baseAddr;
        uint _currentAddr;
        bool _isLoaded;

        // Song-context tracking for the "Expand List" (voicegroup → 128)
        // affordance (#780). A voicegroup is a *shared* instrument set
        // referenced per-song via songHeader+4. We only enable / perform the
        // expand when the currently-loaded base is a real voicegroup reachable
        // from at least one song header (so the all-reference repoint via
        // DataExpansionCore.RepointAllReferences is meaningful and we never
        // relocate an arbitrary user-typed address). _hasSongContext is set by
        // LoadInstrumentList / LoadList whenever the loaded base resolves to a
        // song's voicegroup; _songContextBase records that voicegroup base
        // (== BaseAddr for the resolved case).
        bool _hasSongContext;
        uint _songContextBase;

        // Common: byte 0
        byte _headerByte;
        InstrumentCategory _category;
        string _typeName = "";

        // Raw per-byte access — every byte the WF designer exposes is
        // user-editable (#387 plan review v2 concern #2). LoadEntry/Write
        // route through these raw fields so we never drop bytes the user
        // explicitly set (regression guard: Drum N80 B8..B11, SquareWave B4).
        byte _b1, _b2, _b3, _b4, _b5, _b6, _b7;
        byte _b8, _b9, _b10, _b11;

        // DirectSound / WaveMemory: u32 at offset 4 = wave pointer
        uint _wavePtr;

        // MultiSample: u32 at offset 4 = key map ptr, u32 at offset 8 = sub-instr ptr
        uint _keyMapPtr;
        uint _subInstrPtr;

        // Drum: u32 at offset 4 = sub-instr ptr (reuses _subInstrPtr)

        public uint BaseAddr { get => _baseAddr; set => SetField(ref _baseAddr, value); }
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public byte HeaderByte { get => _headerByte; set => SetField(ref _headerByte, value); }
        public InstrumentCategory Category { get => _category; set => SetField(ref _category, value); }
        public string TypeName { get => _typeName; set => SetField(ref _typeName, value); }

        // Raw per-byte access (B1..B11) — see field declarations above.
        // Each property setter routes through SetField so PropertyChanged
        // events fire for UI binding.
        public byte B1 { get => _b1; set => SetField(ref _b1, value); }
        public byte B2 { get => _b2; set => SetField(ref _b2, value); }
        public byte B3 { get => _b3; set => SetField(ref _b3, value); }
        public byte B4 { get => _b4; set => SetField(ref _b4, value); }
        public byte B5 { get => _b5; set => SetField(ref _b5, value); }
        public byte B6 { get => _b6; set => SetField(ref _b6, value); }
        public byte B7 { get => _b7; set => SetField(ref _b7, value); }
        public byte B8 { get => _b8; set => SetField(ref _b8, value); }
        public byte B9 { get => _b9; set => SetField(ref _b9, value); }
        public byte B10 { get => _b10; set => SetField(ref _b10, value); }
        public byte B11 { get => _b11; set => SetField(ref _b11, value); }

        // DirectSound / WaveMemory
        public uint WavePtr { get => _wavePtr; set => SetField(ref _wavePtr, value); }

        // SquareWave semantic aliases over the raw bytes. The WF designer
        // labels the SquareWave tab rows as:
        //   B1 ("??"),  B2 ("00"),  B3 ("sweep"),  B4 ("squarepattern").
        // The hardware-level "sweep envelope" byte therefore lives at B3,
        // not B1 — corrected per Copilot review PR #626 round 2 finding #6.
        // DutyLen / EnvStep are not labeled in WF; we keep semantic aliases
        // pointing at the most plausible raw bytes (B4 = squarepattern,
        // B1 = the unlabeled "??" envelope-step byte). Callers SHOULD prefer
        // the raw B1..B11 accessors; the aliases exist only for legacy test
        // ergonomics.
        public byte Sweep { get => _b3; set => B3 = value; }
        public byte DutyLen { get => _b4; set => B4 = value; }
        public byte EnvStep { get => _b1; set => B1 = value; }

        // Noise semantic alias (B4 = noisepattern / Period).
        public byte Period { get => _b4; set => B4 = value; }

        // MultiSample
        public uint KeyMapPtr { get => _keyMapPtr; set => SetField(ref _keyMapPtr, value); }

        // MultiSample / Drum
        public uint SubInstrPtr { get => _subInstrPtr; set => SetField(ref _subInstrPtr, value); }

        // ADSR semantic aliases over the raw bytes (B8=Attack, B9=Decay,
        // B10=Sustain, B11=Release).
        public byte Attack { get => _b8; set => B8 = value; }
        public byte Decay { get => _b9; set => B9 = value; }
        public byte Sustain { get => _b10; set => B10 = value; }
        public byte Release { get => _b11; set => B11 = value; }

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
            // Names must match the AXAML tab Header strings so the
            // MoreInfo text stays consistent with the active UNIONTAB
            // header (Copilot review PR #626 round 2 finding #8 — the
            // 0x09/0x0A/0x0B/0x0C variants previously returned
            // "*(no pop)" while the tab headers showed SquareWave3 /
            // SquareWave4 / Wave Memory2 / Noise2).
            switch (type)
            {
                case 0x00: return "DirectSound";
                case 0x01: return "SquareWave1";
                case 0x02: return "SquareWave2";
                case 0x03: return "Wave Memory";
                case 0x04: return "Noise";
                case 0x08: return "DirectSound Fixed Freq";
                case 0x09: return "SquareWave3";
                case 0x0A: return "SquareWave4";
                case 0x0B: return "Wave Memory2";
                case 0x0C: return "Noise2";
                case 0x10: return "DirectSound Reverse";
                case 0x18: return "DirectSound Fixed Freq Reverse";
                case 0x40: return "Multi Sample";
                case 0x80: return "DrumPart";
                default: return $"Unknown (0x{type:X02})";
            }
        }

        /// <summary>
        /// Build the instrument list for the AddressListControl.
        /// Enumerates up to <paramref name="maxCount"/> instruments from
        /// baseAddr (or 128 when 0 is passed). The scan still stops on the
        /// first invalid instrument byte, so passing a large cap mirrors
        /// the previous "scan until invalid" behavior; passing a small cap
        /// surfaces the read-config bar's WinForms-parity Read Count input
        /// (Copilot review PR #626 round 3).
        /// </summary>
        public List<AddrResult> LoadInstrumentList(uint baseAddr, uint maxCount = 0)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            BaseAddr = baseAddr;
            // Record whether this base is a real song-referenced voicegroup so
            // the "Expand List" (→ 128) affordance can gate on it (#780). An
            // explicit base (SongTrack jump / user-typed) only counts as song
            // context when at least one song header points at it.
            SetSongContext(IsSongReferencedVoicegroup(rom, baseAddr), baseAddr);
            var result = new List<AddrResult>();

            uint capUInt = maxCount == 0 ? MaxInstruments : maxCount;
            int cap = capUInt > MaxInstruments ? MaxInstruments : (int)capUInt;

            for (int i = 0; i < cap; i++)
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

        /// <summary>
        /// Resolve a valid voicegroup address from the song table.
        /// Scans songs starting from index 0; some songs (e.g. song 0) may have
        /// invalid voicegroup pointers (0x40000000), so we skip those.
        /// Path per song: song table entry -> p32 -> song header -> p32(header+4) -> voicegroup.
        /// Returns 0 if no valid voicegroup found.
        /// </summary>
        static uint ResolveFirstSongVoicegroup(ROM rom)
            => ResolveFirstSongVoicegroup(rom, out _);

        /// <summary>
        /// Resolve a valid voicegroup address from the song table, also
        /// returning the song-header <c>+4</c> slot that points at it
        /// (<paramref name="songHeaderVocaSlot"/>). Returns 0 (and slot 0) if
        /// no valid voicegroup is found.
        /// </summary>
        static uint ResolveFirstSongVoicegroup(ROM rom, out uint songHeaderVocaSlot)
        {
            songHeaderVocaSlot = 0;

            uint songTablePointer = rom.RomInfo.sound_table_pointer;
            if (songTablePointer == 0) return 0;

            // Verify the pointer at sound_table_pointer is valid
            uint songTablePointerVal = rom.u32(songTablePointer);
            if (!U.isSafetyPointer(songTablePointerVal)) return 0;

            // Dereference to get song table base
            uint songTableBase = rom.p32(songTablePointer);
            if (!U.isSafetyOffset(songTableBase)) return 0;

            // Song table entries are 8 bytes each (4 pointer + 4 priority)
            const int SongEntrySize = 8;
            const int MaxSongsToCheck = 32; // Enough to find a valid one

            for (int i = 0; i < MaxSongsToCheck; i++)
            {
                uint entryAddr = songTableBase + (uint)(i * SongEntrySize);
                if (entryAddr + SongEntrySize > (uint)rom.Data.Length) break;

                uint songHeaderPtr = rom.u32(entryAddr);
                if (!U.isPointer(songHeaderPtr)) continue;

                uint songHeader = rom.p32(entryAddr);
                if (!U.isSafetyOffset(songHeader)) continue;

                // Song header offset +4 = voicegroup pointer
                uint vocaPtr = rom.u32(songHeader + 4);
                if (!U.isPointer(vocaPtr)) continue;

                uint voca = rom.p32(songHeader + 4);
                if (!U.isSafetyOffset(voca)) continue;

                songHeaderVocaSlot = songHeader + 4;
                return voca;
            }

            return 0;
        }

        /// <summary>
        /// Decide whether <paramref name="baseAddr"/> is a voicegroup
        /// referenced from at least one song header (<c>songHeader+4</c>). Used
        /// to qualify the SongTrack-supplied / user-typed base for the
        /// "Expand List" affordance (#780). Mirrors the song-table walk of
        /// <see cref="ResolveFirstSongVoicegroup(ROM, out uint)"/> but scans
        /// every song looking for one whose voicegroup equals
        /// <paramref name="baseAddr"/> (as an offset).
        /// </summary>
        static bool IsSongReferencedVoicegroup(ROM rom, uint baseAddr)
        {
            if (rom?.RomInfo == null) return false;
            uint baseOffset = U.toOffset(baseAddr);
            if (!U.isSafetyOffset(baseOffset, rom)) return false;

            uint songTablePointer = rom.RomInfo.sound_table_pointer;
            if (songTablePointer == 0) return false;
            uint songTablePointerVal = rom.u32(songTablePointer);
            if (!U.isSafetyPointer(songTablePointerVal)) return false;
            uint songTableBase = rom.p32(songTablePointer);
            if (!U.isSafetyOffset(songTableBase)) return false;

            const int SongEntrySize = 8;
            // Walk the whole table (terminated by an invalid header pointer).
            // Cap defensively so a corrupt table can never spin forever.
            const int MaxSongsToCheck = 0x1000;
            for (int i = 0; i < MaxSongsToCheck; i++)
            {
                uint entryAddr = songTableBase + (uint)(i * SongEntrySize);
                if (entryAddr + SongEntrySize > (uint)rom.Data.Length) break;

                uint songHeaderPtr = rom.u32(entryAddr);
                if (!U.isPointer(songHeaderPtr)) continue;
                uint songHeader = rom.p32(entryAddr);
                if (!U.isSafetyOffset(songHeader)) continue;

                uint vocaPtr = rom.u32(songHeader + 4);
                if (!U.isPointer(vocaPtr)) continue;
                uint voca = rom.p32(songHeader + 4);
                if (voca == baseOffset) return true;
            }
            return false;
        }

        /// <summary>
        /// IDataVerifiable: when BaseAddr is set (by parent SongTrack view), uses it directly.
        /// When BaseAddr is 0 (standalone), auto-resolves the first song's voicegroup.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            if (BaseAddr != 0) return LoadInstrumentList(BaseAddr);

            // Auto-resolve first song's voicegroup for standalone mode.
            // The resolved voca is by definition song-referenced, so
            // LoadInstrumentList(voca) -> IsSongReferencedVoicegroup records
            // the song context for the Expand List affordance (#780).
            uint voca = ResolveFirstSongVoicegroup(rom);
            if (voca != 0) return LoadInstrumentList(voca);

            // No voicegroup found — clear any stale song context so Expand
            // List stays disabled.
            SetSongContext(false, 0);
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
            // Shared 12-byte range guard (PR #626 round 4 blocker #1).
            // Reject addr==0 (sentinel for "no selection") and validate the
            // full base..base+BlockSize range, not just the end offset.
            if (!IsValidEntryRange(rom, addr)) return;

            CurrentAddr = addr;

            HeaderByte = (byte)rom.u8(addr);
            Category = ClassifyType(HeaderByte);
            TypeName = GetInstrumentTypeName(HeaderByte);

            // Reset pointer fields (raw bytes are repopulated below).
            WavePtr = 0; KeyMapPtr = 0; SubInstrPtr = 0;
            // Always load raw bytes so the per-tab UI surfaces every byte
            // the WF designer exposes (#387 plan review v2 concern #2).
            B1 = (byte)rom.u8(addr + 1);
            B2 = (byte)rom.u8(addr + 2);
            B3 = (byte)rom.u8(addr + 3);
            B4 = (byte)rom.u8(addr + 4);
            B5 = (byte)rom.u8(addr + 5);
            B6 = (byte)rom.u8(addr + 6);
            B7 = (byte)rom.u8(addr + 7);
            B8 = (byte)rom.u8(addr + 8);
            B9 = (byte)rom.u8(addr + 9);
            B10 = (byte)rom.u8(addr + 10);
            B11 = (byte)rom.u8(addr + 11);

            switch (Category)
            {
                case InstrumentCategory.DirectSound:
                case InstrumentCategory.WaveMemory:
                    // P4=WavePtr(u32)
                    WavePtr = rom.u32(addr + 4);
                    break;

                case InstrumentCategory.MultiSample:
                    // P4=KeyMapPtr(u32), P8=SubInstrPtr(u32)
                    KeyMapPtr = rom.u32(addr + 4);
                    SubInstrPtr = rom.u32(addr + 8);
                    break;

                case InstrumentCategory.Drum:
                    // P4=SubInstrPtr(u32)
                    SubInstrPtr = rom.u32(addr + 4);
                    break;

                // SquareWave / Noise: raw bytes already loaded above.
                // Per WF designer labels, SquareWave B3 = sweep, B4 =
                // squarepattern; Noise B4 = noisepattern. Use the raw
                // B1..B11 accessors for per-byte UI binding.
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
            if (rom == null) return;
            // Shared 12-byte range guard (PR #626 round 4 blocker #1).
            // The previous `U.isSafetyOffset(CurrentAddr + BlockSize)` was
            // off-by-one (rejected the last valid block where
            // CurrentAddr+12 == ROM length) and didn't validate the base.
            if (!IsValidEntryRange(rom, CurrentAddr)) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr, HeaderByte);
            // Always write B1..B3 (#387 concern v2 #2 — preserve user edits).
            rom.write_u8(addr + 1, B1);
            rom.write_u8(addr + 2, B2);
            rom.write_u8(addr + 3, B3);

            switch (Category)
            {
                case InstrumentCategory.DirectSound:
                case InstrumentCategory.WaveMemory:
                    // P4 = WavePtr (u32) overwrites raw B4..B7.
                    rom.write_u32(addr + 4, WavePtr);
                    rom.write_u8(addr + 8, B8);
                    rom.write_u8(addr + 9, B9);
                    rom.write_u8(addr + 10, B10);
                    rom.write_u8(addr + 11, B11);
                    break;

                case InstrumentCategory.SquareWave:
                    // SquareWave exposes B4..B7 as raw bytes (squarepattern at B4).
                    rom.write_u8(addr + 4, B4);
                    rom.write_u8(addr + 5, B5);
                    rom.write_u8(addr + 6, B6);
                    rom.write_u8(addr + 7, B7);
                    rom.write_u8(addr + 8, B8);
                    rom.write_u8(addr + 9, B9);
                    rom.write_u8(addr + 10, B10);
                    rom.write_u8(addr + 11, B11);
                    break;

                case InstrumentCategory.Noise:
                    // Noise: B4 = noisepattern, B5..B7 still raw.
                    rom.write_u8(addr + 4, B4);
                    rom.write_u8(addr + 5, B5);
                    rom.write_u8(addr + 6, B6);
                    rom.write_u8(addr + 7, B7);
                    rom.write_u8(addr + 8, B8);
                    rom.write_u8(addr + 9, B9);
                    rom.write_u8(addr + 10, B10);
                    rom.write_u8(addr + 11, B11);
                    break;

                case InstrumentCategory.MultiSample:
                    // P4=KeyMapPtr(u32), P8=SubInstrPtr(u32). No B8..B11 user
                    // fields per WF designer (N40 has no B8-B11 controls).
                    rom.write_u32(addr + 4, KeyMapPtr);
                    rom.write_u32(addr + 8, SubInstrPtr);
                    break;

                case InstrumentCategory.Drum:
                    // P4=SubInstrPtr(u32). N80 EXPOSES B8..B11 (regression
                    // guard for #387 plan review v2 concern #2 — the prior
                    // VM wrote zeros to addr+8..11 and dropped user edits).
                    rom.write_u32(addr + 4, SubInstrPtr);
                    rom.write_u8(addr + 8, B8);
                    rom.write_u8(addr + 9, B9);
                    rom.write_u8(addr + 10, B10);
                    rom.write_u8(addr + 11, B11);
                    break;

                default:
                    // Unknown: write all raw bytes verbatim so user edits
                    // survive even when the category cannot be classified.
                    rom.write_u8(addr + 4, B4);
                    rom.write_u8(addr + 5, B5);
                    rom.write_u8(addr + 6, B6);
                    rom.write_u8(addr + 7, B7);
                    rom.write_u8(addr + 8, B8);
                    rom.write_u8(addr + 9, B9);
                    rom.write_u8(addr + 10, B10);
                    rom.write_u8(addr + 11, B11);
                    break;
            }
        }

        /// <summary>
        /// Map a header byte to the corresponding tab AutomationId
        /// (#387 plan review v2 concern #1). Returns the empty string for
        /// header bytes that do not map to a UNIONTAB_Nxx tab.
        /// </summary>
        public static string GetExpectedTabId(byte headerByte)
        {
            switch (headerByte)
            {
                case 0x00: return "SongInstrument_UNIONTAB_N00_Tab";
                case 0x01: return "SongInstrument_UNIONTAB_N01_Tab";
                case 0x02: return "SongInstrument_UNIONTAB_N02_Tab";
                case 0x03: return "SongInstrument_UNIONTAB_N03_Tab";
                case 0x04: return "SongInstrument_UNIONTAB_N04_Tab";
                case 0x08: return "SongInstrument_UNIONTAB_N08_Tab";
                case 0x09: return "SongInstrument_UNIONTAB_N09_Tab";
                case 0x0A: return "SongInstrument_UNIONTAB_N0A_Tab";
                case 0x0B: return "SongInstrument_UNIONTAB_N0B_Tab";
                case 0x0C: return "SongInstrument_UNIONTAB_N0C_Tab";
                case 0x10: return "SongInstrument_UNIONTAB_N10_Tab";
                case 0x18: return "SongInstrument_UNIONTAB_N18_Tab";
                case 0x40: return "SongInstrument_UNIONTAB_N40_Tab";
                case 0x80: return "SongInstrument_UNIONTAB_N80_Tab";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Compute the SongInstrument fingerprint MD5 hash for the loaded
        /// entry (mirrors WF `SongInstrumentForm.FingerPrint`). DirectSound /
        /// WaveMemory variants hash B1..B3 + B8..B11 + the wave data bytes;
        /// SquareWave / Noise hash bytes 1..11 directly. Returns the empty
        /// string when the address is out of range or the wave data cannot
        /// be located. Used by the View to populate the FINGERPRINT footer
        /// (Copilot review PR #626 round 2 finding #5).
        /// </summary>
        public string ComputeFingerprint()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return string.Empty;
            // Shared 12-byte range guard (PR #626 round 4 blocker #1).
            if (!IsValidEntryRange(rom, CurrentAddr)) return string.Empty;

            uint addr = CurrentAddr;
            byte type = (byte)rom.u8(addr);

            switch (type)
            {
                case 0x00:
                case 0x08:
                case 0x10:
                case 0x18:
                    return ComputeDirectSoundFingerprint(rom, addr);

                case 0x03:
                case 0x0B:
                    return "WW" + ComputeWaveMemoryFingerprint(rom, addr);

                case 0x01:
                case 0x02:
                case 0x09:
                case 0x0A:
                    return "SQ" + ComputeRawSliceFingerprint(rom, addr + 1, 11);

                // WF SongInstrumentForm.FingerPrint only fingerprints 0x04
                // (Noise); 0x0C (Noise2) falls through to empty per
                // WinForms behavior (PR #626 round 4 blocker #2 — was
                // incorrectly returning "NZ..." for 0x0C).
                case 0x04:
                    return "NZ" + ComputeRawSliceFingerprint(rom, addr + 1, 11);

                // Drum / MultiSample / 0x0C do not produce a stable
                // fingerprint in WF — nested instruments would loop
                // forever and Noise2 is intentionally excluded. Empty.
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Shared 12-byte range validity guard used by LoadEntry, Write, and
        /// ComputeFingerprint. Validates that:
        ///   * addr is non-zero (rejects the sentinel "no selection" value)
        ///   * addr is at the normal ROM safety floor
        ///   * addr + BlockSize fits within the ROM (no off-by-one — the
        ///     last block where addr + 12 == rom.Data.Length is accepted).
        /// PR #626 Copilot round 4 blocker #1.
        /// </summary>
        static bool IsValidEntryRange(ROM rom, uint addr)
        {
            if (rom == null) return false;
            if (addr == 0) return false;
            // Inline the safety-floor check against the provided rom (not
            // CoreState.ROM) so this helper stays correct even when callers
            // pass a non-global ROM instance — PR #626 round 5 finding.
            // Mirrors U.isSafetyOffset's logic: addr in [0x200, rom.Length).
            uint length = (uint)rom.Data.Length;
            if (addr < 0x00000200) return false;
            if (addr >= 0x02000000) return false;
            if (addr >= length) return false;
            return addr + BlockSize <= length;
        }

        static string ComputeDirectSoundFingerprint(ROM rom, uint vocaaddr)
        {
            var data = new List<byte>();
            // Skip B0 (the header byte — used for type switching, not data).
            data.Add((byte)rom.u8(vocaaddr + 1));
            data.Add((byte)rom.u8(vocaaddr + 2));
            data.Add((byte)rom.u8(vocaaddr + 3));
            // B4..B7 = wave pointer (excluded from hash; the pointer target
            // is hashed below).
            data.Add((byte)rom.u8(vocaaddr + 8));
            data.Add((byte)rom.u8(vocaaddr + 9));
            data.Add((byte)rom.u8(vocaaddr + 10));
            data.Add((byte)rom.u8(vocaaddr + 11));

            uint songdataAddr = rom.p32(vocaaddr + 4);
            if (!U.isSafetyOffset(songdataAddr)) return string.Empty;
            if (!U.isSafetyOffset(songdataAddr + 16)) return string.Empty;

            // Match WF SongInstrumentForm.DirectoSoundFingerPrint: hash the
            // sample data slice starting at songdata+12, length derived from
            // the songdata header (DPCM-compressed instruments use a
            // different length formula). PR #626 round 4 blocker #2.
            uint sampleLength = GetDirectSoundWaveDataLength(rom, songdataAddr);
            if (sampleLength == 0) return string.Empty;
            if (!U.isSafetyLength(songdataAddr + 12 + 4, sampleLength)) return string.Empty;

            byte[] waveData = rom.getBinaryData(songdataAddr + 12, sampleLength);
            data.AddRange(waveData);

            return ComputeMd5Hex(data.ToArray());
        }

        /// <summary>
        /// Compute the GBA DirectSound wave-data length given the songdata
        /// pointer. Mirrors WF `SongUtil.GetDirectSoundWaveDataLength` for
        /// both uncompressed and DPCM-compressed samples. The compressed
        /// formula is `33 * ceil(uncompressed_length / 64)`.
        /// </summary>
        static uint GetDirectSoundWaveDataLength(ROM rom, uint songdataAddr)
        {
            uint sampleLength = rom.u32(songdataAddr + 12);
            // DPCM compression flag lives in the songdata header's first
            // byte: 0x01 == DPCM, 0x00 == uncompressed PCM.
            byte head1 = (byte)rom.u8(songdataAddr + 0);
            bool isDpcm = head1 == 0x01;
            if (!isDpcm) return sampleLength;

            uint div64 = sampleLength / 64;
            if (sampleLength % 64 != 0) div64++;
            return 33 * div64;
        }

        static string ComputeWaveMemoryFingerprint(ROM rom, uint vocaaddr)
        {
            var data = new List<byte>();
            data.Add((byte)rom.u8(vocaaddr + 1));
            data.Add((byte)rom.u8(vocaaddr + 2));
            data.Add((byte)rom.u8(vocaaddr + 3));
            data.Add((byte)rom.u8(vocaaddr + 8));
            data.Add((byte)rom.u8(vocaaddr + 9));
            data.Add((byte)rom.u8(vocaaddr + 10));
            data.Add((byte)rom.u8(vocaaddr + 11));

            uint songdataAddr = rom.p32(vocaaddr + 4);
            if (!U.isSafetyOffset(songdataAddr)) return string.Empty;
            if (!U.isSafetyOffset(songdataAddr + 12)) return string.Empty;

            byte[] fixedData = rom.getBinaryData(songdataAddr, 12);
            data.AddRange(fixedData);

            return ComputeMd5Hex(data.ToArray());
        }

        static string ComputeRawSliceFingerprint(ROM rom, uint addr, uint length)
        {
            if (!U.isSafetyLength(addr, length)) return string.Empty;
            byte[] data = rom.getBinaryData(addr, length);
            return ComputeMd5Hex(data);
        }

        static string ComputeMd5Hex(byte[] data)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] bs = md5.ComputeHash(data);
                var sb = new System.Text.StringBuilder(bs.Length * 2);
                foreach (var b in bs) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            uint effectiveBase = BaseAddr;
            if (effectiveBase == 0)
                effectiveBase = ResolveFirstSongVoicegroup(rom);
            if (effectiveBase == 0) return 0;

            return CountDefinedPrefix(rom, effectiveBase);
        }

        /// <summary>
        /// Count the defined-prefix instrument rows starting at
        /// <paramref name="baseAddr"/>: walk 12-byte blocks (max 128) and stop
        /// at the first row that fails <see cref="IsValidInstrument"/> — the
        /// SAME validity predicate the master list (<see cref="LoadInstrumentList"/>)
        /// and WF <c>SongInstrumentForm</c>'s read-loop use to decide a row is a
        /// real instrument. Used by both <see cref="GetListCount"/> and the
        /// "Expand List" (→ 128) operation (#780).
        /// </summary>
        static int CountDefinedPrefix(ROM rom, uint baseAddr)
        {
            int count = 0;
            for (int i = 0; i < MaxInstruments; i++)
            {
                uint addr = baseAddr + (uint)(i * BlockSize);
                if (addr + BlockSize > (uint)rom.Data.Length) break;
                byte type = (byte)rom.u8(addr);
                if (!IsValidInstrument(rom, addr, type)) break;
                count++;
            }
            return count;
        }

        // -----------------------------------------------------------------
        // "Expand List" — grow the voicegroup (instrument set) to 128 records
        // (#780). Mirrors WF SongInstrumentForm AddressListExpandsButton_128 /
        // InputFormRef.ExpandsArea(FIRST, ...): relocate the block to free
        // space, copy the defined prefix verbatim, fill the gap rows from the
        // row-0 template (last row left zero — WF "末尾は埋めてはいけない"),
        // then repoint EVERY song-header reference via
        // DataExpansionCore.RepointAllReferences (the #782 shared-voicegroup
        // win — single-slot repoint would corrupt the other songs).
        // -----------------------------------------------------------------

        /// <summary>Record whether the loaded base is a song-referenced voicegroup.</summary>
        void SetSongContext(bool hasContext, uint baseAddr)
        {
            _hasSongContext = hasContext;
            _songContextBase = hasContext ? U.toOffset(baseAddr) : 0;
            OnPropertyChanged(nameof(HasSongContext));
            OnPropertyChanged(nameof(CanExpandVoicegroup));
        }

        /// <summary>
        /// True when the currently-loaded base is a real voicegroup reachable
        /// from a song header (so Expand List is meaningful). The View gates
        /// the Expand List button on <see cref="CanExpandVoicegroup"/>.
        /// </summary>
        public bool HasSongContext => _hasSongContext;

        /// <summary>
        /// True when Expand List should be enabled: a song-context voicegroup
        /// is loaded AND its defined-prefix instrument count is &lt; 128
        /// (already-full sets cannot grow).
        /// </summary>
        public bool CanExpandVoicegroup
        {
            get
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return false;
                if (!_hasSongContext || _songContextBase == 0) return false;
                if (!U.isSafetyOffset(_songContextBase, rom)) return false;
                int defined = CountDefinedPrefix(rom, _songContextBase);
                return defined >= 1 && defined < MaxInstruments;
            }
        }

        /// <summary>
        /// Grow the current song's voicegroup (instrument set) to the full 128
        /// 12-byte records. Returns true on success. All ROM writes happen
        /// under the ambient undo scope opened by the caller (the View's
        /// UndoService) or the passed <paramref name="undo"/>; on any refusal
        /// the method makes NO mutation and returns false so the caller rolls
        /// back cleanly.
        ///
        /// <para>Steps (accepted v3 plan + CLI refinement):</para>
        /// <list type="number">
        ///   <item>Resolve <c>oldBase</c> = the song-context voicegroup base.
        ///         No song context → false (no mutation).</item>
        ///   <item><c>currentCount</c> = defined prefix via
        ///         <see cref="CountDefinedPrefix"/> (same validity predicate as
        ///         the master list).</item>
        ///   <item><c>currentCount == 0</c> → false (no template row to copy —
        ///         CLI refinement; never synthesize zero rows).</item>
        ///   <item><c>currentCount &gt;= 128</c> → false (already full).</item>
        ///   <item>Build the 1536-byte (128×12) block: copy the prefix verbatim,
        ///         fill rows <c>[currentCount..127)</c> from the row-0 template
        ///         (NOT zero), leave row 127 zero — mirrors WF
        ///         <c>ExpandsArea(FIRST)</c> "tail must not be filled".</item>
        ///   <item>Allocate the block in free space → <c>newBase</c>. Alloc
        ///         failure (<see cref="U.NOT_FOUND"/>) → false.</item>
        ///   <item><c>RepointAllReferences(oldBase → newBase)</c>. Repointed
        ///         &lt; 1 → false (don't leave an orphan).</item>
        /// </list>
        /// </summary>
        public bool ExpandVoicegroupTo128(Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;

            // (1) Require a real song-context voicegroup slot.
            if (!_hasSongContext || _songContextBase == 0) return false;
            uint oldBase = _songContextBase; // offset form
            if (!U.isSafetyOffset(oldBase, rom)) return false;

            // (2) Defined-prefix count (same predicate as the master list).
            int currentCount = CountDefinedPrefix(rom, oldBase);

            // (3) CLI refinement — no valid template row to copy.
            if (currentCount == 0) return false;

            // (4) Already full.
            if (currentCount >= MaxInstruments) return false;

            // Bounds: the existing prefix must fit in ROM.
            uint definedSize = (uint)(currentCount * BlockSize);
            if (oldBase + definedSize > (uint)rom.Data.Length) return false;

            // (5) Build the 128×12 block. Copy the defined prefix verbatim,
            // then fill rows [currentCount..127) from the row-0 template. Row
            // 127 (the last) is left zero — WF ExpandsArea(FIRST) explicitly
            // does NOT fill the tail row ("末尾は埋めてはいけない").
            const int FullSize = MaxInstruments * BlockSize; // 1536
            byte[] block = new byte[FullSize];
            byte[] prefix = rom.getBinaryData(oldBase, definedSize);
            Array.Copy(prefix, 0, block, 0, (int)definedSize);

            // Row-0 template (always valid: it's the first row of the defined
            // prefix, currentCount >= 1).
            byte[] template = new byte[BlockSize];
            Array.Copy(block, 0, template, 0, BlockSize);

            // Fill the gap rows [currentCount .. 127) — stop BEFORE the last
            // row so row 127 stays zero (WF tail rule).
            for (int r = currentCount; r < MaxInstruments - 1; r++)
                Array.Copy(template, 0, block, r * BlockSize, BlockSize);

            // (6) Allocate the new block in free space.
            uint newAddr = AppendBinaryDataHeadless(rom, block);
            if (newAddr == U.NOT_FOUND) return false;

            // (7) Repoint EVERY song-header (+LDR) reference to the shared
            // voicegroup. < 1 slot → orphan; refuse so the caller rolls back.
            int n = DataExpansionCore.RepointAllReferences(rom, oldBase, newAddr, undo);
            if (n < 1) return false;

            // Re-anchor onto the relocated base so a subsequent reload /
            // re-expand sees the new address. BaseAddr is the voicegroup base
            // (offset form, as set by LoadInstrumentList / ResolveFirst…), and
            // after relocation it genuinely IS newAddr — update it so the
            // View's LoadList() explicit-base path lists the new 128-row block.
            BaseAddr = newAddr;
            SetSongContext(true, newAddr);
            return true;
        }

        /// <summary>
        /// Headless equivalent of <c>InputFormRef.AppendBinaryData</c>. Routes
        /// through the registered <see cref="CoreState.AppendBinaryData"/>
        /// delegate (WinForms wires the real freespace allocator; the same
        /// delegate serves the Avalonia editor). Returns <see cref="U.NOT_FOUND"/>
        /// when no allocator is wired (headless tests) or no ambient undo scope
        /// is active. Mirrors the EventCondViewModel.AppendBinaryDataHeadless
        /// pattern.
        /// </summary>
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer)
        {
            var allocator = CoreState.AppendBinaryData;
            if (allocator == null) return U.NOT_FOUND;
            var ambient = ROM.GetAmbientUndoData();
            if (ambient == null) return U.NOT_FOUND;
            return allocator(buffer, ambient);
        }

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
                // All 12 raw bytes — LoadEntry reads each one and the View
                // exposes them per-tab (the WF designer surfaces every byte
                // from B1..B11 across tabs). The full raw report keeps the
                // AvaloniaFieldCompletenessTests coverage check satisfied
                // (PR #626 CI run after round-4: SongInstrumentViewModel
                // had 16 ROM reads but only 9 raw entries == 56%, below the
                // 80% floor).
                ["u8@0x00_Header"] = $"0x{rom.u8(a):X02}",
                ["u8@0x01_B1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_B2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_B3"] = $"0x{rom.u8(a + 3):X02}",
                // Offset 4 is u32 in DirectSound / WaveMemory / MultiSample
                // / Drum (pointer), but B4 raw byte in SquareWave / Noise
                // tabs. Surface both interpretations.
                ["u8@0x04_B4"] = $"0x{rom.u8(a + 4):X02}",
                ["u32@0x04_P4"] = $"0x{rom.u32(a + 4):X08}",
                ["u8@0x05_B5"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06_B6"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_B7"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08_B8"] = $"0x{rom.u8(a + 8):X02}",
                ["u32@0x08_P8"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x09_B9"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_B10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_B11"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
