// SPDX-License-Identifier: GPL-3.0-or-later
// SongTrack inline .instrument export tests (#1609).
//
// Proves the Avalonia Song Track editor's "Export Music File" button now offers
// .instrument export inline (WinForms parity: SongTrackForm.cs:459-466), reusing
// the existing read-only Core seam SongInstrumentSetCore.ExportAll (the same one
// the Song Instrument editor already calls). The new VM method
// SongTrackViewModel.ExportInstrumentSet:
//   - resolves the loaded song's instrument-set pointer from InstrumentAddr
//     (= rom.u32(songHeader + 4) = WinForms P4.Value);
//   - is READ-ONLY: no ROM mutation, no undo;
//   - rejects the no-song / no-output-path / null-P4 / OUT-OF-RANGE-P4 cases with
//     an error string and ZERO files written (Copilot plan review pt 1 — the void
//     Core API silently no-ops on an unsafe base, so an edited/stale nonzero P4
//     must NOT produce a false-success toast);
//   - on a SEEDED voicegroup emits a non-empty .instrument index whose content is
//     byte-identical to the same vocaBase exported directly through Core (Copilot
//     plan review pt 2 — the seeded voice makes the parity assertion non-vacuous).
//
// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
// CoreState.ROM. The real-ROM test uses RomFixture (full init) and is SKIPPED when
// no ROM is available.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongTrackInstrumentExportTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SongTrackInstrumentExportTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        const uint TableBase = 0x200000;
        const uint SongHeaderAddr = 0x100000;
        const uint VocaBaseOffset = 0x100200;          // where P4 points (the voicegroup)
        const uint VocaBasePointer = 0x08000000u | VocaBaseOffset;

        /// <summary>
        /// Build a synthetic FE8U ROM with a one-entry song table at 0x200000 whose
        /// entry 0 points at a song header at 0x100000. The header's P4 (+4) points
        /// at a voicegroup at <see cref="VocaBaseOffset"/> seeded with ONE valid
        /// "square/noise" voice (type 0x01 — a no-pointer type that
        /// <c>SongInstrumentSetCore.IsValidVoice</c> always accepts), so
        /// <c>CountVoices</c> returns 1 and <c>ExportAll</c> emits a non-empty index
        /// (the prior all-zero voicegroup yielded an EMPTY index — Copilot review pt 2).
        /// </summary>
        static ROM MakeRomWithSeededVoicegroup()
        {
            var rom = new ROM();
            // FE8U RomInfo requires a >= 16 MB image (LoadLow size gate).
            bool ok = rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            if (!ok || rom.RomInfo == null)
                throw new InvalidOperationException("synthetic FE8U ROM init failed");

            // Keep the low region "used" so any free-space scan stays realistic
            // (not strictly needed for the read-only export, but matches the sibling
            // SongTrackMidiImportTests helper).
            for (int i = 0; i < 0x400; i++) rom.Data[i] = 0x55;

            // Song table entry 0 -> header pointer + dummy player type; entry 1 = 0
            // terminates the table walk.
            WriteU32(rom.Data, (int)TableBase + 0, 0x08000000u | SongHeaderAddr);
            WriteU32(rom.Data, (int)TableBase + 4, 0x00000000u);
            WriteU32(rom.Data, (int)TableBase + 8, 0x00000000u);

            // Song header: 1 track, P4 = the voicegroup pointer, one track pointer.
            rom.Data[(int)SongHeaderAddr + 0] = 0x01; // track count
            rom.Data[(int)SongHeaderAddr + 1] = 0x10;
            rom.Data[(int)SongHeaderAddr + 2] = 0x20;
            rom.Data[(int)SongHeaderAddr + 3] = 0x80;
            WriteU32(rom.Data, (int)SongHeaderAddr + 4, VocaBasePointer);              // P4
            WriteU32(rom.Data, (int)SongHeaderAddr + 8, 0x08000000u | 0x100400u);      // track 0
            rom.Data[0x100400] = 0xB2;

            // Voicegroup: one VALID voice (type 0x01 = square/noise, no pointer).
            // The next 12-byte slot stays type 0x00 with a null +4 pointer, which
            // fails isSafetyPointer and stops the voice scan -> exactly 1 voice.
            WriteVoiceSquare(rom, VocaBaseOffset, 0x01);

            // Repoint sound_table_pointer at the synthetic table.
            uint stp = rom.RomInfo.sound_table_pointer;
            WriteU32(rom.Data, (int)stp, 0x08000000u | TableBase);

            return rom;
        }

        // A no-pointer "square/noise" voice (type 0x01). +4..+11 are raw data bytes.
        static void WriteVoiceSquare(ROM rom, uint addr, byte type)
        {
            rom.Data[(int)addr + 0] = type;
            rom.Data[(int)addr + 1] = 0x3C;
            rom.Data[(int)addr + 2] = 0x00;
            rom.Data[(int)addr + 3] = 0x00;
            // +4..+11: arbitrary non-zero data so the emitted row's data columns are
            // deterministic (square/noise envelope-style bytes).
            for (int i = 4; i < 12; i++) rom.Data[(int)addr + i] = (byte)(0xA0 + i);
        }

        // -----------------------------------------------------------------
        // Error paths — error string, ZERO files written, no throw.
        // -----------------------------------------------------------------

        [Fact]
        public void ExportInstrumentSet_NoSongLoaded_ReturnsError_NoFile()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel(); // nothing loaded

                string dir = NewTempDir();
                string path = Path.Combine(dir, "song.instrument");
                try
                {
                    string? error = vm.ExportInstrumentSet(path);
                    Assert.False(string.IsNullOrEmpty(error));
                    Assert.False(File.Exists(path));
                    Assert.Empty(Directory.GetFiles(dir));
                }
                finally { CleanupDir(dir); }
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ExportInstrumentSet_ZeroInstrumentAddr_ReturnsError_NoFile()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);
                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);

                // Force a null instrument set (P4 == 0).
                vm.InstrumentAddr = 0;

                string dir = NewTempDir();
                string path = Path.Combine(dir, "song.instrument");
                try
                {
                    string? error = vm.ExportInstrumentSet(path);
                    Assert.False(string.IsNullOrEmpty(error));
                    Assert.False(File.Exists(path));
                    Assert.Empty(Directory.GetFiles(dir));
                }
                finally { CleanupDir(dir); }
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// (Copilot plan review pt 1) A loaded song whose P4 is nonzero but
        /// OUT-OF-RANGE must be rejected with an error and NO file written — the
        /// prevalidation guards against the void Core API silently no-opping into a
        /// false-success toast with no .instrument index.
        /// </summary>
        [Fact]
        public void ExportInstrumentSet_InvalidNonzeroInstrumentAddr_ReturnsError_NoFile()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);
                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);

                // A nonzero but clearly out-of-range pointer (past EOF of the 16 MB
                // synthetic image, and far outside the cartridge window).
                vm.InstrumentAddr = 0x09FFFFF0u;

                string dir = NewTempDir();
                string path = Path.Combine(dir, "song.instrument");
                try
                {
                    string? error = vm.ExportInstrumentSet(path);
                    Assert.False(string.IsNullOrEmpty(error));
                    Assert.Contains("out of range", error);
                    Assert.False(File.Exists(path));
                    Assert.Empty(Directory.GetFiles(dir)); // no false-success file
                }
                finally { CleanupDir(dir); }
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ExportInstrumentSet_EmptyPath_ReturnsError()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);

                string? error = vm.ExportInstrumentSet(string.Empty);
                Assert.False(string.IsNullOrEmpty(error));
            }
            finally { CoreState.ROM = prevRom; }
        }

        // -----------------------------------------------------------------
        // Happy path — seeded voicegroup yields a NON-EMPTY index.
        // -----------------------------------------------------------------

        [Fact]
        public void ExportInstrumentSet_SeededVoicegroup_WritesNonEmptyIndex()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                Assert.NotEmpty(list);
                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);
                // The header's P4 points at the seeded voicegroup.
                Assert.Equal(VocaBasePointer, vm.InstrumentAddr);

                string dir = NewTempDir();
                string path = Path.Combine(dir, "voicegroup.instrument");
                try
                {
                    string? error = vm.ExportInstrumentSet(path);
                    Assert.Null(error); // success
                    Assert.True(File.Exists(path));

                    // The index has >= 1 data row (the seeded square/noise voice) —
                    // proves real instrument-set content was emitted, not an empty file.
                    string[] lines = File.ReadAllLines(path);
                    Assert.NotEmpty(lines);
                    Assert.True(lines.Length >= 1,
                        "the seeded voicegroup must produce at least one index row");
                    // First column of row 0 is the voice type hex (U.ToHexString
                    // emits "01" for the seeded 0x01 square/noise voice).
                    Assert.Equal(U.ToHexString(0x01), lines[0].Split('\t')[0]);
                }
                finally { CleanupDir(dir); }
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// (Copilot plan review pt 2) The index produced via the SongTrack VM path
        /// is byte-identical to the same vocaBase exported directly through the Core
        /// seam (the one the Song Instrument editor calls). Non-vacuous because the
        /// seeded voicegroup yields rows.
        /// </summary>
        [Fact]
        public void ExportInstrumentSet_MatchesSongInstrumentCoreExport()
        {
            var rom = MakeRomWithSeededVoicegroup();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTrackViewModel();
                var list = vm.LoadFullList();
                vm.LoadEntry(list[0].addr);
                Assert.True(vm.IsLoaded);
                Assert.Equal(VocaBasePointer, vm.InstrumentAddr);

                // (a) Export via the new SongTrack VM path.
                string dirVm = NewTempDir();
                string pathVm = Path.Combine(dirVm, "voicegroup.instrument");

                // (b) Export the SAME vocaBase directly through Core (mirrors the
                //     Song Instrument editor call) into an in-memory sink.
                var sink = new System.Collections.Generic.Dictionary<string, string[]>();
                try
                {
                    string? error = vm.ExportInstrumentSet(pathVm);
                    Assert.Null(error);

                    SongInstrumentSetCore.ExportAll(
                        rom, vm.InstrumentAddr, "voicegroup",
                        (name, bytes) => { /* no side files for a square/noise voice */ },
                        (name, lines) => sink[name] = lines.ToArray());

                    Assert.True(sink.ContainsKey("voicegroup.instrument"),
                        "Core export must emit the top-level index file");

                    string[] viaVm = File.ReadAllLines(pathVm);
                    string[] viaCore = sink["voicegroup.instrument"];
                    Assert.Equal(viaCore, viaVm); // byte/row-identical parity
                }
                finally { CleanupDir(dirVm); }
            }
            finally { CoreState.ROM = prevRom; }
        }

        // -----------------------------------------------------------------
        // Real-ROM smoke: export a real FE8U song's instrument set.
        // -----------------------------------------------------------------

        [Fact]
        public void ExportInstrumentSet_RealRomFE8U_WritesInstrumentIndex()
        {
            if (!_fixture.IsAvailable || _fixture.ROM == null)
            {
                _output.WriteLine("SKIP: no ROM available (ROMS_DIR / roms/).");
                return;
            }

            var vm = new SongTrackViewModel();
            var list = vm.LoadFullList();
            Assert.NotEmpty(list);

            // Find a song whose P4 (InstrumentAddr) is a valid in-range pointer.
            bool tested = false;
            foreach (AddrResult entry in list)
            {
                vm.LoadEntry(entry.addr);
                if (!vm.IsLoaded || vm.InstrumentAddr == 0) continue;
                if (!U.isSafetyOffset(U.toOffset(vm.InstrumentAddr), _fixture.ROM)) continue;

                string dir = NewTempDir();
                string path = Path.Combine(dir, $"song_0x{entry.tag:X02}.instrument");
                try
                {
                    string? error = vm.ExportInstrumentSet(path);
                    if (error != null)
                    {
                        _output.WriteLine($"songId 0x{entry.tag:X02}: export error ({error}), trying next.");
                        continue;
                    }
                    Assert.True(File.Exists(path));
                    string[] lines = File.ReadAllLines(path);
                    Assert.NotEmpty(lines);
                    _output.WriteLine(
                        $"songId 0x{entry.tag:X02}: instrument index {lines.Length} rows -> {path}");
                    tested = true;
                    break;
                }
                finally { CleanupDir(dir); }
            }

            Assert.True(tested, "No FE8U song with a valid instrument set could be exported.");
        }

        // -----------------------------------------------------------------
        // Helpers.
        // -----------------------------------------------------------------

        static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"inst_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void CleanupDir(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
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
