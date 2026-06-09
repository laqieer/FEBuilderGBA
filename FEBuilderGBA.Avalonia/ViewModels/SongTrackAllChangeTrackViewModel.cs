// SPDX-License-Identifier: GPL-3.0-or-later
// Bulk Track Change editor VM (#1015). Mirrors the voice-reassignment half of
// WinForms SongTrackAllChangeTrackForm: collect the distinct 0xBD voices used
// across ALL tracks of a song, let the user remap each to a new voice number,
// then apply every non-identity mapping in one undo step.
//
// Vol/Pan/Tempo nudges (the WF form's other affordances) are DEFERRED — this is
// PARTIAL WF parity (voice reassignment only); see #1015.
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

        /// <summary>Song HEADER address (the caller passes this via Navigate so
        /// the editor re-derives tracks + voices straight from the header).</summary>
        public uint SongAddr { get => _songAddr; set => SetField(ref _songAddr, value); }
        /// <summary>Instrument-set pointer (P4) read from the song header +4 —
        /// used to resolve voice -> instrument names.</summary>
        public uint InstrumentAddr { get => _instrumentAddr; set => SetField(ref _instrumentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Editable voice-remap rows (one per distinct 0xBD voice).</summary>
        public ObservableCollection<VoiceRow> Rows { get; } = new();

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
        /// Apply every non-identity voice remap to the song in ROM. Returns "" on
        /// success (including the no-op case where every row is unchanged) or a
        /// localized error string. The CALLER owns the ambient undo scope.
        /// </summary>
        public string ApplyChanges()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM is not loaded.");
            if (!IsLoaded || SongAddr == 0) return R._("Song address is invalid.");

            var map = new Dictionary<int, int>();
            foreach (var row in Rows)
            {
                if (row.To != row.From)
                    map[row.From] = row.To;
            }
            if (map.Count == 0) return "";

            return SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map);
        }
    }
}
