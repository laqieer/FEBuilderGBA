using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTrackImportMidiViewModel : ViewModelBase
    {
        uint _currentAddr;            // selected song HEADER offset
        uint _songTableEntryAddr;     // selected song-table ENTRY pointer slot
        uint _instrumentAddr;         // raw GBA pointer from song header +4
        bool _isLoaded;
        string _midiFilePath = string.Empty;
        string _midiInfoText = string.Empty;
        bool _hasMidiInfo;

        /// <summary>Selected song HEADER offset (0 when nothing selected).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        /// <summary>
        /// Selected song-table ENTRY pointer slot — the address
        /// <see cref="SongMidiCore.ImportMidiFile"/> repoints. 0 when no real
        /// song is selected.
        /// </summary>
        public uint SongTableEntryAddr { get => _songTableEntryAddr; set => SetField(ref _songTableEntryAddr, value); }
        /// <summary>Raw GBA instrument-set pointer from the song header +4.</summary>
        public uint InstrumentAddr { get => _instrumentAddr; set => SetField(ref _instrumentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Path to the selected MIDI file.</summary>
        public string MidiFilePath { get => _midiFilePath; set => SetField(ref _midiFilePath, value); }
        /// <summary>Formatted MIDI metadata text for display.</summary>
        public string MidiInfoText { get => _midiInfoText; set => SetField(ref _midiInfoText, value); }
        /// <summary>Whether MIDI metadata has been successfully parsed.</summary>
        public bool HasMidiInfo { get => _hasMidiInfo; set => SetField(ref _hasMidiInfo, value); }

        /// <summary>
        /// Enumerate the real song table so the user can pick the destination
        /// song for the MIDI import. Each entry's <c>addr</c> is the song
        /// HEADER offset and <c>tag</c> is the song-table index (songId).
        /// Mirrors <see cref="SongTrackViewModel.LoadFullList"/>.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint tablePtr = rom.RomInfo.sound_table_pointer;
            if (tablePtr == 0) return new List<AddrResult>();

            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < 512; i++)
            {
                uint entryAddr = (uint)(tableBase + (uint)i * 8u);
                if (entryAddr + 8 > romLen) break;

                uint headerPtr = rom.u32(entryAddr);
                if (!U.isPointer(headerPtr)) break;

                uint headerAddr = U.toOffset(headerPtr);
                if (!U.isSafetyOffset(headerAddr) || headerAddr + 8 > romLen)
                    continue;

                string songName = NameResolver.GetSongName((uint)i);
                string name = string.IsNullOrEmpty(songName)
                    ? $"0x{i:X02} Song {i}"
                    : $"0x{i:X02} {songName}";
                result.Add(new AddrResult(headerAddr, name, (uint)i));
            }

            return result;
        }

        /// <summary>
        /// Resolve the selected song header + instrument + table-entry slot.
        /// <paramref name="addr"/> is the song HEADER offset (the
        /// <c>AddrResult.addr</c> from <see cref="LoadList"/>); the matching
        /// table-entry slot is located by walking the song table for the entry
        /// whose dereferenced pointer equals this header.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            SongTableEntryAddr = 0;
            InstrumentAddr = 0;
            IsLoaded = false;

            if (addr == 0 || addr + 8 > (uint)rom.Data.Length) return;

            // Instrument pointer lives at song header +4 (raw GBA pointer).
            InstrumentAddr = rom.u32(addr + 4);

            // Locate the song-table entry slot that points at this header.
            SongTableEntryAddr = FindTableEntryForHeader(rom, addr);
            IsLoaded = SongTableEntryAddr != 0;
        }

        /// <summary>
        /// Walk the song table for the entry pointer slot that dereferences to
        /// <paramref name="headerAddr"/>. Returns 0 when not found.
        /// </summary>
        static uint FindTableEntryForHeader(ROM rom, uint headerAddr)
        {
            if (rom?.RomInfo == null) return 0;
            uint tablePtr = rom.RomInfo.sound_table_pointer;
            if (tablePtr == 0) return 0;
            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase)) return 0;

            uint headerPtr = headerAddr + 0x08000000;
            uint romLen = (uint)rom.Data.Length;
            for (int i = 0; i < 512; i++)
            {
                uint entryAddr = (uint)(tableBase + (uint)i * 8u);
                if (entryAddr + 8 > romLen) break;
                uint p = rom.u32(entryAddr);
                if (!U.isPointer(p)) break;
                if (p == headerPtr) return entryAddr;
            }
            return 0;
        }

        /// <summary>
        /// Parse a MIDI file and populate metadata properties for display.
        /// Returns null on success, or an error message on failure.
        /// </summary>
        public string? ParseMidiInfo(string filename)
        {
            if (!File.Exists(filename))
                return $"File not found: {filename}";

            var info = SongMidiCore.ParseMidiFile(filename);
            if (info == null)
                return "Failed to parse MIDI file -- invalid format.";

            MidiFilePath = filename;
            MidiInfoText = FormatMidiMetadata(info, filename);
            HasMidiInfo = true;
            return null;
        }

        /// <summary>
        /// Import the previously-parsed MIDI file into the selected song.
        /// Converts MIDI to GBA, appends to ROM free space, and repoints the
        /// song-table entry slot. Returns null on success (with
        /// <paramref name="summary"/> set), or an error string on failure.
        /// </summary>
        /// <remarks>
        /// The caller MUST wrap this in an ambient undo scope
        /// (<c>UndoService.Begin/Commit/Rollback</c>) so the underlying ROM
        /// writes are captured as a single, fully-reversible undo record.
        /// </remarks>
        public string? ImportMidi(out string summary)
        {
            summary = string.Empty;

            ROM rom = CoreState.ROM;
            if (rom == null)
                return "No ROM loaded.";
            if (!IsLoaded || SongTableEntryAddr == 0)
                return "No song selected. Pick a destination song from the list first.";
            if (!HasMidiInfo || string.IsNullOrEmpty(MidiFilePath))
                return "No MIDI file selected. Use 'Browse MIDI File...' first.";
            if (!File.Exists(MidiFilePath))
                return $"File not found: {MidiFilePath}";

            var midiInfo = SongMidiCore.ParseMidiFile(MidiFilePath);
            if (midiInfo == null)
                return "Failed to parse MIDI file -- invalid format.";

            string result = SongMidiCore.ImportMidiFile(MidiFilePath, SongTableEntryAddr, InstrumentAddr);
            if (!string.IsNullOrEmpty(result))
                return result;

            // Reload from the freshly-repointed slot to reflect the new song.
            uint newHeaderPtr = rom.u32(SongTableEntryAddr);
            if (U.isPointer(newHeaderPtr))
                LoadEntry(U.toOffset(newHeaderPtr));

            summary = FormatMidiImportSuccess(midiInfo, MidiFilePath);
            return null;
        }

        /// <summary>Build a human-readable summary of a successful MIDI import.</summary>
        static string FormatMidiImportSuccess(SongMidiCore.MidiFileInfo info, string filename)
        {
            int totalNotes = 0;
            foreach (var t in info.Tracks)
                totalNotes += t.NoteCount;

            var sb = new StringBuilder();
            sb.AppendLine($"MIDI imported: {Path.GetFileName(filename)}");
            sb.AppendLine($"  Format: {info.Format}");
            sb.AppendLine($"  Tracks: {info.TrackCount}");
            sb.AppendLine($"  Tempo: {info.TempoBPM:F1} BPM");
            sb.AppendLine($"  Total notes: {totalNotes}");
            sb.AppendLine();
            sb.Append("Song data written to ROM successfully.");
            return sb.ToString();
        }

        /// <summary>Build a human-readable summary of MIDI file metadata.</summary>
        internal static string FormatMidiMetadata(SongMidiCore.MidiFileInfo info, string filename)
        {
            int totalNotes = 0;
            foreach (var t in info.Tracks)
                totalNotes += t.NoteCount;

            int totalEvents = 0;
            foreach (var t in info.Tracks)
                totalEvents += t.EventCount;

            var sb = new StringBuilder();
            sb.AppendLine($"File: {Path.GetFileName(filename)}");
            sb.AppendLine($"MIDI Format: {info.Format}");
            sb.AppendLine($"Track Count: {info.TrackCount}");
            sb.AppendLine($"Ticks/Quarter Note: {info.TicksPerQuarterNote}");
            sb.AppendLine($"Tempo: {info.TempoBPM:F1} BPM");
            sb.AppendLine($"Total Notes: {totalNotes}");
            sb.AppendLine($"Total Events: {totalEvents}");
            sb.AppendLine();

            // Estimate duration from first track's total ticks
            if (info.Tracks.Count > 0 && info.TicksPerQuarterNote > 0 && info.TempoBPM > 0)
            {
                int maxTicks = 0;
                foreach (var t in info.Tracks)
                    if (t.TotalTicks > maxTicks) maxTicks = t.TotalTicks;
                double seconds = (double)maxTicks / info.TicksPerQuarterNote * (60.0 / info.TempoBPM);
                int mins = (int)(seconds / 60);
                int secs = (int)(seconds % 60);
                sb.AppendLine($"Estimated Duration: {mins}:{secs:D2}");
                sb.AppendLine();
            }

            for (int i = 0; i < info.Tracks.Count; i++)
            {
                var t = info.Tracks[i];
                sb.Append($"  Track {t.Index}: {t.NoteCount} notes, {t.EventCount} events, {t.TotalTicks} ticks");
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

            return sb.ToString().TrimEnd();
        }
    }
}
