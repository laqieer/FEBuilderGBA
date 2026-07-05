using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core; // SongInstrumentSetCore (#1609)

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

    public partial class SongTrackViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Find songs from the song table and load the first valid one
            uint tablePtr = rom.RomInfo.sound_table_pointer;
            if (tablePtr == 0) return new List<AddrResult>();

            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            uint romLen = (uint)rom.Data.Length;

            // Walk the song table (each entry = 8 bytes: 4-byte pointer + 4-byte player type)
            for (int i = 0; i < 512; i++)
            {
                uint entryAddr = (uint)(tableBase + i * 8);
                if (entryAddr + 8 > romLen) break;

                uint headerPtr = rom.u32(entryAddr);
                if (!U.isPointer(headerPtr)) break;

                uint headerAddr = U.toOffset(headerPtr);
                if (!U.isSafetyOffset(headerAddr) || headerAddr + 8 > romLen)
                    continue;

                // Read track count from the song header
                uint trackCount = rom.u8(headerAddr);
                if (trackCount == 0 || trackCount > 16) continue;

                // Use the real song name (same as LoadFullList below).
                string songName = NameResolver.GetSongName((uint)i);
                string name = string.IsNullOrEmpty(songName)
                    ? $"0x{i:X02} Song {i}"
                    : $"0x{i:X02} {songName}";
                result.Add(new AddrResult(headerAddr, name, (uint)i));

                // Load the first valid song for data-verify standalone init
                if (result.Count == 1)
                    LoadEntry(headerAddr);

                // For data-verify we only need one entry
                break;
            }

            return result;
        }

        /// <summary>
        /// Full song-table scan driven by the WF-mirror read-config bar
        /// (`ReadStartAddress` + `ReadCount`). Mirrors the WF
        /// `InputFormRef.MakeList` behavior with adjustable scan window —
        /// pressing the Reload button calls this to rebuild the song list
        /// using the user-edited values. When either field is zero / unset
        /// the call falls back to default behavior (auto-detect base via
        /// `sound_table_pointer`, scan up to 512 entries).
        /// </summary>
        public List<AddrResult> LoadFullList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint tableBase;
            if (ReadStartAddress != 0)
            {
                tableBase = ReadStartAddress;
            }
            else
            {
                uint tablePtr = rom.RomInfo.sound_table_pointer;
                if (tablePtr == 0) return new List<AddrResult>();
                tableBase = rom.p32(tablePtr);
            }
            if (!U.isSafetyOffset(tableBase)) return new List<AddrResult>();

            // Cap scanLimit at int.MaxValue so the int-typed loop counter
            // can compare safely; in practice ReadCount is bounded to 4096
            // via the UI NumericUpDown.Maximum, so any clamp is defensive.
            uint scanLimitUInt = ReadCount > 0 ? ReadCount : 512u;
            int scanLimit = scanLimitUInt > int.MaxValue ? int.MaxValue : (int)scanLimitUInt;
            var result = new List<AddrResult>();
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < scanLimit; i++)
            {
                uint entryAddr = (uint)(tableBase + (uint)i * 8u);
                if (entryAddr + 8 > romLen) break;

                uint headerPtr = rom.u32(entryAddr);
                if (!U.isPointer(headerPtr)) break;

                uint headerAddr = U.toOffset(headerPtr);
                if (!U.isSafetyOffset(headerAddr) || headerAddr + 8 > romLen)
                    continue;

                // Use the real song name from the resource cache (mirrors
                // WF's `SongTableForm.GetSongName((uint)i)` so songs share
                // identical display labels across the master list and the
                // SongTable editor).
                string songName = NameResolver.GetSongName((uint)i);
                string name = string.IsNullOrEmpty(songName)
                    ? $"0x{i:X02} Song {i}"
                    : $"0x{i:X02} {songName}";
                result.Add(new AddrResult(headerAddr, name, (uint)i));
            }

            // Surface defaults back to the UI so the read-config bar
            // populates with the auto-detected values on first load.
            if (ReadStartAddress == 0) ReadStartAddress = tableBase;
            if (ReadCount == 0) ReadCount = (uint)scanLimit;

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
        uint _readStartAddress;
        uint _readCount;

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
        /// <summary>WF panel1 read-config: start address of the song table scan
        /// (mirrors WF `ReadStartAddress` NumericUpDown). Editing this and then
        /// pressing Reload drives <see cref="LoadFullList"/> against the new
        /// base (the View's `LoadList` thin-wrapper delegates to it).</summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        /// <summary>WF panel1 read-config: number of song entries to enumerate
        /// (mirrors WF `ReadCount` NumericUpDown). Editing this and then
        /// pressing Reload caps the song table scan length.</summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

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
        /// Currently-selected song's table index (0..N-1). Surfaces the songId
        /// to the View so SongID-0 write-protection (mirrors WF
        /// `UseWriteProtectionID00 = true`) and the SongExchange jump can read
        /// the right id. -1 = nothing selected.
        /// </summary>
        public int SelectedSongIndex { get; set; } = -1;

        // ---- #1014: Source-file affordance (mirrors WorldMapImageViewModel /
        // ImageBGViewModel). WF SongTrackForm records the imported source-music
        // path under the PER-SONG ResourceCache key "Song_" + hex(songId) — NOT
        // a fixed key, NOT the song-header address (WF SongTrackForm.cs:137-146,
        // 303-305,560-563). Open Source File / Folder become available only when
        // the recorded file exists. ----
        string _sourceFilePath = string.Empty;
        bool _isSourceFileAvailable;
        public string SourceFilePath { get => _sourceFilePath; set => SetField(ref _sourceFilePath, value); }
        public bool IsSourceFileAvailable { get => _isSourceFileAvailable; set => SetField(ref _isSourceFileAvailable, value); }

        /// <summary>Build the per-song ResourceCache key (WF "Song_" + hex(songId)).</summary>
        static string SourceFileResourceKey(uint songId) => "Song_" + U.ToHexString(songId);

        /// <summary>
        /// Re-read the recorded source-music path for <paramref name="songId"/>
        /// from ResourceCache (WF per-song "Song_" + hex(songId) key). Called
        /// after each song selection so Open Source File / Folder light up for
        /// the selected song only.
        /// </summary>
        public void RefreshSourceFile(uint songId)
        {
            var cache = CoreState.ResourceCache as FEBuilderGBA.EtcCacheResource;
            if (cache == null) { IsSourceFileAvailable = false; SourceFilePath = string.Empty; return; }
            string path = cache.At(SourceFileResourceKey(songId), string.Empty);
            SourceFilePath = path ?? string.Empty;
            IsSourceFileAvailable = !string.IsNullOrEmpty(SourceFilePath) && System.IO.File.Exists(SourceFilePath);
        }

        /// <summary>
        /// Record a new source-music path for <paramref name="songId"/> after a
        /// successful MIDI import (WF SongTrackForm ImportButton_Click records
        /// the picked file under "Song_" + hex(songId)).
        /// </summary>
        public void RecordSourceFile(uint songId, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var cache = CoreState.ResourceCache as FEBuilderGBA.EtcCacheResource;
            if (cache == null) return;
            cache.Update(SourceFileResourceKey(songId), path);
            SourceFilePath = path;
            IsSourceFileAvailable = System.IO.File.Exists(path);
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
        /// Export the current song's instrument set (voicegroup) to a
        /// <c>.instrument</c> TSV index + per-voice side files (#1609). Mirrors
        /// WinForms <c>SongTrackForm.cs:459-466</c> → <c>SongUtil.ExportInstrument</c>
        /// (which dispatches to <c>SongInstrumentForm.ExportAllLow</c>) and reuses
        /// the SAME Core seam (<see cref="SongInstrumentSetCore.ExportAll"/>) the
        /// Song Instrument editor already calls. READ-ONLY — no ROM mutation, no undo.
        ///
        /// The instrument-set pointer is <see cref="InstrumentAddr"/>, i.e.
        /// <c>rom.u32(songHeader + 4)</c> = WinForms <c>P4.Value</c>.
        /// Returns null on success, or an error message on failure (never throws).
        /// </summary>
        public string? ExportInstrumentSet(string indexPath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !IsLoaded || CurrentAddr == 0)
                return "No song loaded.";
            if (string.IsNullOrEmpty(indexPath))
                return "No output path chosen.";
            if (InstrumentAddr == 0)
                return "This song has no instrument set (P4 is null).";

            // (Copilot plan review pt 1) SongInstrumentSetCore.ExportAll is a void
            // API that SILENTLY no-ops on an unsafe base offset
            // (SongInstrumentSetCore.cs:117-121). An edited/stale nonzero P4 would
            // otherwise produce a false-success toast with no file written —
            // prevalidate the pointer here and reject explicitly.
            if (!U.isSafetyOffset(U.toOffset(InstrumentAddr), rom))
                return $"Instrument set pointer 0x{InstrumentAddr:X08} is out of range.";

            try
            {
                string dir = Path.GetDirectoryName(indexPath) ?? ".";
                string baseName = Path.GetFileNameWithoutExtension(indexPath);

                // The Core emits RELATIVE filename tokens; resolve each against the
                // chosen index file's directory so all side + nested files land next
                // to the .instrument index (verbatim mirror of
                // SongInstrumentView.axaml.cs:682-689).
                SongInstrumentSetCore.ExportAll(
                    rom, InstrumentAddr, baseName,
                    (name, bytes) => File.WriteAllBytes(Path.Combine(dir, name), bytes),
                    (name, lines) => File.WriteAllLines(Path.Combine(dir, name), lines));

                // (Copilot plan review pt 1, defense-in-depth) Verify the index file
                // Core emits (baseName + ".instrument") actually exists before
                // reporting success — guards against any remaining silent no-op.
                string emitted = Path.Combine(dir, baseName + ".instrument");
                if (!File.Exists(emitted))
                    return "Instrument set export produced no file (the song's instrument set may be empty or invalid).";

                return null; // success
            }
            catch (System.Exception ex)
            {
                return $"Instrument set export failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Preview a MIDI file by parsing its metadata without writing to ROM.
        /// Returns a formatted metadata string, or an error message prefixed with "Error:".
        /// </summary>
        public string PreviewMidi(string filename)
        {
            if (!File.Exists(filename))
                return $"Error: File not found: {filename}";

            var midiInfo = SongMidiCore.ParseMidiFile(filename);
            if (midiInfo == null)
                return "Error: Failed to parse MIDI file -- invalid format.";

            return SongTrackImportMidiViewModel.FormatMidiMetadata(midiInfo, filename);
        }

        /// <summary>
        /// Resolve the active song-table base offset. Mirrors
        /// <see cref="LoadFullList"/>: uses the user-edited
        /// <see cref="ReadStartAddress"/> when set (so a custom scan base is
        /// honoured), else derefs <c>RomInfo.sound_table_pointer</c>. Returns
        /// <see cref="U.NOT_FOUND"/> when no valid base can be resolved.
        /// </summary>
        uint ResolveSongTableBase()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint tableBase;
            if (ReadStartAddress != 0)
            {
                tableBase = ReadStartAddress;
            }
            else
            {
                uint tablePtr = rom.RomInfo.sound_table_pointer;
                if (tablePtr == 0) return U.NOT_FOUND;
                tableBase = rom.p32(tablePtr);
            }
            return U.isSafetyOffset(tableBase) ? tableBase : U.NOT_FOUND;
        }

        /// <summary>
        /// Compute the ROM offset of the 4-byte song-table ENTRY pointer slot
        /// for the currently-selected song. This is the slot
        /// <see cref="SongMidiCore.ImportMidiFile"/> repoints (the CLI
        /// <c>--import-midi</c> passes the same <c>tableBase + songId*8</c>
        /// address), NOT the dereferenced song-header offset. Returns
        /// <see cref="U.NOT_FOUND"/> when no song is selected or the slot is
        /// out of range. Each table entry is 8 bytes (4-byte header pointer +
        /// 4-byte player type).
        /// </summary>
        public uint GetSelectedSongTableEntryAddr()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SelectedSongIndex < 0) return U.NOT_FOUND;

            uint tableBase = ResolveSongTableBase();
            if (tableBase == U.NOT_FOUND) return U.NOT_FOUND;

            // Compute in long so a large SelectedSongIndex can't overflow/wrap
            // a uint into an in-range-looking-but-wrong offset (which would make
            // ImportMidi repoint the WRONG ROM address). Reject anything whose
            // 8-byte entry would not fit fully inside rom.Data.
            long entryAddrLong = (long)tableBase + (long)SelectedSongIndex * 8L;
            if (entryAddrLong + 8 > rom.Data.Length) return U.NOT_FOUND;

            uint entryAddr = (uint)entryAddrLong;
            if (!U.isSafetyOffset(entryAddr)) return U.NOT_FOUND;
            return entryAddr;
        }

        /// <summary>
        /// Import a MIDI file into the currently-selected song.
        /// Converts MIDI to GBA format, appends it to ROM free space, and
        /// repoints the song-table entry slot at the new song header.
        /// Returns <c>null</c> on success (with <paramref name="summary"/> set
        /// to a human-readable import report), or an error message string on
        /// failure (with <paramref name="summary"/> empty).
        /// </summary>
        /// <remarks>
        /// The caller MUST wrap this in an ambient undo scope
        /// (<c>UndoService.Begin/Commit/Rollback</c>) so the
        /// <c>rom.write_range</c> + <c>rom.write_u32</c> performed by
        /// <see cref="SongMidiCore.ImportMidiFile"/> are captured as a single
        /// undo record. <c>FindFreeSpace</c> never resizes <c>rom.Data</c>,
        /// so a Rollback restores the ROM byte-identical.
        /// </remarks>
        public string? ImportMidi(string filename, out string summary)
        {
            summary = string.Empty;

            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0)
                return "No song loaded.";

            // WF parity: SongID 0 is write-protected (UseWriteProtectionID00) —
            // never repoint the silence song's table slot (mirrors Write_Click).
            if (SelectedSongIndex == 0)
                return "Song ID 0 is write-protected (silence song).";

            if (!File.Exists(filename))
                return $"File not found: {filename}";

            // Resolve the song-table entry pointer slot (the address
            // ImportMidiFile repoints). Using the header offset here would
            // clobber the old header's first 4 bytes instead of redirecting
            // the table entry, so resolve the real slot.
            uint songTableEntryAddr = GetSelectedSongTableEntryAddr();
            if (songTableEntryAddr == U.NOT_FOUND)
                return "Cannot resolve the song-table entry for the selected song.";

            // Parse MIDI info for summary
            var midiInfo = SongMidiCore.ParseMidiFile(filename);
            if (midiInfo == null)
                return "Failed to parse MIDI file -- invalid format.";

            // Convert and write to ROM. InstrumentAddr is the raw GBA pointer
            // read from the song header +4 (matches the CLI contract).
            string result = SongMidiCore.ImportMidiFile(filename, songTableEntryAddr, InstrumentAddr);
            if (!string.IsNullOrEmpty(result))
                return result; // error message

            // Reload the entry from the freshly-repointed table slot so the UI
            // reflects the new song header / track data.
            uint newHeaderPtr = rom.u32(songTableEntryAddr);
            if (U.isPointer(newHeaderPtr))
                LoadEntry(U.toOffset(newHeaderPtr));

            summary = FormatMidiImportSuccess(midiInfo, filename);
            return null; // success
        }

        /// <summary>
        /// Convenience overload for callers that only need the success/error
        /// signal (returns the error string on failure, or the success summary
        /// on success). Prefer the <c>out summary</c> overload in handlers that
        /// must distinguish success from failure for undo Commit/Rollback.
        /// </summary>
        public string? ImportMidi(string filename)
        {
            string? error = ImportMidi(filename, out string summary);
            return error ?? summary;
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
            sb.Append("Song data written to ROM successfully.");
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

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TrackCount"] = "TrackCount@0x00",
            ["NumBlks"] = "NumBlks@0x01",
            ["Priority"] = "Priority@0x02",
            ["Reverb"] = "Reverb@0x03",
            ["InstrumentAddr"] = "InstrumentAddr@0x04",
        };
    }
}
