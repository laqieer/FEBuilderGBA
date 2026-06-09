// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform "Bulk Track Change" voice-reassignment seam (#1015).
//
// Ports the voice-reassignment half of WinForms SongTrackAllChangeTrackForm +
// SongUtil.GetVoices / ChangeCodeAddr2ByteCommand. The editor lets the user
// remap every distinct 0xBD voice (instrument/program) used across ALL tracks
// of a song to a new voice number in a single bulk operation.
//
//   * GetDistinctVoices — READ-ONLY: collect the DISTINCT 0xBD voices used by
//     any track (dedup by `from`, range 0..127), each seeded `to == from`.
//   * ApplyVoiceChanges  — ROM-MUTATING: rewrite the voice byte (at code.addr+1)
//     for every voice that has a non-identity from->to mapping.
//
// Atomicity: ApplyVoiceChanges runs under the CALLER's ambient undo scope
// (the View owns UndoService.Begin/Commit/Rollback -> ROM.BeginUndoScope), so
// it writes with the PLAIN no-undo `rom.write_u8(addr, val)` overload — the
// ambient scope records each byte for UNDO. Passing an explicit Undo.UndoData
// into the 3-arg overload while a scope is active would DOUBLE-RECORD the byte,
// so this seam takes NO undo parameter (mirrors WaitIconImportCore.Import).
// A defensive rom.Data snapshot guarantees a FAILED apply mutates ZERO bytes
// (length-aware byte-identical restore, #885/#923 pattern).
//
// Scope: VOICE reassignment only. The WF form also exposes Vol/Pan/Tempo
// nudges — those are deferred (partial WF parity); see #1015.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform voice-reassignment seam for the "Bulk Track Change"
    /// editor (#1015). PURE read (<see cref="GetDistinctVoices"/>) +
    /// ROM-mutating apply (<see cref="ApplyVoiceChanges"/>). Guards every read;
    /// never throws.
    /// </summary>
    public static class SongVoiceChangeCore
    {
        /// <summary>A single voice (0xBD program) remap: rewrite every code
        /// whose value == <see cref="From"/> to <see cref="To"/>.</summary>
        public struct VoiceChange
        {
            public int From;
            public int To;
        }

        /// <summary>
        /// Collect the DISTINCT 0xBD voices used across all tracks of the song
        /// at <paramref name="songAddr"/> (port of WF
        /// <c>SongUtil.GetVoices</c>). Each returned <see cref="VoiceChange"/> is
        /// seeded with <c>To == From</c> (a no-op until the user edits it).
        /// PURE / read-only; guards every read; never throws.
        /// </summary>
        public static List<VoiceChange> GetDistinctVoices(ROM rom, uint songAddr)
        {
            var list = new List<VoiceChange>();
            if (rom == null || rom.Data == null) return list;

            songAddr = U.toOffset(songAddr);
            if (!U.isSafetyOffset(songAddr, rom)) return list;

            // Defensive header guard: ParseTracks reads the +8 track-pointer
            // slots, so the 8-byte song header must fit fully inside rom.Data.
            if ((ulong)songAddr + 8 > (ulong)rom.Data.Length) return list;

            uint trackCount = rom.u8(songAddr); // u8: 0..255; clamp to the GBA 0..16 limit.
            if (trackCount > 16) trackCount = 16;
            if (trackCount == 0) return list;

            // Full track-pointer-table bound: ParseTracks reads trackCount 4-byte
            // slots at songAddr+8+ti*4, so the WHOLE table must fit (overflow-safe)
            // — guarding only songAddr+8 lets a later slot read past EOF (#1088).
            if ((ulong)songAddr + 8 + (ulong)trackCount * 4 > (ulong)rom.Data.Length) return list;

            // ParseTrackOne can still read past a truncated/corrupt TRACK stream
            // (e.g. 0xB2/0xB3 read p32(addr+1) without a per-code EOF check), so
            // treat ANY parse failure as "no voices" to honor never-throws (#1088).
            List<SongMidiCore.Track> tracks;
            try { tracks = SongMidiCore.ParseTracks(rom, songAddr, trackCount); }
            catch { return list; }

            var seen = new HashSet<int>();
            foreach (var t in tracks)
            {
                if (t?.codes == null) continue;
                foreach (var c in t.codes)
                {
                    if (c == null || c.type != 0xBD) continue;
                    int v = (int)c.value;
                    if (v >= 0 && v < 128 && seen.Add(v))
                        list.Add(new VoiceChange { From = v, To = v });
                }
            }
            return list;
        }

        /// <summary>
        /// Rewrite the 0xBD voice byte (at <c>code.addr + 1</c>) for every voice
        /// that has a non-identity <c>from -&gt; to</c> mapping. Identity rows
        /// (<c>from == to</c>) and voices not present in <paramref name="fromTo"/>
        /// are left untouched.
        /// <para>
        /// VALIDATE-ALL-BEFORE-MUTATE: every target write site is collected and
        /// range-validated first; only then are the bytes written. A defensive
        /// length-aware byte-identical snapshot restore (#885/#923) guarantees a
        /// FAILED apply mutates ZERO bytes.
        /// </para>
        /// <para>
        /// Single-pass semantics: mappings apply against the ORIGINALLY parsed
        /// codes — there is NO cascade within one apply (e.g. given <c>1-&gt;2</c>
        /// and <c>2-&gt;3</c>, a code that was originally a 2 becomes a 3, and a
        /// code that was originally a 1 becomes a 2; the freshly-written 2 is NOT
        /// re-mapped to 3).
        /// </para>
        /// <para>
        /// Undo: writes with the PLAIN no-undo <c>rom.write_u8(addr, val)</c>
        /// overload; the CALLER must own an ambient undo scope
        /// (<c>ROM.BeginUndoScope</c> via <c>UndoService.Begin/Commit/Rollback</c>)
        /// so each byte is recorded for UNDO. Do NOT also pass an active
        /// <c>Undo.UndoData</c> here — that would double-record.
        /// </para>
        /// </summary>
        /// <returns>"" on success (including the no-op case), or a localized
        /// error string on failure (with ZERO surviving mutation).</returns>
        public static string ApplyVoiceChanges(ROM rom, uint songAddr,
            IReadOnlyDictionary<int, int> fromTo)
        {
            if (rom == null || rom.Data == null) return R._("ROM is not loaded.");
            if (fromTo == null || fromTo.Count == 0) return "";

            songAddr = U.toOffset(songAddr);
            if (!U.isSafetyOffset(songAddr, rom)) return R._("Song address is invalid.");
            if ((ulong)songAddr + 8 > (ulong)rom.Data.Length) return R._("Song address is invalid.");

            uint trackCount = rom.u8(songAddr);
            if (trackCount > 16) trackCount = 16;
            // Full track-pointer-table bound (overflow-safe): ParseTracks reads
            // trackCount 4-byte slots at songAddr+8+ti*4 — a truncated table must
            // return an error with ZERO mutation, never throw (#1088).
            if ((ulong)songAddr + 8 + (ulong)trackCount * 4 > (ulong)rom.Data.Length)
                return R._("Song track table runs past ROM end.");
            // ParseTrackOne can throw on a truncated/corrupt TRACK stream (e.g.
            // 0xB2/0xB3 p32(addr+1)) — catch it and return an error BEFORE any
            // write so the seam never throws and never partially mutates (#1088).
            List<SongMidiCore.Track> tracks;
            try { tracks = SongMidiCore.ParseTracks(rom, songAddr, trackCount); }
            catch { return R._("Song track data is corrupt or truncated."); }

            // Collect target write sites + validate EVERYTHING before any write.
            var writes = new List<(uint addr, byte val)>();
            foreach (var t in tracks)
            {
                if (t?.codes == null) continue;
                foreach (var c in t.codes)
                {
                    if (c == null || c.type != 0xBD) continue;
                    if (!fromTo.TryGetValue((int)c.value, out int to)) continue;
                    if (to == (int)c.value) continue; // identity no-op
                    if (to < 0 || to > 127) return R._("Target voice {0} is out of range.", to);

                    uint vAddr = c.addr + 1;
                    if ((ulong)vAddr >= (ulong)rom.Data.Length)
                        return R._("Voice code runs past ROM end.");
                    writes.Add((vAddr, (byte)to));
                }
            }
            if (writes.Count == 0) return "";

            // Defensive snapshot: the caller's ambient scope captures the writes
            // for UNDO; this snapshot guarantees a FAILED apply mutates ZERO
            // bytes (length-aware byte-identical restore).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                foreach (var w in writes)
                    rom.write_u8(w.addr, w.val); // PLAIN overload -> ambient undo scope.
                return "";
            }
            catch (Exception ex)
            {
                if (rom.Data.Length != snap.Length)
                    rom.write_resize_data((uint)snap.Length);
                Array.Copy(snap, rom.Data, snap.Length);
                return R._("Apply voice changes failed: {0}", ex.Message);
            }
        }
    }
}
