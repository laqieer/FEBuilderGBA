// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform single-track "Track Change" writer seam (#1002 Slice 1).
//
// Ports the per-track half of WinForms SongUtil.ChangeTrackAndWrite +
// SongUtil.ChangeCodeAddr2ByteCommand. Operates on ONE already-parsed track
// (SongMidiCore.Track) and applies, in a single pass against the ORIGINALLY
// parsed codes:
//   * VOICE (0xBD) remap        — rewrite code value from -> to for each mapping
//   * VOL   (0xBE) +dVol        — clamp 0..127
//   * PAN   (0xBF) +dPan        — clamp 0..127
//   * TEMPO (0xBB) +dTempo      — clamp 0..255  (NOT 127 — TEMPO is a full byte)
//   * NOTE velocity +dVol       — only when changeVelocity && dVol != 0
//       - full Nxx/TIE velocity  written at code.addr+2 (code.value2)
//       - running-note velocity  written at code.addr+1 (code.value)
//
// isAbbreviation parity: ChangeCodeAddr2ByteCommand writes the 1-byte abbreviated
// form at code.addr, and the normal 2-byte command's value byte at code.addr+1.
// This seam preserves that (the WF semantics) for VOICE/VOL/PAN/TEMPO writes.
//
// Undo (Finding 1): writes with the PLAIN no-undo rom.write_u8(addr, val)
// overload so the CALLER's single ambient ROM.BeginUndoScope (via the Avalonia
// UndoService.Begin/Commit/Rollback) records each byte for UNDO. The seam takes
// NO explicit Undo arg and pushes NO UndoData of its own — passing an active
// Undo.UndoData while a scope is open would DOUBLE-RECORD the byte (mirrors
// SongVoiceChangeCore.ApplyVoiceChanges / WaitIconImportCore.Import).
//
// Atomicity: VALIDATE-ALL-BEFORE-MUTATE — every write site is collected and
// range-validated first; only then are the bytes written. The defensive fault
// snapshot captures ONLY the original bytes at the write sites (a small
// (addr, oldByte) list — NOT a full-ROM clone, #1106), so a FAILED apply
// mutates ZERO net bytes with O(write-set) cost. A bulk all-tracks caller
// (running every track under ONE ambient scope) still rolls back EVERY touched
// track as one action: each track's writes are recorded in the shared scope, so
// the caller's UndoService.Rollback reverts them all together.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform single-track Track Change writer (#1002 Slice 1). Ports
    /// WinForms <c>SongUtil.ChangeTrackAndWrite</c> + <c>ChangeCodeAddr2ByteCommand</c>.
    /// Validate-all-before-mutate, ambient-undo, isAbbreviation-aware. Guards
    /// every write; never throws.
    /// </summary>
    public static class SongTrackChangeCore
    {
        // GBA song command bytes (mirror SongMidiCore / SongUtil constants).
        const uint EOT = 0xCE;
        const uint TIE = 0xCF;
        const uint NOTE_END = 0xFF;
        const uint VOICE = 0xBD;
        const uint VOL = 0xBE;
        const uint PAN = 0xBF;
        const uint TEMPO = 0xBB;

        /// <summary>
        /// Apply VOICE remap + VOL/PAN/TEMPO deltas (and optional note-velocity
        /// delta) to a single already-parsed <paramref name="track"/>.
        /// </summary>
        /// <param name="rom">The loaded ROM (the track's codes hold ROM addresses).</param>
        /// <param name="track">A track parsed via
        /// <see cref="SongMidiCore.ParseSingleTrackFromDataOffset"/> (or one element of
        /// <see cref="SongMidiCore.ParseTracks"/>). Code addresses must point into this ROM.</param>
        /// <param name="voices">Voice (0xBD) remaps; identity rows (From == To) and
        /// voices not present in the track are ignored. May be null/empty.</param>
        /// <param name="dVol">Volume delta added to every 0xBE VOL (clamped 0..127).
        /// Also drives note-velocity when <paramref name="changeVelocity"/> is set.</param>
        /// <param name="dPan">Pan delta added to every 0xBF PAN (clamped 0..127).</param>
        /// <param name="dTempo">Tempo delta added to every 0xBB TEMPO (clamped 0..255).</param>
        /// <param name="changeVelocity">When true (and <paramref name="dVol"/> != 0),
        /// also nudge note velocities by <paramref name="dVol"/>.</param>
        /// <returns>"" on success (including the no-op case), or a localized error
        /// string on failure (with ZERO surviving mutation).</returns>
        public static string ApplyTrackChange(ROM rom, SongMidiCore.Track track,
            IList<SongVoiceChangeCore.VoiceChange> voices,
            int dVol, int dPan, int dTempo, bool changeVelocity)
        {
            if (rom == null || rom.Data == null) return R._("ROM is not loaded.");
            if (track?.codes == null) return R._("Track is invalid.");

            // VALIDATE-ALL-BEFORE-MUTATE: collect every (addr, value) write site
            // and range-validate it BEFORE touching the ROM. Mirrors WF
            // ChangeTrackAndWrite's per-code branch order (VOICE, VOL/velocity,
            // PAN, TEMPO) but never writes during this pass.
            var writes = new List<(uint addr, byte val)>();
            string err = CollectWrites(rom, track, voices, dVol, dPan, dTempo, changeVelocity, writes);
            if (err != "") return err;
            if (writes.Count == 0) return ""; // nothing to do (true no-op)

            // WRITE-SET-ONLY fault snapshot (#1106): capture ONLY the original
            // bytes at the addresses we are about to write — NOT the whole ROM.
            // Every write is a single byte (write_u8) with NO resize, so a tiny
            // (addr, oldByte) list is a complete defensive snapshot. The caller's
            // ambient ROM.BeginUndoScope still records each write for UNDO; this
            // list only guarantees a mid-write throw mutates ZERO net bytes. On
            // fault we restore via DIRECT rom.Data[addr] = oldByte so we do NOT
            // add extra ambient-undo records (which would survive as orphan undo
            // positions). The bulk all-tracks path keeps full atomicity because
            // each track's partial writes are recorded in the ONE shared scope,
            // so the caller's UndoService.Rollback reverts every track together.
            var restore = new (uint addr, byte oldByte)[writes.Count];
            int written = 0;
            try
            {
                for (int i = 0; i < writes.Count; i++)
                {
                    restore[i] = (writes[i].addr, rom.Data[writes[i].addr]);
                    rom.write_u8(writes[i].addr, writes[i].val); // PLAIN overload -> caller's ambient undo scope.
                    written++;
                }
                return "";
            }
            catch (Exception ex)
            {
                // Roll back ONLY the bytes we actually wrote, in reverse order,
                // writing directly to rom.Data so no new undo records are added.
                for (int i = written - 1; i >= 0; i--)
                {
                    uint addr = restore[i].addr;
                    if (addr < rom.Data.Length)
                        rom.Data[addr] = restore[i].oldByte;
                }
                return R._("Apply track change failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Build (and validate) the full write list for one track without mutating
        /// the ROM. Returns "" when every collected write is in range, or a
        /// localized error (with the partial list discarded by the caller).
        /// </summary>
        static string CollectWrites(ROM rom, SongMidiCore.Track track,
            IList<SongVoiceChangeCore.VoiceChange> voices,
            int dVol, int dPan, int dTempo, bool changeVelocity,
            List<(uint addr, byte val)> writes)
        {
            long romLen = rom.Data.Length;
            bool changeVol = dVol != 0;

            foreach (var c in track.codes)
            {
                if (c == null) continue;

                // ---- VOICE (0xBD): remap value from -> to ----
                if (c.type == VOICE)
                {
                    if (voices != null)
                    {
                        for (int n = 0; n < voices.Count; n++)
                        {
                            if ((int)c.value != voices[n].From) continue;
                            int to = voices[n].To;
                            if (to == (int)c.value) continue; // identity no-op
                            if (to < 0 || to > 127)
                                return R._("Target voice {0} is out of range.", to);
                            if (!TryAddrFor2ByteCommand(c, romLen, out uint vAddr))
                                return R._("Voice code runs past ROM end.");
                            writes.Add((vAddr, (byte)to));
                        }
                    }
                    continue; // WF: VOICE codes never fall through to VOL/PAN/TEMPO
                }

                // ---- VOL (0xBE) / note velocity ----
                if (changeVol)
                {
                    if (c.type == VOL)
                    {
                        int a = Clamp((int)c.value + dVol, 0, 127);
                        if (!TryAddrFor2ByteCommand(c, romLen, out uint vAddr))
                            return R._("Volume code runs past ROM end.");
                        writes.Add((vAddr, (byte)a));
                        continue;
                    }
                    else if (changeVelocity)
                    {
                        // Full Nxx/TIE note: nudge velocity (code.value2) at addr+2.
                        if (c.type >= TIE && c.type <= NOTE_END)
                        {
                            if (c.value != U.NOT_FOUND && c.value2 != U.NOT_FOUND)
                            {
                                int a = Clamp((int)c.value2 + dVol, 0, 127);
                                uint vAddr = c.addr + 2;
                                if ((ulong)vAddr >= (ulong)romLen)
                                    return R._("Note velocity runs past ROM end.");
                                writes.Add((vAddr, (byte)a));
                            }
                        }
                        // Running-note (type <= 127): nudge velocity (code.value) at addr+1.
                        else if (c.type <= 127)
                        {
                            if (c.value != U.NOT_FOUND)
                            {
                                int a = Clamp((int)c.value + dVol, 0, 127);
                                uint vAddr = c.addr + 1;
                                if ((ulong)vAddr >= (ulong)romLen)
                                    return R._("Note velocity runs past ROM end.");
                                writes.Add((vAddr, (byte)a));
                            }
                        }
                    }
                }

                // ---- PAN (0xBF): +dPan, clamp 0..127 ----
                if (dPan != 0 && c.type == PAN)
                {
                    int a = Clamp((int)c.value + dPan, 0, 127);
                    if (!TryAddrFor2ByteCommand(c, romLen, out uint vAddr))
                        return R._("Pan code runs past ROM end.");
                    writes.Add((vAddr, (byte)a));
                    continue;
                }

                // ---- TEMPO (0xBB): +dTempo, clamp 0..255 (full byte, NOT 127) ----
                if (dTempo != 0 && c.type == TEMPO)
                {
                    int a = Clamp((int)c.value + dTempo, 0, 255);
                    if (!TryAddrFor2ByteCommand(c, romLen, out uint vAddr))
                        return R._("Tempo code runs past ROM end.");
                    writes.Add((vAddr, (byte)a));
                    continue;
                }
            }
            return "";
        }

        /// <summary>
        /// Resolve the write address for a 2-byte command (VOICE/VOL/PAN/TEMPO),
        /// preserving WF <c>ChangeCodeAddr2ByteCommand</c> semantics: an abbreviated
        /// (running-status) code writes the 1 byte at <c>code.addr</c>; a normal
        /// code writes its value byte at <c>code.addr + 1</c>. Returns false when
        /// the resolved address runs past ROM end.
        /// </summary>
        static bool TryAddrFor2ByteCommand(SongMidiCore.Code c, long romLen, out uint addr)
        {
            addr = c.isAbbreviation ? c.addr : c.addr + 1;
            return (ulong)addr < (ulong)romLen;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
