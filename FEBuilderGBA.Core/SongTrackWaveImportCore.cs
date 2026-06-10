// SPDX-License-Identifier: GPL-3.0-or-later
// Whole-song WAV import seam (#1001 PR1). Builds an ENTIRE one-track song that
// plays an imported RIFF WAV and repoints a song-table entry at it.
//
// Port of WinForms SongUtil.ImportWave (the .wav branch only), de-coupled from
// InputFormRef / RecycleAddress / Undo dialogs:
//   * WAV -> GBA DirectSound sample bytes via SongDirectSoundWavCore.WavToByte.
//   * a two-row voicegroup: row[type=0x00, key=0x3c, P4->sample, ADSR FF 00 FF A5]
//     + a terminator row (all-0xFF dwords, WF "保険のために無効値").
//   * a one-track playback stream: VOL 127, KEYSH 0, VOICE 0, TIE 60 127, the
//     W96 rest run (WF playsec/2 + odd-remainder + 11% pad), EOT, optional
//     GOTO addr (when useLoop), FINE.
//   * a 12-byte song header: track count 1, voicegroup ptr @+4, track ptr @+8.
//   * repoint the song-table entry slot at songTableSlotAddr to the new header.
//
// SCOPE (#1001 PR1): RIFF .wav ONLY. WF's SOX channel/rate/bit-depth/volume
// normalization, the .DPCM and .S branches, and the recycle/blackout pass are
// NOT ported here (.s / .instrument land in PR2). The hard requirements kept
// from WF are the EXACT byte layout + the validate-before-mutate transaction.
//
// TRANSACTION (the #885/#923 pattern, mirrors SongDirectSoundWavCore.ImportWave):
//   precompute every aligned blob offset + the final ROM length with overflow
//   checks BEFORE the first resize/write; snapshot rom.Data.Clone(); on ANY
//   failure restore the snapshot byte-identically (length-aware) and return
//   U.NOT_FOUND with NO net byte mutation. All writes go through the ambient-undo
//   ROM overloads (rom.write_range / rom.write_p32) so the caller's BeginUndoScope
//   captures them as a single undo record.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform whole-song WAV import (#1001 PR1). Pure static port of the
    /// WinForms <c>SongUtil.ImportWave</c> <c>.wav</c> branch: build an entire
    /// one-track song that plays the imported sample, append it to ROM free
    /// space, and repoint a song-table entry at the new song header. Every entry
    /// point is bounds-guarded and never throws.
    /// </summary>
    public static class SongTrackWaveImportCore
    {
        // mxlplay command bytes (verbatim from WF SongUtil constants).
        const byte VOL = 0xBE;   // volume
        const byte KEYSH = 0xBC; // key shift
        const byte VOICE = 0xBD; // 楽器変更 (voice/program change)
        const byte EOT = 0xCE;   // end of track marker
        const byte TIE = 0xCF;   // tied note
        const byte GOTO = 0xB2;  // goto (loop)
        const byte FINE = 0xB1;  // end of song
        const byte W96 = 48 + 0x80; // whole rest (W96)

        // The voicegroup default ADSR/playback bytes WF writes into the row
        // (voca[8..11] = FF 00 FF A5).
        const byte ADSR_ATTACK = 0xFF;
        const byte ADSR_DECAY = 0x00;
        const byte ADSR_SUSTAIN = 0xFF;
        const byte ADSR_RELEASE = 0xA5;

        const byte BASE_KEY = 0x3c;     // voca[1] = 0x3c (base note)
        const int VOICE_ROW_SIZE = 12;  // one voicegroup row
        const int SONG_HEADER_SIZE = 12;// 4 (count+pad) + 4 (voca ptr) + 4 (track ptr)

        /// <summary>
        /// Import a RIFF/WAVE byte array as an ENTIRE new one-track song that
        /// plays the imported sample, and repoint the song-table entry slot
        /// <paramref name="songTableSlotAddr"/> (passed as an OFFSET) at the new
        /// song header. ROM-MUTATING; records into the caller's ambient undo
        /// scope.
        /// <para>Returns <see cref="U.toPointer(uint)"/> of the new song-header
        /// offset on success, or <see cref="U.NOT_FOUND"/> on any failure — with
        /// the ROM restored byte-identical (defensive snapshot + length-aware
        /// restore). Never throws.</para>
        /// </summary>
        /// <param name="rom">The loaded ROM (its ambient undo scope captures the writes).</param>
        /// <param name="songTableSlotAddr">OFFSET of the 4-byte song-table entry
        /// pointer slot to repoint (same slot the MIDI import repoints).</param>
        /// <param name="wavBytes">The raw RIFF WAV file bytes.</param>
        /// <param name="useLoop">When true, append a <c>GOTO</c> back to the rest
        /// run so the song loops; when false (WF default), the track ends at
        /// <c>FINE</c> with no <c>GOTO</c>.</param>
        /// <param name="error"><c>null</c> on success, else a user-facing message.</param>
        public static uint ImportWaveAsSong(ROM rom, uint songTableSlotAddr, byte[] wavBytes, bool useLoop, out string error)
        {
            error = null;
            if (rom == null)
            {
                error = R._("ROM is not loaded.");
                return U.NOT_FOUND;
            }

            // 1. Convert the WAV to GBA DirectSound sample bytes (validate-first;
            //    WavToByte sets error and returns null on any malformed input).
            byte[] sample = SongDirectSoundWavCore.WavToByte(wavBytes, out error);
            if (sample == null)
            {
                // error already set by WavToByte
                return U.NOT_FOUND;
            }

            // Playback seconds (drives the W96 rest run). WF wavToDataSec.
            uint playsec = WavToDataSec(wavBytes);
            if (playsec == U.NOT_FOUND)
            {
                error = R._("The Wave file has no playable data.");
                return U.NOT_FOUND;
            }

            // The song-table slot must be in-bounds for the final repoint.
            if ((long)songTableSlotAddr + 4 > rom.Data.Length)
            {
                error = R._("The song-table slot address is out of range.");
                return U.NOT_FOUND;
            }

            // 2. Build the in-memory blobs (no cross-pointers resolved yet). The
            //    voicegroup + song header hold pointers we patch once the append
            //    offsets are known (two-phase).
            byte[] voca = BuildVoicegroupNoPointers();
            byte[] track = BuildTrack(playsec, useLoop, out int gotoPointerIndex);
            byte[] header = new byte[SONG_HEADER_SIZE];
            header[0] = 1; // 1 track

            // 3. Precompute every aligned append offset + the final ROM length,
            //    with overflow checks, BEFORE any resize/write. Layout order
            //    (each 4-byte aligned): sample, voicegroup, track, song header.
            long baseLen = U.Padding4((uint)rom.Data.Length);
            long sampleOff = baseLen;
            long vocaOff = Align4(sampleOff + sample.Length);
            long trackOff = Align4(vocaOff + voca.Length);
            long headerOff = Align4(trackOff + track.Length);
            long finalLen = headerOff + header.Length;

            // GBA cartridge cap (mirrors write_resize_data's 0x02000000 guard);
            // reject before mutating so a too-large import never half-writes.
            if (finalLen > 0x02000000)
            {
                error = R._("The imported song does not fit in the 32MB ROM space.");
                return U.NOT_FOUND;
            }

            // 4. Resolve cross-pointers into the prebuilt blobs (OFFSETS in;
            //    U.write_p32 converts each to a GBA pointer internally).
            //    voicegroup row P4 (+4) -> the appended sample offset.
            U.write_p32(voca, 4, (uint)sampleOff);
            //    song header: voicegroup ptr (+4), track ptr (+8).
            U.write_p32(header, 4, (uint)vocaOff);
            U.write_p32(header, 8, (uint)trackOff);
            //    loop GOTO -> the rest run start (track_addr + 6, WF). When
            //    useLoop is false there is no GOTO operand to patch.
            if (useLoop && gotoPointerIndex >= 0)
                U.write_p32(track, (uint)gotoPointerIndex, (uint)(trackOff + 6));

            // 5. Mutate under a defensive snapshot. ANY fault -> byte-identical
            //    restore + U.NOT_FOUND (zero net mutation).
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                if (!rom.write_resize_data((uint)finalLen))
                {
                    RestoreSnapshot(rom, snapshot);
                    error = R._("Failed to allocate ROM space for the imported song.");
                    return U.NOT_FOUND;
                }

                rom.write_range((uint)sampleOff, sample);
                rom.write_range((uint)vocaOff, voca);
                rom.write_range((uint)trackOff, track);
                rom.write_range((uint)headerOff, header);

                // Repoint the song-table entry slot to the new song header
                // (write_p32 takes an OFFSET and converts it internally).
                rom.write_p32(songTableSlotAddr, (uint)headerOff);

                return U.toPointer((uint)headerOff);
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snapshot);
                error = R._("Wave-as-song import failed: {0}", ex.Message);
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Build the two-row voicegroup with the row defaults filled but the P4
        /// sample pointer (row +4) left zero — the caller patches it once the
        /// sample append offset is known. Row 0 = one playable DirectSound voice
        /// (type 0x00, base key 0x3c, ADSR FF 00 FF A5); row 1 = the WF
        /// terminator (all-0xFF dwords). Mirrors WF <c>voca</c> exactly.
        /// </summary>
        internal static byte[] BuildVoicegroupNoPointers()
        {
            byte[] voca = new byte[VOICE_ROW_SIZE * 2];
            // Row 0: type=0x00 (voca[0] already 0), base key, ADSR.
            voca[1] = BASE_KEY;
            // voca[4..7] = sample pointer (left 0 here; patched by caller).
            voca[8] = ADSR_ATTACK;
            voca[9] = ADSR_DECAY;
            voca[10] = ADSR_SUSTAIN;
            voca[11] = ADSR_RELEASE;
            // Row 1: WF "保険のために無効値" terminator — all-0xFF dwords.
            U.write_u32(voca, 12, U.NOT_FOUND);
            U.write_u32(voca, 16, U.NOT_FOUND);
            U.write_u32(voca, 20, U.NOT_FOUND);
            return voca;
        }

        /// <summary>
        /// Build the one-track playback stream that plays voice 0 for the whole
        /// sample. Verbatim WF command order: VOL 127, KEYSH 0, VOICE 0, TIE 60
        /// 127, the W96 rest run (playsec/2 + odd-remainder + 11% pad), EOT,
        /// optional GOTO (operand left 0 — caller patches it), FINE.
        /// <para><paramref name="gotoPointerIndex"/> is the byte index of the
        /// GOTO 4-byte operand (for the caller to patch), or -1 when
        /// <paramref name="useLoop"/> is false (no GOTO emitted).</para>
        /// </summary>
        internal static byte[] BuildTrack(uint playsec, bool useLoop, out int gotoPointerIndex)
        {
            gotoPointerIndex = -1;
            List<byte> track = new List<byte>();
            U.append_u8(track, VOL);
            U.append_u8(track, 127);
            U.append_u8(track, KEYSH);
            U.append_u8(track, 0);
            U.append_u8(track, VOICE);
            U.append_u8(track, 0);   // voice 0
            U.append_u8(track, TIE);
            U.append_u8(track, 60);  // Cn3
            U.append_u8(track, 127); // v127

            // 全休符 — the W96 rest run.
            uint zenkyufu = playsec / 2;
            for (uint i = 0; i < zenkyufu; i++)
                U.append_u8(track, W96);
            if (playsec % 2 == 1)
                U.append_u8(track, W96);
            // 微妙にずれるらしいので補正 (11% pad).
            uint yohaku = (uint)(playsec * 0.11f);
            for (uint i = 0; i < yohaku; i++)
                U.append_u8(track, W96);
            U.append_u8(track, EOT);

            if (useLoop)
            {
                U.append_u8(track, GOTO);
                gotoPointerIndex = track.Count; // operand starts here
                U.append_u32(track, 0);         // patched by caller -> track+6
            }
            U.append_u8(track, FINE);
            return track.ToArray();
        }

        /// <summary>
        /// Port of WinForms <c>SongUtil.wavToDataSec</c>: the number of playback
        /// "seconds" (data_chunk_size / bytes_per_sec, rounded up, min 1) that
        /// drives the W96 rest run length. Bounds-guarded; <see cref="U.NOT_FOUND"/>
        /// on guard failure. Never throws.
        /// </summary>
        internal static uint WavToDataSec(byte[] data)
        {
            if (data == null || data.Length < (44 + 1))
                return U.NOT_FOUND;

            uint fmt_bytes_per_sec = U.u32(data, 28);
            if (fmt_bytes_per_sec == 0)
                return U.NOT_FOUND;

            uint data_chunk_size = U.u32(data, 40);
            if (data_chunk_size > data.Length - (44 + 1))
            {//チャンクのデータサイズが不正だったら修正する.
                data_chunk_size = (uint)(data.Length - (44 + 1));
            }

            uint ret = data_chunk_size / fmt_bytes_per_sec;
            if (data_chunk_size % fmt_bytes_per_sec != 0)
                ret += 1;

            return Math.Max(ret, 1);
        }

        /// <summary>4-byte align a long offset (overflow-safe; used during the
        /// pre-write offset precomputation).</summary>
        static long Align4(long v)
        {
            long mod = v % 4;
            return mod == 0 ? v : v + (4 - mod);
        }

        /// <summary>
        /// Length-aware byte-identical restore (the #885/#923 pattern): an append
        /// can GROW rom.Data, so down-resize back to the snapshot length BEFORE
        /// the in-place copy (a naive Array.Copy would leave the grown tail
        /// alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snapshot)
        {
            if (rom.Data.Length != snapshot.Length)
                rom.write_resize_data((uint)snapshot.Length);
            Array.Copy(snapshot, rom.Data, snapshot.Length);
        }
    }
}
