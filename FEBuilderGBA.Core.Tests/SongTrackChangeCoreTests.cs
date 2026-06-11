using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for the single-track Track Change writer seam (#1002 Slice 1).
    /// Builds SYNTHETIC track streams (and a synthetic song header where needed)
    /// so the tests run hermetic with no real ROM, then exercises VOICE remap,
    /// VOL/PAN/TEMPO deltas + clamps, isAbbreviation write addressing,
    /// note-velocity nudges, no-op identity, corrupt-parse hardening, and the
    /// ambient-undo byte-identical rollback.
    /// </summary>
    [Collection("SharedState")]
    public class SongTrackChangeCoreTests
    {
        const uint SongAddr = 0x300;
        const uint Track0Data = 0x400;

        static void WriteGbaPtr(byte[] data, uint at, uint romOffset)
        {
            uint ptr = romOffset + 0x08000000;
            data[at + 0] = (byte)(ptr & 0xFF);
            data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        static ROM NewRom(byte[] data)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            // The ambient-undo snapshot (Undo.UndoPostion(addr, size)) reads
            // CoreState.ROM to capture before-bytes, so point it at this ROM for
            // the rollback test. Harmless for the no-scope tests.
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>Build a 1-track song so we can parse via the data offset.</summary>
        static ROM BuildSongWithTrack(byte[] trackBytes, out uint trackDataOffset)
        {
            byte[] data = new byte[0x1000];
            data[SongAddr] = 1; // trackCount = 1
            WriteGbaPtr(data, SongAddr + 4, 0x800);          // instrument ptr (unused)
            WriteGbaPtr(data, SongAddr + 8, Track0Data);     // track0 -> 0x400
            for (int i = 0; i < trackBytes.Length; i++)
                data[Track0Data + i] = trackBytes[i];
            trackDataOffset = Track0Data;
            return NewRom(data);
        }

        static List<SongVoiceChangeCore.VoiceChange> Voices(params (int from, int to)[] rows)
            => rows.Select(r => new SongVoiceChangeCore.VoiceChange { From = r.from, To = r.to }).ToList();


        // ------------------------------------------------------------------
        // VOICE remap
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_VoiceRemap_RewritesMatchingVoiceBytes()
        {
            // Track: BD 05, BD 09, BD 05, B1
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xBD, 9, 0xBD, 5, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            string err = SongTrackChangeCore.ApplyTrackChange(rom, track, Voices((5, 42)), 0, 0, 0, false);
            Assert.Equal("", err);

            Assert.Equal(42, rom.Data[off + 1]); // first BD value
            Assert.Equal(9, rom.Data[off + 3]);  // BD 09 untouched
            Assert.Equal(42, rom.Data[off + 5]); // third BD value
        }

        [Fact]
        public void ApplyTrackChange_VoiceIdentity_NoMutation()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongTrackChangeCore.ApplyTrackChange(rom, track, Voices((5, 5)), 0, 0, 0, false);
            Assert.Equal("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyTrackChange_VoiceOutOfRange_NoMutation()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongTrackChangeCore.ApplyTrackChange(rom, track, Voices((5, 200)), 0, 0, 0, false);
            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data); // validate-all-before-mutate
        }

        // ------------------------------------------------------------------
        // VOL / PAN +delta and clamps
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_VolDelta_AddsAndWritesAtAddrPlus1()
        {
            // Track: BE 64 (VOL=100), B1
            var rom = BuildSongWithTrack(new byte[] { 0xBE, 100, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +10, 0, 0, false));
            Assert.Equal(110, rom.Data[off + 1]); // normal 2-byte command -> value at addr+1
        }

        [Fact]
        public void ApplyTrackChange_VolClampUpperAt127()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBE, 120, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +50, 0, 0, false));
            Assert.Equal(127, rom.Data[off + 1]); // 120 + 50 = 170 -> clamp 127
        }

        [Fact]
        public void ApplyTrackChange_VolClampLowerAt0()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBE, 10, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, -50, 0, 0, false));
            Assert.Equal(0, rom.Data[off + 1]); // 10 - 50 = -40 -> clamp 0
        }

        [Fact]
        public void ApplyTrackChange_PanDelta_AddsAndClampsAt127()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBF, 100, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, +50, 0, false));
            Assert.Equal(127, rom.Data[off + 1]); // 100 + 50 = 150 -> clamp 127
        }

        // ------------------------------------------------------------------
        // TEMPO clamp at 255 (NOT 127) — the key Finding-4 distinction
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_TempoClampAt255_NotAt127()
        {
            // Track: BB C8 (TEMPO=200), B1
            var rom = BuildSongWithTrack(new byte[] { 0xBB, 200, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            // 200 + 100 = 300 -> clamp 255 (a 0..127 clamp would WRONGLY give 127).
            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, 0, +100, false));
            Assert.Equal(255, rom.Data[off + 1]);
        }

        [Fact]
        public void ApplyTrackChange_TempoAbove127_WritesActualValue()
        {
            // TEMPO=100, +50 = 150 -> stays 150 (full byte; a 127-clamp would cap it).
            var rom = BuildSongWithTrack(new byte[] { 0xBB, 100, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, 0, +50, false));
            Assert.Equal(150, rom.Data[off + 1]);
        }

        [Fact]
        public void ApplyTrackChange_TempoClampLowerAt0()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBB, 20, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, 0, -50, false));
            Assert.Equal(0, rom.Data[off + 1]);
        }

        // ------------------------------------------------------------------
        // isAbbreviation write addressing (c.addr) vs normal (c.addr+1)
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_AbbreviatedControl_WritesAtCodeAddr()
        {
            // The parser emits an ABBREVIATED code (running status) when a byte
            // <=127 is followed by a byte >127 AND a 2-byte command set lastCommand.
            //   BE 64  -> normal VOL=64 (lastCommand=0xBE)
            //   05     -> key candidate 5; next byte 0xB1(177) > 127 -> running
            //             status: abbreviated VOL, value=5, isAbbreviation=true,
            //             at addr off+2, consuming ONLY 1 byte
            //   B1     -> FINE (the byte that made 05 run as abbreviated), break
            byte[] bytes = { 0xBE, 64, 0x05, 0xB1 };
            var rom = BuildSongWithTrack(bytes, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            // Confirm the parser produced an abbreviated VOL code at addr = off+2.
            var abbr = track.codes.FirstOrDefault(c => c.isAbbreviation);
            Assert.NotNull(abbr);
            Assert.Equal(0xBEu, abbr.type);
            Assert.Equal(off + 2, abbr.addr);
            Assert.Equal(5u, abbr.value);

            // VOL +10: the normal VOL(64)->74 at off+1 (addr+1 write); the
            // abbreviated VOL(5)->15 written AT c.addr (off+2), NOT off+3. The FINE
            // byte at off+3 must stay 0xB1 (a wrong addr+1 write would clobber it).
            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +10, 0, 0, false));
            Assert.Equal(74, rom.Data[off + 1]);   // normal VOL 64+10=74 at addr+1
            Assert.Equal(15, rom.Data[off + 2]);   // abbreviated VOL 5+10=15 written AT c.addr
            Assert.Equal(0xB1, rom.Data[off + 3]); // FINE untouched
        }

        // ------------------------------------------------------------------
        // Note velocity (full Nxx addr+2, running-note addr+1), gated on
        // changeVelocity && dVol != 0
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_FullNoteVelocity_NudgedAtAddrPlus2()
        {
            // Full note: D0 (note) key=60 vel=80, then B1.
            // 0xD0 in [TIE..NOTE_END]; key<=127 with vel<=127 and no GTP -> 3-byte note.
            byte[] bytes = { 0xD0, 60, 80, 0xB1 };
            var rom = BuildSongWithTrack(bytes, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +10, 0, 0, changeVelocity: true));
            Assert.Equal(60, rom.Data[off + 1]); // key untouched
            Assert.Equal(90, rom.Data[off + 2]); // velocity 80 + 10 at addr+2
        }

        [Fact]
        public void ApplyTrackChange_RunningNoteVelocity_NudgedAtAddrPlus1()
        {
            // Establish a running NOTE command then an abbreviated note:
            //   D0 3C 50  -> full note key=60 vel=80 (lastCommand stays 0 for notes)
            // Running-note path requires a leading byte <=127 forming "key velocity".
            // Build: 3C 50 -> key=60 vel=80 (type=60 <=127 path), velocity 0x50<=127.
            // That code has type=60 (<=127), value=velocity(80). changeVelocity
            // nudges code.value at addr+1.
            byte[] bytes = { 60, 80, 0xB1 };
            var rom = BuildSongWithTrack(bytes, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);

            var note = track.codes.First(c => c.type == 60);
            Assert.Equal(80u, note.value);

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +5, 0, 0, changeVelocity: true));
            Assert.Equal(85, rom.Data[off + 1]); // velocity 80 + 5 at addr+1
        }

        [Fact]
        public void ApplyTrackChange_Velocity_NotChangedWhenFlagOff()
        {
            byte[] bytes = { 0xD0, 60, 80, 0xB1 };
            var rom = BuildSongWithTrack(bytes, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            // dVol != 0 but changeVelocity false -> velocity untouched (no VOL code
            // present, so nothing changes at all).
            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +10, 0, 0, changeVelocity: false));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyTrackChange_Velocity_NotChangedWhenDVolZero()
        {
            byte[] bytes = { 0xD0, 60, 80, 0xB1 };
            var rom = BuildSongWithTrack(bytes, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            // changeVelocity true but dVol == 0 -> WF gates velocity on changeVol
            // (dVol != 0), so velocity is untouched.
            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, 0, 0, changeVelocity: true));
            Assert.Equal(before, rom.Data);
        }

        // ------------------------------------------------------------------
        // No-op: identity voices + zero deltas
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_NoChanges_NoMutation()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xBE, 64, 0xBF, 64, 0xBB, 100, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, Voices((5, 5)), 0, 0, 0, false));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyTrackChange_NullVoicesZeroDeltas_NoMutation()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xBE, 64, 0xB1 }, out uint off);
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, 0, 0, 0, false));
            Assert.Equal(before, rom.Data);
        }

        // ------------------------------------------------------------------
        // Guard cases
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_NullRom_ReturnsErrorNoThrow()
        {
            var track = new SongMidiCore.Track();
            string err = SongTrackChangeCore.ApplyTrackChange(null, track, Voices((5, 9)), 0, 0, 0, false);
            Assert.NotEqual("", err);
        }

        [Fact]
        public void ApplyTrackChange_NullTrack_ReturnsErrorNoThrow()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xB1 }, out _);
            string err = SongTrackChangeCore.ApplyTrackChange(rom, null, Voices((5, 9)), 0, 0, 0, false);
            Assert.NotEqual("", err);
        }

        // ------------------------------------------------------------------
        // ParseSingleTrackFromDataOffset offset semantics + hardening
        // ------------------------------------------------------------------

        [Fact]
        public void ParseSingleTrackFromDataOffset_ParsesFromDataNotPointerSlot()
        {
            // Place a track stream directly at the data offset (NOT a pointer slot).
            byte[] data = new byte[0x500];
            byte[] stream = { 0xBD, 7, 0xBE, 90, 0xB1 };
            for (int i = 0; i < stream.Length; i++) data[Track0Data + i] = stream[i];
            var rom = NewRom(data);

            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, Track0Data);
            // First code is the VOICE (0xBD value 7) read straight from the data
            // offset — a pointer-slot parser would instead u32() those first bytes.
            Assert.Equal(0xBDu, track.codes[0].type);
            Assert.Equal(7u, track.codes[0].value);
            Assert.Equal(Track0Data, track.codes[0].addr);
        }

        [Fact]
        public void ParseSingleTrackFromDataOffset_AcceptsGbaPointer()
        {
            byte[] data = new byte[0x500];
            byte[] stream = { 0xBD, 7, 0xB1 };
            for (int i = 0; i < stream.Length; i++) data[Track0Data + i] = stream[i];
            var rom = NewRom(data);

            // Pass the GBA pointer form (0x08000000 | offset) — normalized via U.toOffset.
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, Track0Data + 0x08000000);
            Assert.Equal(0xBDu, track.codes[0].type);
            Assert.Equal(Track0Data, track.codes[0].addr);
        }

        [Fact]
        public void ParseSingleTrackFromDataOffset_NullRom_ReturnsEmptyNoThrow()
        {
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(null, Track0Data);
            Assert.Empty(track.codes);
        }

        [Fact]
        public void ParseSingleTrackFromDataOffset_UnsafeOffset_ReturnsEmpty()
        {
            var rom = NewRom(new byte[0x500]);
            // 0x10 is below the 0x200 safety floor.
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, 0x10);
            Assert.Empty(track.codes);
        }

        [Fact]
        public void ParseSingleTrackFromDataOffset_CorruptLoopAtEnd_NoThrowNoOob()
        {
            // 0xB2 (loop) at the very last byte: p32(addr+1) would read past EOF.
            // The hardened loop guards addr+4 >= limitter and breaks -> empty track,
            // no IndexOutOfRange.
            byte[] data = new byte[0x420];
            data[0x41F] = 0xB2; // last byte
            var rom = NewRom(data);

            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, 0x41F);
            Assert.Empty(track.codes); // loop code dropped, no crash
        }

        [Fact]
        public void ParseSingleTrackFromDataOffset_NoteWithoutGtpAtEnd_NoOob()
        {
            // Full note "D0 key vel" as the LAST 3 bytes — the GTP read at addr+3
            // would run past EOF. The hardened loop treats a missing GTP as "no GTP".
            byte[] data = new byte[0x500];
            uint at = (uint)(data.Length - 3);
            data[at + 0] = 0xD0; // note
            data[at + 1] = 60;   // key
            data[at + 2] = 80;   // velocity (no GTP byte follows)
            var rom = NewRom(data);

            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, at);
            Assert.Single(track.codes);
            Assert.Equal(60u, track.codes[0].value);
            Assert.Equal(80u, track.codes[0].value2); // velocity captured, no GTP read OOB
        }

        // ------------------------------------------------------------------
        // Ambient-undo rollback restores bytes byte-identical (no full-ROM clone)
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_AmbientUndo_RollbackRestoresBytes()
        {
            var rom = BuildSongWithTrack(new byte[] { 0xBD, 5, 0xBE, 64, 0xBF, 64, 0xBB, 100, 0xB1 }, out uint off);
            // NewRom set CoreState.ROM = rom; wire a fresh Undo for the rollback flow.
            CoreState.Undo = new Undo();
            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, off);
            byte[] before = (byte[])rom.Data.Clone();

            // Apply voice + vol + pan + tempo under the caller's single ambient scope
            // (mirrors UndoService.Begin/Commit -> ROM.BeginUndoScope + Undo.Push).
            // ApplyTrackChange no longer clones the whole ROM (#1106) — atomicity
            // comes from the caller's scope here, and from a write-set-only restore
            // on a mid-write fault.
            var ud = CoreState.Undo.NewUndoData("test");
            using (ROM.BeginUndoScope(ud))
            {
                string err = SongTrackChangeCore.ApplyTrackChange(rom, track, Voices((5, 42)), +10, +10, +10, false);
                Assert.Equal("", err);
            }
            Assert.NotEqual(before, rom.Data); // mutated

            // The ambient scope recorded EXACTLY one undo position per written byte
            // (voice 1 + vol 1 + pan 1 + tempo 1 = 4) — proof the apply does NOT
            // snapshot the whole ROM into the scope.
            Assert.Equal(4, ud.list.Count);
            foreach (var pos in ud.list)
                Assert.Equal(1, pos.data.Length); // each undo record is a single byte, not a ROM-sized blob

            // Commit then undo -> byte-identical restore.
            CoreState.Undo.Push(ud);
            CoreState.Undo.RunUndo();
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyTrackChange_LargeRom_NoFullRomSnapshotCost()
        {
            // A 16 MB ROM with a tiny write-set: the apply must stay correct and
            // record only the 1 written byte's undo position (NOT a 16 MB blob) —
            // the regression guard for the #1106 full-ROM-clone removal.
            byte[] data = new byte[0x1000000]; // 16 MB
            data[SongAddr + 0] = 1;
            WriteGbaPtr(data, SongAddr + 4, 0x800);
            WriteGbaPtr(data, SongAddr + 8, Track0Data);
            data[Track0Data + 0] = 0xBE; data[Track0Data + 1] = 50; data[Track0Data + 2] = 0xB1;
            var rom = NewRom(data);
            CoreState.Undo = new Undo();

            var track = SongMidiCore.ParseSingleTrackFromDataOffset(rom, Track0Data);
            var ud = CoreState.Undo.NewUndoData("test");
            using (ROM.BeginUndoScope(ud))
            {
                Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, track, null, +10, 0, 0, false));
            }
            Assert.Equal(60, rom.Data[Track0Data + 1]); // 50 + 10
            Assert.Single(ud.list);                     // exactly 1 undo record
            Assert.Equal(1, ud.list[0].data.Length);    // a single byte, not 16 MB
        }

        [Fact]
        public void ApplyTrackChange_BulkPath_FaultRollsBackEveryTouchedTrack()
        {
            // Bulk caller semantics: apply across ALL tracks under ONE scope. If a
            // later track's apply fails (here: track1 has an out-of-range target
            // voice rejected by validate-all-before-mutate), the bulk caller stops
            // and the caller's UndoService.Rollback reverts the EARLIER tracks'
            // already-written bytes — byte-identical restore for the whole action.
            byte[] data = new byte[0x1000];
            data[SongAddr] = 2;
            WriteGbaPtr(data, SongAddr + 4, 0x800);
            WriteGbaPtr(data, SongAddr + 8, 0x400);
            WriteGbaPtr(data, SongAddr + 12, 0x440);
            // Track0 @0x400: BD 05, B1 (voice 5 -> remappable)
            data[0x400] = 0xBD; data[0x401] = 5; data[0x402] = 0xB1;
            // Track1 @0x440: BD 05, B1 (same voice; the bad target hits here too)
            data[0x440] = 0xBD; data[0x441] = 5; data[0x442] = 0xB1;
            var rom = NewRom(data);
            CoreState.Undo = new Undo();
            byte[] before = (byte[])rom.Data.Clone();

            var tracks = SongMidiCore.ParseTracks(rom, SongAddr, 2);
            // First track gets a VALID remap (5 -> 9); the "bulk fault" is simulated
            // by then applying an INVALID remap (5 -> 200) which CollectWrites
            // rejects with zero mutation. We run both under ONE scope and roll back.
            var ud = CoreState.Undo.NewUndoData("bulk");
            string firstErr, secondErr;
            using (ROM.BeginUndoScope(ud))
            {
                firstErr = SongTrackChangeCore.ApplyTrackChange(rom, tracks[0], Voices((5, 9)), 0, 0, 0, false);
                Assert.Equal("", firstErr);
                Assert.Equal(9, rom.Data[0x401]); // track0 already mutated

                secondErr = SongTrackChangeCore.ApplyTrackChange(rom, tracks[1], Voices((5, 200)), 0, 0, 0, false);
                Assert.NotEqual("", secondErr); // track1 rejected, ZERO mutation
                Assert.Equal(5, rom.Data[0x441]); // track1 untouched
            }

            // The caller rolls back the whole action -> track0's write is reverted.
            CoreState.Undo.Push(ud);
            CoreState.Undo.RunUndo();
            Assert.Equal(before, rom.Data); // byte-identical across BOTH tracks
        }

        // ------------------------------------------------------------------
        // Per-track application across multiple parsed tracks (bulk path)
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyTrackChange_PerTrackInBulkLoop_AllTracksMutated()
        {
            // 2-track song: each track has a VOL command. A bulk caller loops
            // ApplyTrackChange over every parsed track.
            byte[] data = new byte[0x1000];
            data[SongAddr] = 2;
            WriteGbaPtr(data, SongAddr + 4, 0x800);
            WriteGbaPtr(data, SongAddr + 8, 0x400);
            WriteGbaPtr(data, SongAddr + 12, 0x440);
            // Track0 @0x400: BE 50, B1
            data[0x400] = 0xBE; data[0x401] = 50; data[0x402] = 0xB1;
            // Track1 @0x440: BE 60, B1
            data[0x440] = 0xBE; data[0x441] = 60; data[0x442] = 0xB1;
            var rom = NewRom(data);

            var tracks = SongMidiCore.ParseTracks(rom, SongAddr, 2);
            foreach (var t in tracks)
                Assert.Equal("", SongTrackChangeCore.ApplyTrackChange(rom, t, null, +10, 0, 0, false));

            Assert.Equal(60, rom.Data[0x401]); // 50 + 10
            Assert.Equal(70, rom.Data[0x441]); // 60 + 10
        }
    }
}
