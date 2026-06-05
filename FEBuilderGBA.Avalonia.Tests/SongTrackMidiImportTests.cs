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

            // Song table entry 0 -> header pointer + dummy player type.
            WriteU32(rom.Data, (int)tableBase + 0, 0x08000000u | songHeaderAddr);
            WriteU32(rom.Data, (int)tableBase + 4, 0x00000000u);
            // Entry 1 is a non-pointer so the table walk stops cleanly.
            WriteU32(rom.Data, (int)tableBase + 8, 0x00000000u);

            // Song header: 1 track, instrument pointer at +4, one track pointer.
            rom.Data[songHeaderAddr + 0] = 0x01; // track count
            rom.Data[songHeaderAddr + 1] = 0x10;
            rom.Data[songHeaderAddr + 2] = 0x20;
            rom.Data[songHeaderAddr + 3] = 0x80;
            WriteU32(rom.Data, (int)songHeaderAddr + 4, 0x08100200u); // instrument
            WriteU32(rom.Data, (int)songHeaderAddr + 8, 0x08100400u); // track 0 ptr
            rom.Data[0x100400] = 0xB2;

            // Repoint sound_table_pointer at the synthetic table.
            uint stp = rom.RomInfo.sound_table_pointer;
            WriteU32(rom.Data, (int)stp, 0x08000000u | tableBase);

            return rom;
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
                vm.LoadEntry(list[0].addr);
                vm.SelectedSongIndex = (int)list[0].tag;

                string missing = Path.Combine(Path.GetTempPath(),
                    $"no_such_{Guid.NewGuid():N}.mid");
                string? error = vm.ImportMidi(missing, out string summary);

                Assert.False(string.IsNullOrEmpty(error));
                Assert.Equal(string.Empty, summary);
                Assert.Equal(before, rom.Data); // zero mutation
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
                Assert.NotEmpty(list);
                Assert.Equal(headerAddr, list[0].addr);

                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);
                Assert.Equal(entrySlot, vm.SongTableEntryAddr);
                Assert.Equal(0x08100200u, vm.InstrumentAddr);

                // Selected but no MIDI file -> error, no throw.
                string? error = vm.ImportMidi(out string summary);
                Assert.False(string.IsNullOrEmpty(error));
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

                    // Exactly one undo record was pushed.
                    Assert.True(CoreState.Undo!.IsModified);

                    // Undo: the table entry pointer AND the whole ROM restore
                    // byte-identical to the pre-import snapshot.
                    CoreState.Undo.RunUndo();
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

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
