// SPDX-License-Identifier: GPL-3.0-or-later
// Single-track "Track Change" editor VM (#1002 Slice 1). Mirrors WinForms
// SongTrackChangeTrackForm.Init(..., this.Tracks[no]) -> SongUtil.ChangeTrackAndWrite:
// parse ONE track (whose data begins at the resolved DataOffset the caller
// passes), surface the distinct 0xBD voices used by that track as editable
// remap rows, plus Vol / Pan / Velocity nudge inputs, then apply every change
// in ONE undo step via SongTrackChangeCore.ApplyTrackChange.
//
// Vol/Pan are the per-track VOL(0xBE)/PAN(0xBF) deltas. Velocity (gated on
// dVol != 0) nudges note velocities by the same dVol. Tempo is a song-wide
// concept surfaced on the BULK editor, not here (matches the WF single-track
// dialog, which exposes Voice/Vol/Pan/Velocity).
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTrackChangeTrackViewModel : ViewModelBase
    {
        uint _trackDataOffset;
        bool _isLoaded;
        SongMidiCore.Track _track;

        int _dVol;
        int _dPan;
        bool _changeVelocity;

        /// <summary>Resolved track-DATA ROM offset (WF <c>SongUtil.Track.basepointer</c> /
        /// Avalonia <c>TrackInfo.DataOffset</c>). The caller passes this via Navigate.</summary>
        public uint TrackDataOffset { get => _trackDataOffset; set => SetField(ref _trackDataOffset, value); }
        // Backwards-compat alias for older list/jump wiring that read CurrentAddr.
        public uint CurrentAddr { get => _trackDataOffset; set => SetField(ref _trackDataOffset, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Volume delta applied to every 0xBE VOL (and, when
        /// <see cref="ChangeVelocity"/> is set, to note velocities). Clamped 0..127 on write.</summary>
        public int DVol { get => _dVol; set => SetField(ref _dVol, value); }
        /// <summary>Pan delta applied to every 0xBF PAN. Clamped 0..127 on write.</summary>
        public int DPan { get => _dPan; set => SetField(ref _dPan, value); }
        /// <summary>When true (and <see cref="DVol"/> != 0), also nudge note velocities by DVol.</summary>
        public bool ChangeVelocity { get => _changeVelocity; set => SetField(ref _changeVelocity, value); }

        /// <summary>Editable voice-remap rows (one per distinct 0xBD voice in this track).</summary>
        public ObservableCollection<VoiceRow> Rows { get; } = new();

        /// <summary>True when at least one row has an edited target voice
        /// (<c>To != From</c>) OR any Vol/Pan delta is set OR a velocity nudge is
        /// requested. The view checks this before opening an undo scope so a no-op
        /// Apply neither mutates nor reports "applied".</summary>
        public bool HasPendingChanges
        {
            get
            {
                foreach (var r in Rows) if (r.To != r.From) return true;
                if (_dVol != 0) return true;
                if (_dPan != 0) return true;
                if (_changeVelocity && _dVol != 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Build the left-hand list — one AddrResult per distinct voice so the list
        /// reflects real track data, not a placeholder. The standalone "open" path
        /// (no track context) yields an empty list.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            for (int i = 0; i < Rows.Count; i++)
                result.Add(new AddrResult((uint)i, Rows[i].Display, (uint)i));
            return result;
        }

        /// <summary>
        /// Load the single track whose DATA begins at <paramref name="trackDataOffset"/>
        /// (the resolved ROM offset the caller passes): parse it via
        /// <see cref="SongMidiCore.ParseSingleTrackFromDataOffset"/> and derive the
        /// distinct 0xBD voices it uses.
        /// </summary>
        public void LoadEntry(uint trackDataOffset)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            TrackDataOffset = trackDataOffset;
            Rows.Clear();

            // Reset the nudge inputs on (re)load so a fresh open starts from no-op.
            _dVol = 0; _dPan = 0; _changeVelocity = false;
            OnPropertyChanged(nameof(DVol));
            OnPropertyChanged(nameof(DPan));
            OnPropertyChanged(nameof(ChangeVelocity));

            _track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, trackDataOffset);

            var seen = new HashSet<int>();
            if (_track?.codes != null)
            {
                foreach (var c in _track.codes)
                {
                    if (c == null || c.type != 0xBD) continue;
                    int v = (int)c.value;
                    if (v >= 0 && v < 128 && seen.Add(v))
                        Rows.Add(new VoiceRow { From = v, To = v });
                }
            }

            IsLoaded = true;
        }

        /// <summary>
        /// Apply the voice remaps + Vol/Pan/Velocity nudges to this track in ROM.
        /// Returns "" on success (including the no-op case) or a localized error
        /// string. The CALLER owns the ambient undo scope.
        /// </summary>
        public string ApplyChanges()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM is not loaded.");
            if (!IsLoaded || _track == null) return R._("Track is invalid.");

            // Re-parse from ROM so the apply runs against the CURRENT bytes (the
            // editor may be re-applied after a previous commit).
            _track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, TrackDataOffset);

            var voices = new List<SongVoiceChangeCore.VoiceChange>();
            foreach (var row in Rows)
            {
                if (row.To != row.From)
                    voices.Add(new SongVoiceChangeCore.VoiceChange { From = row.From, To = row.To });
            }

            return SongTrackChangeCore.ApplyTrackChange(
                rom, _track, voices, _dVol, _dPan, 0 /* no per-track tempo */, _changeVelocity);
        }
    }
}
