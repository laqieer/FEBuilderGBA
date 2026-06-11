// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the single-track Track Change editor and the completed bulk
// Vol/Pan/Tempo affordances (#1002 Slice 1):
//   * SongTrackChangeTrackView is no longer a stub — it builds a real voice list
//     from one track (ParseSingleTrackFromDataOffset), wires Apply under an undo
//     scope, and exposes Vol/Pan/Velocity nudge inputs.
//   * SongTrackAllChangeTrackView gains the previously-deferred Vol/Pan/Tempo
//     inputs; HasPendingChanges is true for a Vol/Pan/Tempo-only edit (Finding 5).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongTrackChangeTests
    {
        // ------------------------------------------------------------------
        // Single-track editor — no longer a placeholder.
        // ------------------------------------------------------------------

        [Fact]
        public void SingleTrackView_IsNoLongerPlaceholder()
        {
            string axaml = File.ReadAllText(ViewPath("SongTrackChangeTrackView.axaml"));
            string cs = File.ReadAllText(ViewPath("SongTrackChangeTrackView.axaml.cs"));

            // Real voice-remap list bound to Rows + Apply button with a handler.
            Assert.Contains("ItemsSource=\"{Binding Rows}\"", axaml);
            Assert.Contains("Click=\"Apply_Click\"", axaml);

            // Vol / Pan / Velocity nudge inputs present.
            Assert.Contains("SongTrackChangeTrack_Vol_Input", axaml);
            Assert.Contains("SongTrackChangeTrack_Pan_Input", axaml);
            Assert.Contains("SongTrackChangeTrack_Velocity_Check", axaml);

            // Apply runs under an undo scope and routes through the VM.
            Assert.Contains("void Apply_Click", cs);
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
            Assert.Contains("_vm.ApplyChanges()", cs);
        }

        [Fact]
        public void SingleTrackViewModel_LoadEntry_BuildsVoiceRows()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                PlantSong(rom);

                var vm = new SongTrackChangeTrackViewModel();
                // Track0 @0x1100 uses voices {5, 9, 5} -> dedup {5, 9}.
                vm.LoadEntry(Track0Data);

                Assert.True(vm.IsLoaded);
                Assert.Equal(2, vm.Rows.Count);
                foreach (var row in vm.Rows)
                    Assert.Equal(row.From, row.To); // seeded no-op
                Assert.Equal(Track0Data, vm.TrackDataOffset);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void SingleTrackViewModel_ApplyVoiceRemap_RewritesTrack()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                PlantSong(rom);

                var vm = new SongTrackChangeTrackViewModel();
                vm.LoadEntry(Track0Data);
                foreach (var row in vm.Rows)
                    if (row.From == 5) row.To = 42;

                Assert.Equal("", vm.ApplyChanges());

                vm.LoadEntry(Track0Data);
                Assert.DoesNotContain(vm.Rows, r => r.From == 5);
                Assert.Contains(vm.Rows, r => r.From == 42);
                Assert.Contains(vm.Rows, r => r.From == 9); // untouched
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void SingleTrackViewModel_VolPanOnly_HasPendingChanges()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                PlantSong(rom);

                var vm = new SongTrackChangeTrackViewModel();
                vm.LoadEntry(Track0Data);

                Assert.False(vm.HasPendingChanges); // nothing edited yet
                vm.DVol = 5;
                Assert.True(vm.HasPendingChanges);  // Vol-only edit is NOT a no-op
                vm.DVol = 0;
                vm.DPan = -3;
                Assert.True(vm.HasPendingChanges);  // Pan-only edit is NOT a no-op
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void SingleTrackViewModel_ApplyVolDelta_NudgesVolBytes()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                // Track2 @0x1180: BE 50, B1 (a VOL command).
                WritePtr(rom, SongHeader + 0, 0); // ensure clean
                rom.Data[SongHeader + 0] = 3;     // 3 tracks
                WritePtr(rom, SongHeader + 4, 0x2000);
                WritePtr(rom, SongHeader + 8, Track0Data);
                WritePtr(rom, SongHeader + 12, Track1Data);
                WritePtr(rom, SongHeader + 16, Track2Data);
                rom.Data[Track0Data] = 0xB1;
                rom.Data[Track1Data] = 0xB1;
                rom.Data[Track2Data + 0] = 0xBE; rom.Data[Track2Data + 1] = 50; rom.Data[Track2Data + 2] = 0xB1;

                var vm = new SongTrackChangeTrackViewModel();
                vm.LoadEntry(Track2Data);
                vm.DVol = 10;

                Assert.Equal("", vm.ApplyChanges());
                Assert.Equal(60, rom.Data[Track2Data + 1]); // 50 + 10
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // Bulk editor — Vol/Pan/Tempo now wired.
        // ------------------------------------------------------------------

        [Fact]
        public void BulkView_HasVolPanTempoInputs()
        {
            string axaml = File.ReadAllText(ViewPath("SongTrackAllChangeTrackView.axaml"));

            Assert.Contains("SongTrackAllChangeTrack_Vol_Input", axaml);
            Assert.Contains("SongTrackAllChangeTrack_Pan_Input", axaml);
            Assert.Contains("SongTrackAllChangeTrack_Tempo_Input", axaml);
            Assert.Contains("SongTrackAllChangeTrack_Velocity_Check", axaml);
            // The old "not yet ported" deferral note is gone.
            Assert.DoesNotContain("not yet ported", axaml);
        }

        [Fact]
        public void BulkViewModel_TempoOnly_HasPendingChanges()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint songAddr = PlantSong(rom);

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(songAddr);

                Assert.False(vm.HasPendingChanges); // no edit
                // Finding 5: a Tempo-only edit (no voice row changed) is NOT a no-op.
                vm.DTempo = 7;
                Assert.True(vm.HasPendingChanges);
                vm.DTempo = 0;
                vm.DVol = 3;
                Assert.True(vm.HasPendingChanges);  // Vol-only too
                vm.DVol = 0;
                vm.DPan = -2;
                Assert.True(vm.HasPendingChanges);  // Pan-only too
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void BulkViewModel_ApplyTempoDelta_NudgesEveryTrackTempo()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                // 2-track song, each with a TEMPO command (0xBB).
                rom.Data[SongHeader + 0] = 2;
                WritePtr(rom, SongHeader + 4, 0x2000);
                WritePtr(rom, SongHeader + 8, Track0Data);
                WritePtr(rom, SongHeader + 12, Track1Data);
                rom.Data[Track0Data + 0] = 0xBB; rom.Data[Track0Data + 1] = 200; rom.Data[Track0Data + 2] = 0xB1;
                rom.Data[Track1Data + 0] = 0xBB; rom.Data[Track1Data + 1] = 100; rom.Data[Track1Data + 2] = 0xB1;

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(SongHeader);
                vm.DTempo = 100;

                Assert.Equal("", vm.ApplyChanges());
                // TEMPO clamps 0..255 (NOT 127): 200+100=300 -> 255; 100+100=200.
                Assert.Equal(255, rom.Data[Track0Data + 1]);
                Assert.Equal(200, rom.Data[Track1Data + 1]);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void BulkViewModel_ApplyVoicePlusTempo_BothApplied()
        {
            var rom = MakeMinimalFE8URom();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                // 1-track song with a voice AND a tempo command.
                rom.Data[SongHeader + 0] = 1;
                WritePtr(rom, SongHeader + 4, 0x2000);
                WritePtr(rom, SongHeader + 8, Track0Data);
                rom.Data[Track0Data + 0] = 0xBD; rom.Data[Track0Data + 1] = 5;
                rom.Data[Track0Data + 2] = 0xBB; rom.Data[Track0Data + 3] = 100;
                rom.Data[Track0Data + 4] = 0xB1;

                var vm = new SongTrackAllChangeTrackViewModel();
                vm.LoadEntry(SongHeader);
                foreach (var row in vm.Rows)
                    if (row.From == 5) row.To = 42;
                vm.DTempo = 20;

                Assert.Equal("", vm.ApplyChanges());
                Assert.Equal(42, rom.Data[Track0Data + 1]);  // voice remapped
                Assert.Equal(120, rom.Data[Track0Data + 3]); // tempo 100+20
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // Helpers (mirror BulkTrackChangeTests).
        // ------------------------------------------------------------------

        const uint SongHeader = 0x1000;
        const uint Track0Data = 0x1100;
        const uint Track1Data = 0x1140;
        const uint Track2Data = 0x1180;

        static uint PlantSong(ROM rom)
        {
            WritePtr(rom, SongHeader + 4, 0x2000);
            rom.Data[SongHeader + 0] = 2;
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
            uint ptr = targetOffset == 0 ? 0 : targetOffset + 0x08000000;
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
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }
    }
}
