// SPDX-License-Identifier: GPL-3.0-or-later
// SongTrack MIDI import write-back tests (#972).
//
// Proves the Avalonia Song Track editor's MIDI import button performs the real
// write-back via SongMidiCore.ImportMidiFile (previously UI-stubbed), targeting
// the correct song-table ENTRY pointer slot (NOT the dereferenced header), and
// that the whole import is exactly one undo record restoring the ROM
// byte-identical on rollback:
//   - GetSelectedSongTableEntryAddr() returns the table-entry slot
//     (tableBase + songId*8), honouring a custom ReadStartAddress — this catches
//     the original header-vs-slot targeting bug (Copilot plan review #2/#3);
//   - real FE8U round-trip: ExportMidi -> ImportMidi under UndoService.Begin/
//     Commit succeeds, the table entry pointer CHANGES, and Undo restores BOTH
//     the entry pointer AND rom.Data byte-for-byte (Copilot plan review: assert
//     the slot changes/restores, not just header validity);
//   - error paths: no song loaded / missing file return an error string with no
//     throw, and mutate ZERO bytes;
//   - the dedicated SongTrackImportMidiView VM enumerates real songs, resolves
//     the entry slot per selection, and refuses to import without a song or a
//     MIDI file (Copilot plan review #1 — the dedicated window does real
//     write-back too).
//
// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
// CoreState.ROM / CoreState.Undo. The real-ROM tests use RomFixture (full init)
// and snapshot/restore CoreState.Undo around the undo round-trip.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongTrackMidiImportTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SongTrackMidiImportTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // -----------------------------------------------------------------
        // Synthetic-ROM targeting: the entry SLOT, not the header offset.
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a tiny synthetic FE8U ROM with a one-entry song table at
        /// 0x200000 whose entry 0 points at a song header at 0x100000.
        ///   table[0] = 0x08100000 (header pointer) + 4-byte player type
        ///   header.B0 = 1 track, P4 = 0x08100200 instrument pointer
        /// sound_table_pointer is repointed to the synthetic table.
        /// </summary>
        static ROM MakeRomWithSongTable(out uint tableBase, out uint songHeaderAddr,
                                        out uint entrySlot)
        {
            var rom = new ROM();
            // FE8U RomInfo requires a >= 16 MB image (LoadLow size gate); the
            // synthetic song table walk reads RomInfo.sound_table_pointer.
            bool ok = rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            if (!ok || rom.RomInfo == null)
                throw new InvalidOperationException("synthetic FE8U ROM init failed");

            tableBase = 0x200000;
            songHeaderAddr = 0x100000;
            entrySlot = tableBase; // entry 0

            // Mark the region below 0x400 as "used" (non-zero, non-0xFF) so the
            // ImportMidiFile free-space search (FindFreeSpace from 0x100) cannot
            // return an offset < 0x200 — an all-zero synthetic ROM otherwise hands
            // back 0x100, which fails U.isSafetyOffset's >= 0x200 lower bound. Real
            // ROMs never have a multi-KB zero run at 0x100, so this only restores
            // realistic free-space layout for the synthetic image.
            for (int i = 0; i < 0x400; i++)
                rom.Data[i] = 0x55;

            // Song table entry 0 -> header0 pointer + dummy player type.
            WriteU32(rom.Data, (int)tableBase + 0, 0x08000000u | songHeaderAddr);
            WriteU32(rom.Data, (int)tableBase + 4, 0x00000000u);
            // Entry 1 -> a SECOND header (a writable, non-silence song so tests
            // that import need a non-zero songId). Entry 2 terminates the walk.
            uint song1Header = 0x110000;
            WriteU32(rom.Data, (int)tableBase + 8, 0x08000000u | song1Header);
            WriteU32(rom.Data, (int)tableBase + 12, 0x00000000u);
            WriteU32(rom.Data, (int)tableBase + 16, 0x00000000u);

            // Song header 0: 1 track, instrument pointer at +4, one track pointer.
            WriteSongHeader(rom, (int)songHeaderAddr, 0x100400);
            // Song header 1.
            WriteSongHeader(rom, (int)song1Header, 0x110400);

            // Repoint sound_table_pointer at the synthetic table.
            uint stp = rom.RomInfo.sound_table_pointer;
            WriteU32(rom.Data, (int)stp, 0x08000000u | tableBase);

            return rom;
        }

        static void WriteSongHeader(ROM rom, int hdr, uint trackDataOffset)
        {
            rom.Data[hdr + 0] = 0x01; // track count
            rom.Data[hdr + 1] = 0x10;
            rom.Data[hdr + 2] = 0x20;
            rom.Data[hdr + 3] = 0x80;
            WriteU32(rom.Data, hdr + 4, 0x08100200u);                 // instrument
            WriteU32(rom.Data, hdr + 8, 0x08000000u | trackDataOffset); // track 0 ptr
            rom.Data[(int)trackDataOffset] = 0xB2;
        }

        [Fact]
        public void GetSelectedSongTableEntryAddr_ReturnsEntrySlot_NotHeaderOffset()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out uint headerAddr, out uint entrySlot);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;

                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);

                // Select the first (and only) song. Its tag is the songId (0).
                vm.LoadEntry(list[0].addr);
                vm.SelectedSongIndex = (int)list[0].tag;

                uint resolved = vm.GetSelectedSongTableEntryAddr();
                // MUST be the table ENTRY slot, NOT the header offset.
                Assert.Equal(entrySlot, resolved);
                Assert.NotEqual(headerAddr, resolved);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void GetSelectedSongTableEntryAddr_HonoursCustomReadStartAddress()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;

                var vm = new SongTrackViewModel();
                // Simulate a user-edited custom scan base equal to the real table
                // (the View always populates ReadStartAddress from LoadFullList).
                vm.ReadStartAddress = tableBase;
                vm.SelectedSongIndex = 2; // arbitrary songId

                uint resolved = vm.GetSelectedSongTableEntryAddr();
                Assert.Equal(tableBase + 2u * 8u, resolved);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void GetSelectedSongTableEntryAddr_NoSelection_ReturnsNotFound()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel { SelectedSongIndex = -1 };
                Assert.Equal(U.NOT_FOUND, vm.GetSelectedSongTableEntryAddr());
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// A huge SelectedSongIndex whose <c>tableBase + index*8</c> would
        /// overflow/wrap a uint must NOT yield an in-range-looking-but-wrong
        /// offset: the method returns the NOT_FOUND sentinel and ImportMidi
        /// refuses with ZERO ROM mutation (it must never repoint the wrong
        /// address).
        /// </summary>
        [Fact]
        public void GetSelectedSongTableEntryAddr_OverflowIndex_ReturnsNotFound_AndImportRefuses()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);
                // Load a real (writable) song so CurrentAddr != 0 and the
                // SongID-0 guard is not what trips — then force an out-of-range
                // index that overflows the uint slot computation.
                vm.LoadEntry(list[list.Count - 1].addr);
                vm.SelectedSongIndex = int.MaxValue; // index*8 overflows uint

                Assert.Equal(U.NOT_FOUND, vm.GetSelectedSongTableEntryAddr());

                // ImportMidi must refuse (the sentinel = "no destination") with
                // zero mutation, even with a real MIDI file present.
                string midi = Path.Combine(Path.GetTempPath(), $"ovf_{Guid.NewGuid():N}.mid");
                WriteMinimalMidi(midi);
                try
                {
                    string? error = vm.ImportMidi(midi, out string summary);
                    Assert.False(string.IsNullOrEmpty(error));
                    Assert.Equal(string.Empty, summary);
                    Assert.Equal(before, rom.Data);
                }
                finally { try { File.Delete(midi); } catch { /* best effort */ } }
            }
            finally { CoreState.ROM = prevRom; }
        }

        // -----------------------------------------------------------------
        // Error paths — no throw, error string, zero mutation.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportMidi_NoSongLoaded_ReturnsErrorNoThrow()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                var vm = new SongTrackViewModel(); // nothing loaded
                string? error = vm.ImportMidi("nonexistent.mid", out string summary);

                Assert.False(string.IsNullOrEmpty(error));
                Assert.Equal(string.Empty, summary);
                Assert.Equal(before, rom.Data); // zero mutation
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportMidi_MissingFile_ReturnsErrorNoThrow()
        {
            var rom = MakeRomWithSongTable(out _, out uint headerAddr, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.True(list.Count >= 2);
                // Use song 1 (writable) so the missing-file error isn't masked
                // by the SongID-0 write-protect guard.
                vm.LoadEntry(list[1].addr);
                vm.SelectedSongIndex = (int)list[1].tag;

                string missing = Path.Combine(Path.GetTempPath(),
                    $"no_such_{Guid.NewGuid():N}.mid");
                string? error = vm.ImportMidi(missing, out string summary);

                Assert.False(string.IsNullOrEmpty(error));
                Assert.Equal(string.Empty, summary);
                Assert.Equal(before, rom.Data); // zero mutation
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// SongID 0 (silence) is write-protected: importing into it returns an
        /// error and mutates ZERO bytes (WF UseWriteProtectionID00 parity).
        /// </summary>
        [Fact]
        public void ImportMidi_SongId0_IsWriteProtected_NoMutation()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                vm.LoadEntry(list[0].addr);
                vm.SelectedSongIndex = 0; // silence song

                string? error = vm.ImportMidi("anything.mid", out string summary);
                Assert.False(string.IsNullOrEmpty(error));
                Assert.Contains("write-protected", error);
                Assert.Equal(string.Empty, summary);
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // -----------------------------------------------------------------
        // Dedicated import-window VM (SongTrackImportMidiView).
        // -----------------------------------------------------------------

        [Fact]
        public void ImportMidiVm_NoSongSelected_ReturnsError()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackImportMidiViewModel(); // nothing selected
                string? error = vm.ImportMidi(out string summary);
                Assert.False(string.IsNullOrEmpty(error));
                Assert.Equal(string.Empty, summary);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportMidiVm_LoadList_EnumeratesRealSongs_AndResolvesEntrySlot()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out uint headerAddr, out uint entrySlot);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackImportMidiViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count >= 2);
                Assert.Equal(headerAddr, list[0].addr);

                // Song 0 resolves to entry-slot 0 (production path: carry the
                // songId tag so a shared header maps to the selected slot).
                vm.LoadEntry(list[0].addr, list[0].tag);
                Assert.True(vm.IsLoaded);
                Assert.Equal(entrySlot, vm.SongTableEntryAddr);
                Assert.Equal(0x08100200u, vm.InstrumentAddr);

                // Select song 1 (a writable, non-silence song): selected but no
                // MIDI file -> error, no throw (and it is NOT the write-protect
                // error, proving song 1 passes the SongID-0 guard).
                vm.LoadEntry(list[1].addr, list[1].tag);
                Assert.True(vm.IsLoaded);
                Assert.Equal(1, vm.SelectedSongId);
                string? error = vm.ImportMidi(out string summary);
                Assert.False(string.IsNullOrEmpty(error));
                Assert.DoesNotContain("write-protected", error);
                Assert.Equal(string.Empty, summary);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // -----------------------------------------------------------------
        // Real-ROM round-trip + single-undo byte-identity.
        // -----------------------------------------------------------------

        [Fact]
        public void RoundTrip_RealRomFE8U_ExportThenImport_SucceedsAndUndoRestores()
        {
            if (!_fixture.IsAvailable || _fixture.ROM == null)
            {
                _output.WriteLine("SKIP: no ROM available (ROMS_DIR / roms/).");
                return;
            }

            ROM rom = _fixture.ROM;
            string tmpMidi = Path.Combine(Path.GetTempPath(),
                $"songtrack_rt_{Guid.NewGuid():N}.mid");

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);

                // Pick the first song that has at least one parseable track so
                // ExportMidi produces a non-trivial file. SongId 0 is silence —
                // skip it. Use the entry's tag as the songId.
                bool tested = false;
                foreach (AddrResult entry in list)
                {
                    if (entry.tag == 0) continue;
                    vm.LoadEntry(entry.addr);
                    vm.SelectedSongIndex = (int)entry.tag;
                    if (!vm.IsLoaded || vm.TrackCount == 0) continue;

                    // Export the current song to a temp MIDI.
                    string? exportErr = vm.ExportMidi(tmpMidi);
                    if (exportErr != null || !File.Exists(tmpMidi))
                    {
                        _output.WriteLine($"songId 0x{entry.tag:X02}: export skipped ({exportErr}).");
                        continue;
                    }

                    uint entrySlot = vm.GetSelectedSongTableEntryAddr();
                    Assert.NotEqual(U.NOT_FOUND, entrySlot);

                    // Snapshot the WHOLE ROM and the table-entry pointer before.
                    byte[] snapshot = (byte[])rom.Data.Clone();
                    uint pointerBefore = rom.u32(entrySlot);

                    // Import under a single undo record (exactly as the View does).
                    var undoService = new UndoService();
                    undoService.Begin("Import MIDI");
                    string? importErr = vm.ImportMidi(tmpMidi, out string summary);

                    if (importErr != null)
                    {
                        undoService.Rollback();
                        _output.WriteLine($"songId 0x{entry.tag:X02}: import returned '{importErr}', trying next.");
                        // Rollback must have restored the ROM byte-identical.
                        Assert.Equal(snapshot, rom.Data);
                        continue;
                    }

                    undoService.Commit();

                    // Success: summary populated, table entry repointed to a
                    // valid pointer in range, and the pointer actually CHANGED
                    // (proves the slot — not the header — was repointed).
                    Assert.False(string.IsNullOrEmpty(summary));
                    uint pointerAfter = rom.u32(entrySlot);
                    Assert.True(U.isPointer(pointerAfter), "new song pointer must be a valid GBA pointer");
                    Assert.True(U.isSafetyOffset(U.toOffset(pointerAfter)), "new song pointer must be in range");
                    Assert.NotEqual(pointerBefore, pointerAfter);
                    Assert.NotEqual(snapshot, rom.Data); // ROM really changed

                    // Exactly ONE undo record was pushed (the test started with a
                    // fresh CoreState.Undo, so the whole import — song-data
                    // write_range + the table-slot write_u32 — must compose into
                    // a single record at position 1).
                    Assert.True(CoreState.Undo!.IsModified);
                    Assert.Equal(1, CoreState.Undo.UndoBuffer.Count);
                    Assert.Equal(1, CoreState.Undo.Postion);

                    // Undo: the table entry pointer AND the whole ROM restore
                    // byte-identical to the pre-import snapshot, and the single
                    // record steps back to position 0 (nothing left applied).
                    CoreState.Undo.RunUndo();
                    Assert.Equal(0, CoreState.Undo.Postion);
                    Assert.Equal(pointerBefore, rom.u32(entrySlot));
                    Assert.Equal(snapshot, rom.Data);

                    _output.WriteLine(
                        $"songId 0x{entry.tag:X02}: import OK, pointer 0x{pointerBefore:X08} -> 0x{pointerAfter:X08}, undo byte-identical.");
                    tested = true;
                    break;
                }

                Assert.True(tested, "No FE8U song could be round-tripped through MIDI export/import.");
            }
            finally
            {
                CoreState.Undo = prevUndo;
                try { File.Delete(tmpMidi); } catch { /* best effort */ }
            }
        }

        // -----------------------------------------------------------------
        // Copilot PR review follow-ups.
        // -----------------------------------------------------------------

        /// <summary>
        /// A header shared by two song-table entries must resolve to the
        /// SELECTED slot (songId), not the first matching slot.
        /// </summary>
        [Fact]
        public void ImportMidiVm_SharedHeader_ResolvesSelectedSlot_NotFirstMatch()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out uint headerAddr, out uint entry0);
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                // Make entry 1 (slot tableBase+8) ALSO point at the same header
                // (a shared-header ROM), with entry 2 terminating the walk.
                WriteU32(rom.Data, (int)tableBase + 8, 0x08000000u | headerAddr);
                WriteU32(rom.Data, (int)tableBase + 16, 0x00000000u);

                var vm = new SongTrackImportMidiViewModel();

                // Selecting songId 1 must resolve to slot tableBase+8, NOT entry0.
                vm.LoadEntry(headerAddr, 1);
                Assert.True(vm.IsLoaded);
                Assert.Equal(tableBase + 8u, vm.SongTableEntryAddr);
                Assert.NotEqual(entry0, vm.SongTableEntryAddr);
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// After a successful parse, a later FAILED parse must clear the MIDI
        /// state so a stale (earlier) file cannot be imported.
        /// </summary>
        [Fact]
        public void ParseMidiInfo_FailureAfterSuccess_ClearsStaleMidiState()
        {
            var rom = MakeRomWithSongTable(out _, out _, out _);
            var prevRom = CoreState.ROM;
            // Author a real, minimal MIDI so the first parse succeeds.
            string goodMidi = Path.Combine(Path.GetTempPath(), $"good_{Guid.NewGuid():N}.mid");
            try
            {
                CoreState.ROM = rom;
                WriteMinimalMidi(goodMidi);

                var vm = new SongTrackImportMidiViewModel();
                Assert.Null(vm.ParseMidiInfo(goodMidi)); // success
                Assert.True(vm.HasMidiInfo);
                Assert.Equal(goodMidi, vm.MidiFilePath);

                // A later parse of a missing file must CLEAR the prior state.
                string missing = Path.Combine(Path.GetTempPath(), $"no_{Guid.NewGuid():N}.mid");
                Assert.NotNull(vm.ParseMidiInfo(missing));
                Assert.False(vm.HasMidiInfo);
                Assert.Equal(string.Empty, vm.MidiFilePath);
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(goodMidi); } catch { /* best effort */ }
            }
        }

        /// <summary>Write a tiny single-track Format-0 MIDI (header + one
        /// End-of-Track meta event) that SongMidiCore.ParseMidiFile accepts.</summary>
        static void WriteMinimalMidi(string path)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            // MThd: format 0, 1 track, 480 TPQN.
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
            bytes.AddRange(new byte[] { 0, 0, 0, 6 });   // header length
            bytes.AddRange(new byte[] { 0, 0 });          // format 0
            bytes.AddRange(new byte[] { 0, 1 });          // 1 track
            bytes.AddRange(new byte[] { 0x01, 0xE0 });    // 480 TPQN
            // MTrk: a single End-of-Track meta event.
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MTrk"));
            bytes.AddRange(new byte[] { 0, 0, 0, 4 });   // track length
            bytes.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 }); // delta + EoT
            File.WriteAllBytes(path, bytes.ToArray());
        }

        /// <summary>Write a Format-0, 1-track MIDI whose MTrk body contains a
        /// real NoteOn/NoteOff pair on channel 0 so SongMidiCore.ConvertMidiToGBA
        /// yields a NON-null song (it returns null when there are zero music
        /// channels with notes). Used by the #1002 Slice 2 pointer tests so the
        /// import actually succeeds and the header+4 assertion is reached.</summary>
        static void WriteMidiWithNote(string path)
        {
            var track = new System.Collections.Generic.List<byte>
            {
                0x00, 0x90, 0x3C, 0x64,   // delta 0, NoteOn  ch0 key=60 vel=100
                0x60, 0x80, 0x3C, 0x40,   // delta 96, NoteOff ch0 key=60 vel=64
                0x00, 0xFF, 0x2F, 0x00,   // delta 0, End-of-Track
            };

            var bytes = new System.Collections.Generic.List<byte>();
            // MThd: format 0, 1 track, 480 TPQN (same header as WriteMinimalMidi).
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
            bytes.AddRange(new byte[] { 0, 0, 0, 6 });   // header length
            bytes.AddRange(new byte[] { 0, 0 });          // format 0
            bytes.AddRange(new byte[] { 0, 1 });          // 1 track
            bytes.AddRange(new byte[] { 0x01, 0xE0 });    // 480 TPQN
            // MTrk: the NoteOn/NoteOff body. Length is big-endian 4 bytes.
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MTrk"));
            int len = track.Count;
            bytes.AddRange(new byte[]
            {
                (byte)((len >> 24) & 0xFF), (byte)((len >> 16) & 0xFF),
                (byte)((len >> 8) & 0xFF), (byte)(len & 0xFF),
            });
            bytes.AddRange(track);
            File.WriteAllBytes(path, bytes.ToArray());
        }

        // -----------------------------------------------------------------
        // #1002 Slice 2: instrument-pointer correctness — end-to-end assertion
        // that the MIDI writer stores the chosen GBA POINTER verbatim in header +4.
        // -----------------------------------------------------------------

        /// <summary>
        /// Regression test for the #1002 Slice 2 pointer-vs-offset correctness
        /// contract: after setting <c>vm.InstrumentAddr</c> to a chosen GBA
        /// POINTER and calling <c>vm.ImportMidi</c>, the freshly-written song
        /// header's +4 slot (read back from ROM) must equal that same POINTER
        /// verbatim. This proves the View's "store as POINTER" step correctly feeds
        /// <see cref="SongMidiCore.ImportMidiFile"/> (which writes verbatim) and
        /// that the offset/pointer conversion is NOT applied a second time.
        /// </summary>
        [Fact]
        public void ImportMidi_HonorsChosenInstrumentPointer()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out _, out _);
            var prevRom = CoreState.ROM;
            // A fresh Undo so ambient writes are captured.
            var prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                CoreState.ROM = rom;

                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                // Select song 1 (writable, non-silence).
                Assert.True(list.Count >= 2);
                vm.LoadEntry(list[1].addr);
                vm.SelectedSongIndex = (int)list[1].tag;
                Assert.True(vm.IsLoaded);

                // Resolve the song-table ENTRY slot for the SELECTED song (song 1
                // lives at tableBase+8, NOT entry 0) so the read-back below targets
                // the slot the import actually repoints.
                uint entrySlot = vm.GetSelectedSongTableEntryAddr();
                Assert.NotEqual(U.NOT_FOUND, entrySlot);

                // Choose a distinct instrument set address (as a POINTER).
                // Pick an offset inside U.isSafetyOffset range (>= 0x200).
                uint chosenOffset = 0x100400u;            // inside our 16MB synthetic ROM
                uint chosenPointer = U.toPointer(chosenOffset); // 0x08100400

                // Simulate what the View does after the instrument picker confirms:
                vm.InstrumentAddr = chosenPointer;

                // Write a MIDI WITH A NOTE so ConvertMidiToGBA yields a non-null
                // song (a note-less MIDI returns "no valid music tracks", which
                // would short-circuit the import and skip the assertion below).
                string midiPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"slice2_{Guid.NewGuid():N}.mid");
                WriteMidiWithNote(midiPath);
                try
                {
                    var undoSvc = new UndoService();
                    undoSvc.Begin("Import MIDI");
                    string? error = vm.ImportMidi(midiPath, out string summary);

                    if (error != null)
                    {
                        undoSvc.Rollback();
                        // A MIDI with a real note MUST import — anything else is a
                        // hard failure (do NOT silently skip the header+4 assertion).
                        Assert.True(false, "ImportMidi must succeed for a MIDI with a note: " + error);
                        return;
                    }

                    undoSvc.Commit();

                    // Read back the freshly-written song-table entry -> new header.
                    uint newHeaderPtr = rom.u32(entrySlot);
                    Assert.True(U.isPointer(newHeaderPtr),
                        "Song-table entry must point to a valid GBA header.");
                    uint newHeaderOffset = U.toOffset(newHeaderPtr);
                    Assert.True(U.isSafetyOffset(newHeaderOffset, rom),
                        "New header offset must be in ROM range.");

                    // Header +4 must equal the chosen POINTER (verbatim write).
                    uint storedInstrPtr = rom.u32(newHeaderOffset + 4);
                    Assert.Equal(chosenPointer, storedInstrPtr);
                }
                finally
                {
                    try { System.IO.File.Delete(midiPath); } catch { /* best effort */ }
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        /// <summary>
        /// Same assertion via <see cref="SongTrackImportMidiViewModel"/> (the
        /// standalone import window's VM): LoadList → LoadEntry a real song →
        /// override InstrumentAddr with a chosen pointer → ParseMidiInfo →
        /// ImportMidi → assert header+4 == chosen pointer end-to-end.
        /// </summary>
        [Fact]
        public void ImportMidiVm_StandaloneHonorsChosenInstrument()
        {
            var rom = MakeRomWithSongTable(out uint tableBase, out _, out _);
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                CoreState.ROM = rom;

                var vm = new SongTrackImportMidiViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count >= 2);
                // Select song 1 (writable, non-silence).
                vm.LoadEntry(list[1].addr, list[1].tag);
                Assert.True(vm.IsLoaded);
                Assert.Equal(1, vm.SelectedSongId);

                // Capture the song-table ENTRY slot for the selected song BEFORE
                // the import — the VM's post-import LoadEntry re-resolves
                // SongTableEntryAddr from the freshly-written header, so we must
                // read the slot the import actually repointed, not the reloaded one.
                uint entrySlot = vm.SongTableEntryAddr;
                Assert.NotEqual(0u, entrySlot);

                // Override with our chosen instrument pointer (GBA pointer slot).
                uint chosenOffset = 0x100400u;
                uint chosenPointer = U.toPointer(chosenOffset); // 0x08100400
                vm.InstrumentAddr = chosenPointer;

                // Parse a MIDI WITH A NOTE so HasMidiInfo is set AND the import
                // yields a non-null song (a note-less MIDI returns "no valid music
                // tracks", which would short-circuit the import).
                string midiPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"slice2vm_{Guid.NewGuid():N}.mid");
                WriteMidiWithNote(midiPath);
                try
                {
                    string? parseErr = vm.ParseMidiInfo(midiPath);
                    Assert.Null(parseErr); // parse must succeed
                    Assert.True(vm.HasMidiInfo);

                    var undoSvc = new UndoService();
                    undoSvc.Begin("Import MIDI");
                    string? error = vm.ImportMidi(out string summary);

                    if (error != null)
                    {
                        undoSvc.Rollback();
                        // A MIDI with a real note MUST import — anything else is a
                        // hard failure (do NOT silently skip the header+4 assertion).
                        Assert.True(false, "ImportMidi must succeed for a MIDI with a note: " + error);
                        return;
                    }

                    undoSvc.Commit();

                    // Re-read the freshly-written song header from the captured
                    // table entry slot (the slot the import repointed).
                    uint newHeaderPtr = rom.u32(entrySlot);
                    Assert.True(U.isPointer(newHeaderPtr),
                        "Table entry must point to a valid GBA header.");
                    uint newHeaderOffset = U.toOffset(newHeaderPtr);
                    Assert.True(U.isSafetyOffset(newHeaderOffset, rom),
                        "New header offset must be in ROM range.");

                    // Header +4 must equal the chosen POINTER verbatim.
                    uint storedInstrPtr = rom.u32(newHeaderOffset + 4);
                    Assert.Equal(chosenPointer, storedInstrPtr);
                }
                finally
                {
                    try { System.IO.File.Delete(midiPath); } catch { /* best effort */ }
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
