// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Bulk Track Change editor (#1015): the Song Track "All Tracks"
// jump now passes the SONG HEADER address so the target re-derives the distinct
// 0xBD voices, the Apply button + handler are wired, and LoadEntry on a
// synthetic song builds one editable VoiceRow per distinct voice.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class BulkTrackChangeTests
    {
        // ------------------------------------------------------------------
        // Source-text assertions (no GUI / ROM needed).
        // ------------------------------------------------------------------

        [Fact]
        public void AllTracksJump_PassesSongHeaderAddr_NotInstrumentAddr()
        {
            string src = File.ReadAllText(ViewPath("SongTrackView.axaml.cs"));

            // The jump must carry the SONG HEADER address (CurrentAddr) so the
            // Bulk Track Change editor re-derives the track list + voices.
            Assert.Contains(
                "Navigate<SongTrackAllChangeTrackView>(_vm.CurrentAddr)", src);
            // And must NOT still pass the instrument-set pointer.
            Assert.DoesNotContain(
                "Navigate<SongTrackAllChangeTrackView>(_vm.InstrumentAddr)", src);
        }

        [Fact]
        public void View_WiresApplyButtonAndHandler()
        {
            string axaml = File.ReadAllText(ViewPath("SongTrackAllChangeTrackView.axaml"));
            string cs = File.ReadAllText(ViewPath("SongTrackAllChangeTrackView.axaml.cs"));

            // Apply button declared with a Click handler + the voice list bound.
            Assert.Contains("Click=\"Apply_Click\"", axaml);
            Assert.Contains("ItemsSource=\"{Binding Rows}\"", axaml);

            // Handler runs under an undo scope: Begin + Commit/Rollback.
            Assert.Contains("void Apply_Click", cs);
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
            Assert.Contains("_vm.ApplyChanges()", cs);
        }

        // ------------------------------------------------------------------
        // Behavioral test on a synthetic in-memory FE8U ROM.
        // ------------------------------------------------------------------

        [Fact]
        public void LoadEntry_BuildsOneRowPerDistinctVoice()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint songAddr = PlantSong(rom);

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(songAddr);

                Assert.True(vm.IsLoaded);
                // Synthetic song uses voices {5, 9, 17} across two tracks
                // (duplicates collapsed) -> 3 distinct rows, > 1.
                Assert.Equal(3, vm.Rows.Count);
                Assert.True(vm.Rows.Count > 1);

                // Each row is seeded To == From (no-op until edited).
                foreach (var row in vm.Rows)
                    Assert.Equal(row.From, row.To);

                // SongAddr stored; InstrumentAddr read from header +4.
                Assert.Equal(songAddr, vm.SongAddr);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ApplyChanges_NoEdits_IsNoOp()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint songAddr = PlantSong(rom);
                byte[] before = (byte[])rom.Data.Clone();

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(songAddr);

                // No row edited -> empty map -> "" and zero mutation.
                Assert.Equal("", vm.ApplyChanges());
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ApplyChanges_EditedRow_RewritesVoiceBytes()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint songAddr = PlantSong(rom);

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(songAddr);

                // Edit the voice-5 row to 42.
                foreach (var row in vm.Rows)
                    if (row.From == 5) row.To = 42;

                Assert.Equal("", vm.ApplyChanges());

                // Re-derive: voice 5 is gone, 42 appears.
                vm.LoadEntry(songAddr);
                Assert.DoesNotContain(vm.Rows, r => r.From == 5);
                Assert.Contains(vm.Rows, r => r.From == 42);
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        const uint SongHeader = 0x1000;
        const uint Track0Data = 0x1100;
        const uint Track1Data = 0x1140;

        /// <summary>Plant a 2-track song with voices {5,9,17} and return the
        /// song HEADER offset.</summary>
        static uint PlantSong(ROM rom)
        {
            WritePtr(rom, SongHeader + 4, 0x2000);          // instrument set (unused)
            rom.Data[SongHeader + 0] = 2;                   // trackCount
            WritePtr(rom, SongHeader + 8, Track0Data);
            WritePtr(rom, SongHeader + 12, Track1Data);

            // Track0: BD 05, BD 09, BD 05, B1
            uint a = Track0Data;
            rom.Data[a++] = 0xBD; rom.Data[a++] = 5;
            rom.Data[a++] = 0xBD; rom.Data[a++] = 9;
            rom.Data[a++] = 0xBD; rom.Data[a++] = 5;
            rom.Data[a++] = 0xB1;

            // Track1: BD 05, BD 17, B1
            a = Track1Data;
            rom.Data[a++] = 0xBD; rom.Data[a++] = 5;
            rom.Data[a++] = 0xBD; rom.Data[a++] = 17;
            rom.Data[a++] = 0xB1;

            return SongHeader;
        }

        static void WritePtr(ROM rom, uint at, uint targetOffset)
        {
            uint ptr = targetOffset + 0x08000000;
            rom.Data[at + 0] = (byte)(ptr & 0xFF);
            rom.Data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            rom.Data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            rom.Data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        static ROM MakeMinimalFE8URom()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        static string ViewPath(string fileName)
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views", fileName);

        static string FindProjectRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            string cwd = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(cwd, "FEBuilderGBA.sln")))
                    return cwd;
                string? parent = Path.GetDirectoryName(cwd);
                if (parent == null || parent == cwd) break;
                cwd = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }
    }
}
