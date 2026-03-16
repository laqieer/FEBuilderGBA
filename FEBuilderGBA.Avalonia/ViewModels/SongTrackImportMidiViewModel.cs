using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTrackImportMidiViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _midiFilePath = string.Empty;
        string _midiInfoText = string.Empty;
        bool _hasMidiInfo;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Path to the selected MIDI file.</summary>
        public string MidiFilePath { get => _midiFilePath; set => SetField(ref _midiFilePath, value); }
        /// <summary>Formatted MIDI metadata text for display.</summary>
        public string MidiInfoText { get => _midiInfoText; set => SetField(ref _midiInfoText, value); }
        /// <summary>Whether MIDI metadata has been successfully parsed.</summary>
        public bool HasMidiInfo { get => _hasMidiInfo; set => SetField(ref _hasMidiInfo, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "MIDI Import", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
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
