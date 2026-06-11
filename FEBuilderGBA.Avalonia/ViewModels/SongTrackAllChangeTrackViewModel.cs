// SPDX-License-Identifier: GPL-3.0-or-later
// Bulk Track Change editor VM (#1015, completed #1002 Slice 1). Mirrors WinForms
// SongTrackAllChangeTrackForm: collect the distinct 0xBD voices used across ALL
// tracks of a song, let the user remap each to a new voice number AND nudge the
// song-wide Volume / Pan / Tempo, then apply every change across every track in
// one undo step.
//
// #1002 Slice 1 completes the previously-deferred Vol/Pan/Tempo nudges: the bulk
// apply now runs SongTrackChangeCore.ApplyTrackChange on EVERY track under the
// CALLER's single ambient undo scope (so a fault rolls back every touched track
// as one action), with full WF parity (TEMPO clamps 0..255, VOL/PAN 0..127).
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>One editable voice-remap row: rewrite every 0xBD code whose
    /// value == <see cref="From"/> to <see cref="To"/>.</summary>
    public class VoiceRow : ViewModelBase
    {
        int _from;
        int _to;
        string _instrumentName = string.Empty;

        // All setters raise PropertyChanged (via SetField) so the
        // UtilityViewModelSweepTests.PropertyChanged_FiresOnSet sweep over every
        // ViewModelBase subclass passes — plain auto-property setters fail it.
        public int From { get => _from; set => SetField(ref _from, value); }
        /// <summary>Editable target voice (0..127). Identity (To == From) is a
        /// no-op when applied.</summary>
        public int To { get => _to; set => SetField(ref _to, value); }
        /// <summary>Resolved instrument name for <see cref="From"/> (or a hex
        /// fallback when no instrument list is available).</summary>
        public string InstrumentName { get => _instrumentName; set => SetField(ref _instrumentName, value); }

        /// <summary>List-row label, e.g. "Voice 5 -> 9 (Strings)".</summary>
        public string Display => $"{R._("Voice")} {From} -> {To}"
            + (string.IsNullOrEmpty(InstrumentName) ? "" : $"  ({InstrumentName})");
    }

    public class SongTrackAllChangeTrackViewModel : ViewModelBase
    {
        uint _songAddr;
        uint _instrumentAddr;
        bool _isLoaded;

        int _dVol;
        int _dPan;
        int _dTempo;
        bool _changeVelocity;

        /// <summary>Song HEADER address (the caller passes this via Navigate so
        /// the editor re-derives tracks + voices straight from the header).</summary>
        public uint SongAddr { get => _songAddr; set => SetField(ref _songAddr, value); }
        /// <summary>Instrument-set pointer (P4) read from the song header +4 —
        /// used to resolve voice -> instrument names.</summary>
        public uint InstrumentAddr { get => _instrumentAddr; set => SetField(ref _instrumentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Song-wide volume delta added to every track's 0xBE VOL
        /// (clamped 0..127 on write). Also drives note velocity when
        /// <see cref="ChangeVelocity"/> is set.</summary>
        public int DVol { get => _dVol; set => SetField(ref _dVol, value); }
        /// <summary>Song-wide pan delta added to every track's 0xBF PAN (clamped 0..127).</summary>
        public int DPan { get => _dPan; set => SetField(ref _dPan, value); }
        /// <summary>Song-wide tempo delta added to every 0xBB TEMPO (clamped 0..255 —
        /// TEMPO is a full byte, NOT 127).</summary>
        public int DTempo { get => _dTempo; set => SetField(ref _dTempo, value); }
        /// <summary>When true (and <see cref="DVol"/> != 0), also nudge note velocities by DVol.</summary>
        public bool ChangeVelocity { get => _changeVelocity; set => SetField(ref _changeVelocity, value); }

        /// <summary>Editable voice-remap rows (one per distinct 0xBD voice).</summary>
        public ObservableCollection<VoiceRow> Rows { get; } = new();

        /// <summary>True when at least one row has an edited target voice
        /// (<c>To != From</c>) OR any Vol/Pan/Tempo delta is set OR a velocity nudge
        /// is requested. The view checks this before opening an undo scope so a
        /// no-op Apply neither mutates nor reports "applied" (#1088). FIXED (#1002
        /// Finding 5): Vol/Pan/Tempo-only edits with no changed voice rows are no
        /// longer treated as a no-op.</summary>
        public bool HasPendingChanges
        {
            get
            {
                foreach (var r in Rows) if (r.To != r.From) return true;
                if (_dVol != 0) return true;
                if (_dPan != 0) return true;
                if (_dTempo != 0) return true;
                if (_changeVelocity && _dVol != 0) return true;
                return false;
            }
        }

        // Backwards-compat alias for older list/jump wiring that read CurrentAddr.
        public uint CurrentAddr { get => _songAddr; set => SetField(ref _songAddr, value); }

        /// <summary>
        /// Build the left-hand list — one AddrResult per distinct voice so the
        /// list reflects real song data, not a placeholder. The standalone
        /// "open" path (no song context) yields an empty list.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            // Only meaningful once a song header has been loaded (the editor is
            // opened via the Song Track jump, which calls LoadEntry first).
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                result.Add(new AddrResult((uint)i, row.Display, (uint)i));
            }
            return result;
        }

        /// <summary>
        /// Load the song at <paramref name="songAddr"/> (a song HEADER address):
        /// derive the distinct 0xBD voices across all tracks and resolve each
        /// voice's instrument name from the instrument set at header +4.
        /// </summary>
        public void LoadEntry(uint songAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            SongAddr = songAddr;
            Rows.Clear();

            // Reset the nudge inputs on (re)load so a fresh open starts from no-op.
            _dVol = 0; _dPan = 0; _dTempo = 0; _changeVelocity = false;
            OnPropertyChanged(nameof(DVol));
            OnPropertyChanged(nameof(DPan));
            OnPropertyChanged(nameof(DTempo));
            OnPropertyChanged(nameof(ChangeVelocity));

            // Read the instrument-set pointer from the header (+4) and normalize
            // to a ROM offset before any instrument-name lookup.
            uint instOffset = 0;
            if (U.isSafetyOffset(U.toOffset(songAddr), rom)
                && (ulong)U.toOffset(songAddr) + 8 <= (ulong)rom.Data.Length)
            {
                InstrumentAddr = rom.u32(U.toOffset(songAddr) + 4);
                instOffset = U.toOffset(InstrumentAddr);
            }

            // Build a voice -> instrument-name lookup once (best-effort; empty
            // when the instrument set can't be resolved).
            var nameByVoice = BuildInstrumentNames(rom, instOffset);

            var voices = SongVoiceChangeCore.GetDistinctVoices(rom, songAddr);
            foreach (var v in voices)
            {
                var row = new VoiceRow { From = v.From, To = v.To };
                if (nameByVoice.TryGetValue(v.From, out string name))
                    row.InstrumentName = name ?? string.Empty;
                Rows.Add(row);
            }

            IsLoaded = true;
        }

        /// <summary>
        /// Resolve voice-number -> instrument display name from the instrument
        /// set at <paramref name="instOffset"/> (a ROM offset). Voice numbers
        /// index directly into the instrument list. Returns an empty map when
        /// the instrument set can't be read (callers fall back to the voice
        /// number alone).
        /// </summary>
        static Dictionary<int, string> BuildInstrumentNames(ROM rom, uint instOffset)
        {
            var map = new Dictionary<int, string>();
            if (rom?.RomInfo == null || instOffset == 0
                || !U.isSafetyOffset(instOffset, rom))
                return map;

            try
            {
                var instVm = new SongInstrumentViewModel();
                var list = instVm.LoadInstrumentList(instOffset);
                for (int i = 0; i < list.Count; i++)
                    map[i] = list[i].name;
            }
            catch
            {
                // Name resolution is best-effort; on any fault the rows just
                // display the voice number.
            }
            return map;
        }

        /// <summary>
        /// Apply every non-identity voice remap AND the song-wide Vol/Pan/Tempo
        /// (and optional velocity) nudges to EVERY track of the song in ROM.
        /// Returns "" on success (including the no-op case where nothing was edited)
        /// or a localized error string. The CALLER owns the ambient undo scope, so
        /// a fault rolls back EVERY touched track as one action (#1002 Slice 1).
        /// </summary>
        public string ApplyChanges()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM is not loaded.");
            if (!IsLoaded || SongAddr == 0) return R._("Song address is invalid.");

            // Build the voice remap list from the edited rows (song-wide; each
            // track only rewrites the voices it actually contains).
            var voices = new List<SongVoiceChangeCore.VoiceChange>();
            foreach (var row in Rows)
            {
                if (row.To != row.From)
                    voices.Add(new SongVoiceChangeCore.VoiceChange { From = row.From, To = row.To });
            }

            bool anyVoice = voices.Count > 0;
            bool anyNudge = _dVol != 0 || _dPan != 0 || _dTempo != 0
                            || (_changeVelocity && _dVol != 0);
            if (!anyVoice && !anyNudge) return ""; // true no-op

            uint songOffset = U.toOffset(SongAddr);
            if (!U.isSafetyOffset(songOffset, rom)) return R._("Song address is invalid.");
            if ((ulong)songOffset + 8 > (ulong)rom.Data.Length) return R._("Song address is invalid.");

            uint trackCount = rom.u8(songOffset);
            if (trackCount > 16) trackCount = 16;
            if ((ulong)songOffset + 8 + (ulong)trackCount * 4 > (ulong)rom.Data.Length)
                return R._("Song track table runs past ROM end.");

            List<SongMidiCore.Track> tracks;
            try { tracks = SongMidiCore.ParseTracks(rom, songOffset, trackCount); }
            catch { return R._("Song track data is corrupt or truncated."); }

            // Apply to EVERY track under the caller's single ambient undo scope.
            // ApplyTrackChange is validate-all-before-mutate per track; on a fault
            // the caller's Rollback undoes every byte already written across all
            // tracks (one action). Stop at the first error so no further tracks
            // mutate before the rollback.
            foreach (var track in tracks)
            {
                string err = SongTrackChangeCore.ApplyTrackChange(
                    rom, track, voices, _dVol, _dPan, _dTempo, _changeVelocity);
                if (err != "") return err;
            }
            return "";
        }
    }
}
