using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Information about a single track within a song.</summary>
    public class TrackInfo
    {
        /// <summary>Zero-based track index.</summary>
        public int Index { get; set; }
        /// <summary>Display label (1-based).</summary>
        public string Label => $"Track {Index + 1}";
        /// <summary>ROM offset where the 4-byte track pointer is stored.</summary>
        public uint PointerOffset { get; set; }
        /// <summary>Raw 32-bit value read from the pointer slot.</summary>
        public uint RawPointer { get; set; }
        /// <summary>Resolved ROM offset of the track data (0 if invalid).</summary>
        public uint DataOffset { get; set; }
        /// <summary>Whether the pointer is a valid GBA ROM pointer.</summary>
        public bool IsValid { get; set; }
        /// <summary>Human-readable status string.</summary>
        public string Status => IsValid
            ? $"0x{DataOffset:X08}"
            : $"Invalid (0x{RawPointer:X08})";
        /// <summary>Combined display text for list UI.</summary>
        public string DisplayText => $"{Label}:  Ptr@0x{PointerOffset:X06}  ->  {Status}";
    }

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
        string _trackInfoText = string.Empty;
        ObservableCollection<TrackInfo> _tracks = new();

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
        /// <summary>Parsed tracks for this song.</summary>
        public ObservableCollection<TrackInfo> Tracks { get => _tracks; set => SetField(ref _tracks, value); }
        /// <summary>Summary text describing all tracks.</summary>
        public string TrackInfoText { get => _trackInfoText; set => SetField(ref _trackInfoText, value); }

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

            ParseTracks(addr, TrackCount);
        }

        /// <summary>Parse individual track pointers from the song header.</summary>
        void ParseTracks(uint songAddr, uint trackCount)
        {
            ROM rom = CoreState.ROM;
            var tracks = new ObservableCollection<TrackInfo>();

            // Cap at 16 tracks (GBA hardware limit)
            uint count = trackCount > 16 ? 16 : trackCount;

            uint romLen = (uint)rom.Data.Length;
            var sb = new StringBuilder();
            sb.AppendLine($"{count} track(s):");

            for (int i = 0; i < count; i++)
            {
                uint ptrOffset = songAddr + 8 + (uint)(i * 4);
                var info = new TrackInfo { Index = i, PointerOffset = ptrOffset };

                if (ptrOffset + 4 > romLen)
                {
                    info.RawPointer = 0;
                    info.DataOffset = 0;
                    info.IsValid = false;
                }
                else
                {
                    uint raw = rom.u32(ptrOffset);
                    info.RawPointer = raw;

                    if (U.isPointer(raw) && U.isSafetyPointer(raw, rom))
                    {
                        info.DataOffset = U.toOffset(raw);
                        info.IsValid = true;
                    }
                    else
                    {
                        info.DataOffset = 0;
                        info.IsValid = false;
                    }
                }

                tracks.Add(info);
                sb.AppendLine($"  {info.DisplayText}");
            }

            Tracks = tracks;
            TrackInfoText = sb.ToString().TrimEnd();
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

        /// <summary>
        /// Export the current song as a MIDI file.
        /// Returns null on success, or an error message on failure.
        /// </summary>
        public string? ExportMidi(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0)
                return "No song loaded.";

            try
            {
                var tracks = SongMidiCore.ParseTracks(rom, CurrentAddr, TrackCount);
                if (tracks.Count == 0)
                    return "No valid tracks found in this song.";

                SongMidiCore.ExportMidiFile(filename, tracks,
                    (int)NumBlks, (int)Priority, (int)Reverb, InstrumentAddr);
                return null; // success
            }
            catch (System.Exception ex)
            {
                return $"MIDI export failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Import a MIDI file into the current song.
        /// Parses the file and returns an informational summary since full import
        /// requires the S-file assembly pipeline (not yet ported from WinForms).
        /// Returns null on success, or a message string (info or error).
        /// </summary>
        public string? ImportMidi(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0)
                return "No song loaded.";

            if (!File.Exists(filename))
                return $"File not found: {filename}";

            // Parse MIDI and show info
            var midiInfo = SongMidiCore.ParseMidiFile(filename);
            if (midiInfo != null)
            {
                return FormatMidiInfo(midiInfo, filename);
            }

            // Fall back to import attempt (will return not-implemented message)
            string result = SongMidiCore.ImportMidiFile(filename, CurrentAddr,
                InstrumentAddr);
            if (string.IsNullOrEmpty(result))
                return null; // success
            return result;
        }

        /// <summary>Build a human-readable summary of parsed MIDI file info.</summary>
        static string FormatMidiInfo(SongMidiCore.MidiFileInfo info, string filename)
        {
            int totalNotes = 0;
            foreach (var t in info.Tracks)
                totalNotes += t.NoteCount;

            var sb = new StringBuilder();
            sb.AppendLine($"MIDI file parsed: {Path.GetFileName(filename)}");
            sb.AppendLine($"  Format: {info.Format}");
            sb.AppendLine($"  Tracks: {info.TrackCount}");
            sb.AppendLine($"  Tempo: {info.TempoBPM:F1} BPM");
            sb.AppendLine($"  Ticks/Quarter: {info.TicksPerQuarterNote}");
            sb.AppendLine($"  Total notes: {totalNotes}");
            sb.AppendLine();

            foreach (var t in info.Tracks)
            {
                sb.Append($"  Track {t.Index}: {t.NoteCount} notes, {t.EventCount} events");
                if (t.Channels.Count > 0)
                {
                    var chList = new List<int>(t.Channels);
                    chList.Sort();
                    sb.Append($", ch={string.Join(",", chList)}");
                }
                if (t.InstrumentChanges.Count > 0)
                    sb.Append($", prog={string.Join(",", t.InstrumentChanges)}");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.Append("Full MIDI import requires the S-file assembly pipeline (not yet ported from WinForms).");
            return sb.ToString();
        }

        public int GetListCount() => (int)Tracks.Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TrackCount"] = $"0x{TrackCount:X02}",
                ["NumBlks"] = $"0x{NumBlks:X02}",
                ["Priority"] = $"0x{Priority:X02}",
                ["Reverb"] = $"0x{Reverb:X02}",
                ["InstrumentAddr"] = $"0x{InstrumentAddr:X08}",
            };
            for (int i = 0; i < Tracks.Count; i++)
            {
                var t = Tracks[i];
                report[$"Track{i}_Pointer"] = $"0x{t.RawPointer:X08}";
                report[$"Track{i}_Valid"] = t.IsValid.ToString();
            }
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["TrackCount@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["NumBlks@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["Priority@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["Reverb@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["InstrumentAddr@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
            uint count = TrackCount > 16 ? 16 : TrackCount;
            for (uint i = 0; i < count; i++)
            {
                uint off = a + 8 + i * 4;
                if (off + 4 <= (uint)rom.Data.Length)
                    report[$"TrackPtr{i}@0x{8 + i * 4:X02}"] = $"0x{rom.u32(off):X08}";
            }
            return report;
        }
    }
}
